using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Environment.Shell;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Internal;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The per-tenant lookup list cache: that it caches, that a write invalidates it, and that the key
/// it caches under names the tenant.
/// </summary>
/// <remarks>
/// <c>RecordsCacheKeyTests</c> asserts the shape of a key with no shell involved, which is the
/// right level for pure string construction. This is the part it cannot answer: that a real cached
/// read, through the real <c>IMemoryCache</c> and the real <c>ISignal</c>, hands back what the
/// tenant actually holds after a write — and that the entry is sitting under the tenant-qualified
/// key rather than a bare one.
///
/// <b>The failure this guards is invisible rather than loud.</b> A cache that is never invalidated
/// does not throw: it goes on offering a retired grade, or withholding a new one, until the process
/// restarts. On a screen where the whole point is to offer the right set of choices, that is the
/// feature not working while appearing to.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class LookupListCachingTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public LookupListCachingTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A read puts the list in the cache, under a key that names this tenant.
    /// </summary>
    /// <remarks>
    /// The key is rebuilt here from <see cref="ShellSettings.Name"/> rather than hard-coded, so
    /// this asserts that the key the cached lookup used and the key the rest of the module would
    /// build are the same one. Specification section 9's rule is that every cache key begins with
    /// the tenant name, and <c>IMemoryCache</c> is a process-wide singleton: a key without it is
    /// not a key that fails, it is a key two customers share.
    /// </remarks>
    [Fact]
    public async Task AReadIsCachedUnderTheTenantsOwnKey() =>
        await InTenantAsync(async services =>
        {
            var tenant = services.GetRequiredService<ShellSettings>().Name;
            var cache = services.GetRequiredService<IMemoryCache>();

            var key = RecordsCacheKeys.LookupListByCode(tenant, StandardLookupLists.Grade.ToUpperInvariant());

            await services.GetRequiredService<ILookupListLookup>()
                .GetByCodeAsync(StandardLookupLists.Grade);

            cache.TryGetValue(key, out LookupListDocument? cached).Should().BeTrue(
                "the read goes through the cache, under the key the rest of the module builds");

            cached!.Code.Should().Be(StandardLookupLists.Grade);

            key.Should().Contain(tenant);
        });

    /// <summary>
    /// A list added after a cached list-read is visible on the next one.
    /// </summary>
    [Fact]
    public async Task CreatingAListIsVisibleInAnAlreadyCachedList() =>
        await InTenantAsync(async services =>
        {
            var lookup = services.GetRequiredService<ILookupListLookup>();

            // Warms the cache.
            var before = await lookup.ListAsync();

            before.Should().NotContain(list => list.Code == "cache-created");

            var created = await services.GetRequiredService<ILookupListService>().CreateAsync(
                "cache-created",
                new BilingualText("Cache created", "منشأة"),
                []);

            created.Succeeded.Should().BeTrue(created.Describe());

            (await lookup.ListAsync()).Should().Contain(
                list => list.Code == "cache-created",
                "the write signalled the token every cached read is tagged with");
        });

    /// <summary>
    /// An entry added to a list is visible on the next cached read of that list.
    /// </summary>
    /// <remarks>
    /// The case that matters most in practice, because it is the one a person watches happen: they
    /// add a grade on the lookup list screen and then open an employee record expecting to choose
    /// it.
    /// </remarks>
    [Fact]
    public async Task AnEntryAddedToAListIsVisibleOnTheNextCachedRead() =>
        await InTenantAsync(async services =>
        {
            var lookup = services.GetRequiredService<ILookupListLookup>();
            var lists = services.GetRequiredService<ILookupListService>();

            var created = await lists.CreateAsync(
                "cache-entries",
                new BilingualText("Cache entries", "مدخلات"),
                [new LookupListItem("A", new BilingualText("Alpha", "ألفا"), SortOrder: 10)]);

            created.Succeeded.Should().BeTrue(created.Describe());

            // Warms the cache for this list specifically.
            (await lookup.GetByCodeAsync("cache-entries"))!.Items.Should().ContainSingle();

            var updated = await lists.UpdateAsync(
                created.Value!.LookupListId,
                created.Value.Name,
                [
                    .. created.Value.Items,
                    new LookupListItem("B", new BilingualText("Bravo", "برافو"), SortOrder: 20),
                ]);

            updated.Succeeded.Should().BeTrue(updated.Describe());

            (await lookup.GetByCodeAsync("cache-entries"))!.Items.Should().HaveCount(
                2, "a cached read after a write must not be the read from before it");
        });

    /// <summary>
    /// Retiring an entry reaches the control that offers it.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ILookupChoiceProvider"/> rather than through the lookup, because that is
    /// what a screen actually asks, and it is the layer where a stale cache would show up as a
    /// retired entry still being offered. The two are one call apart and the gap is exactly where
    /// a caching mistake would hide.
    /// </remarks>
    [Fact]
    public async Task RetiringAnEntryStopsItBeingOfferedOnTheNextRender() =>
        await InTenantAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();
            var choices = services.GetRequiredService<ILookupChoiceProvider>();

            var created = await lists.CreateAsync(
                "cache-retire",
                new BilingualText("Cache retire", "تقاعد"),
                [
                    new LookupListItem("KEEP", new BilingualText("Keep", "إبقاء"), SortOrder: 10),
                    new LookupListItem("GONE", new BilingualText("Gone", "ذهب"), SortOrder: 20),
                ]);

            created.Succeeded.Should().BeTrue(created.Describe());

            (await choices.ForAsync("cache-retire", storedValue: "")).Options
                .Should().HaveCount(2);

            var retired = await lists.UpdateAsync(
                created.Value!.LookupListId,
                created.Value.Name,
                [.. created.Value.Items.Select(item => item.Code == "GONE" ? item with { IsActive = false } : item)]);

            retired.Succeeded.Should().BeTrue(retired.Describe());

            (await choices.ForAsync("cache-retire", storedValue: "")).Options
                .Should().ContainSingle("a retired entry stops being offered as soon as it is retired")
                .Which.Value.Should().Be("KEEP");
        });

    /// <summary>
    /// Deleting a list clears it from the cache.
    /// </summary>
    /// <remarks>
    /// The invalidation that is easiest to leave out, because deletion is the rarest write — and
    /// the one where a stale cache keeps offering a list that no longer exists.
    /// </remarks>
    [Fact]
    public async Task DeletingAListClearsItFromTheCache()
    {
        string listId = string.Empty;

        await InTenantAsync(async services =>
        {
            var created = await services.GetRequiredService<ILookupListService>().CreateAsync(
                "cache-deleted", new BilingualText("Cache deleted", "محذوفة"), []);

            created.Succeeded.Should().BeTrue(created.Describe());
            listId = created.Value!.LookupListId;

            // Warms the cache.
            (await services.GetRequiredService<ILookupListLookup>().GetByCodeAsync("cache-deleted"))
                .Should().NotBeNull();
        });

        await InTenantAsync(async services =>
        {
            var deleted = await services.GetRequiredService<ILookupListService>().DeleteAsync(listId);

            deleted.Succeeded.Should().BeTrue(deleted.Describe());

            (await services.GetRequiredService<ILookupListLookup>().GetByCodeAsync("cache-deleted"))
                .Should().BeNull();
        });
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup list caching test"))
            {
                await work(services);
            }
        });
}
