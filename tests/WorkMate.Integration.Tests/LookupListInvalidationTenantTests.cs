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
/// The defect ADR-0019 was written from: a read that overlaps a write must not leave the cache
/// holding the state from before the write for ever.
/// </summary>
/// <remarks>
/// <b>This is the test that reproduces the manual-testing failure.</b> On a tenant created before
/// slice B2 an operator applied the standard lookup lists recipe; the ten lists were written, and
/// every bound field went on rendering a free-text box until the application was restarted.
///
/// <c>LookupListCachingTenantTests</c> already proved that a write invalidates the cache, and it
/// passed throughout — because it writes and reads in sequence, in one scope. The failure needs the
/// two to overlap, which is the normal condition of a web application and the guaranteed condition
/// of a recipe that makes ten writes in one scope while the operator's browser is making requests
/// through it. <c>ILookupListService</c> signalled the change token as the write was staged, and a
/// read arriving before the commit refilled the cache from the state the write was about to
/// replace, under a token that had already been used.
///
/// The two assertions are deliberately different in kind. The first pins the mechanism — the entry
/// is correct after the commit however it got there. The second pins the symptom, through
/// <see cref="ILookupChoiceProvider"/>, which is what a screen actually asks and where this showed
/// up as a free-text box.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class LookupListInvalidationTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public LookupListInvalidationTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AReadThatOverlapsAWriteDoesNotLeaveTheCacheHoldingTheStateFromBeforeIt()
    {
        const string Code = "overlap-create";

        await WriteWithAnOverlappingReadAsync(Code, services =>
            services.GetRequiredService<ILookupListService>().CreateAsync(
                Code,
                new BilingualText("Overlapping create", "إنشاء متزامن"),
                [new LookupListItem("A", new BilingualText("Alpha", "ألفا"), SortOrder: 10)]));

        await InTenantAsync(async services =>
        {
            var cached = await services.GetRequiredService<ILookupListLookup>().GetByCodeAsync(Code);

            cached.Should().NotBeNull(
                "the list exists once the write committed, and the cached read is what every screen "
                + "goes through — a cache still answering 'no such list' here is the defect, and it "
                + "lasts until the process restarts");

            cached!.Items.Should().ContainSingle();
        });
    }

    /// <summary>
    /// The same race against a list that already exists, which is the case only the deferred signal
    /// can win.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that is load-bearing for ADR-0019 itself.</b> The two either side of it
    /// would also pass with the immediate signal and nothing but "an absence is never cached",
    /// because the value the overlapping reader picks up is an absence. Here the reader picks up a
    /// real, committed, perfectly cacheable list — the version from before the update — and the only
    /// thing that can evict it afterwards is a signal sent after the commit. Verified by putting
    /// <c>SignalTokenAsync</c> back: this fails, the other two do not.
    /// </remarks>
    [Fact]
    public async Task AnUpdateReadOverIsNotLeftInTheCacheAsTheVersionFromBeforeIt()
    {
        const string Code = "overlap-update";

        string listId = string.Empty;

        await InTenantAsync(async services =>
        {
            var created = await services.GetRequiredService<ILookupListService>().CreateAsync(
                Code,
                new BilingualText("Overlapping update", "تحديث متزامن"),
                [new LookupListItem("ONE", new BilingualText("One", "واحد"), SortOrder: 10)]);

            created.Succeeded.Should().BeTrue(created.Describe());
            listId = created.Value!.LookupListId;
        });

        await WriteWithAnOverlappingReadAsync(Code, async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();
            var document = (await lists.GetAsync(listId))!;

            return await lists.UpdateAsync(
                listId,
                document.Name,
                [
                    .. document.Items,
                    new LookupListItem("TWO", new BilingualText("Two", "اثنان"), SortOrder: 20),
                ]);
        });

        await InTenantAsync(async services =>
        {
            var cached = await services.GetRequiredService<ILookupListLookup>().GetByCodeAsync(Code);

            cached!.Items.Should().HaveCount(
                2,
                "the overlapping read cached the committed one-entry version, and only a signal sent "
                + "after the commit can evict it");
        });
    }

    /// <summary>
    /// The same race, seen the way a screen sees it: the field goes on offering free text.
    /// </summary>
    [Fact]
    public async Task AFieldBoundToAListWrittenUnderAnOverlappingReadStopsBeingFreeText()
    {
        const string Code = "overlap-bound";

        await WriteWithAnOverlappingReadAsync(Code, services =>
            services.GetRequiredService<ILookupListService>().CreateAsync(
                Code,
                new BilingualText("Overlapping bound", "حقل متزامن"),
                [
                    new LookupListItem("KEEP", new BilingualText("Keep", "إبقاء"), SortOrder: 10),
                    new LookupListItem("ALSO", new BilingualText("Also", "أيضا"), SortOrder: 20),
                ]));

        await InTenantAsync(async services =>
        {
            var choices = await services.GetRequiredService<ILookupChoiceProvider>()
                .ForAsync(Code, storedValue: "");

            choices.IsBound.Should().BeTrue(
                "an unbound answer is what renders a free-text box, which is how this defect looked "
                + "to the person testing it");

            choices.Options.Should().HaveCount(2);
        });
    }

    /// <summary>
    /// The second line ADR-0019 adds in this module: "there is no such list" is never cached.
    /// </summary>
    /// <remarks>
    /// Asserted on the cache itself rather than through behaviour, because the behaviour of a
    /// correctly invalidated cache and of one that never stored the answer are identical — which is
    /// the point of having both. A test that could not tell them apart would pass if this were
    /// quietly removed.
    /// </remarks>
    [Fact]
    public async Task AListThatDoesNotExistIsNotCached() =>
        await InTenantAsync(async services =>
        {
            var tenant = services.GetRequiredService<ShellSettings>().Name;
            var cache = services.GetRequiredService<IMemoryCache>();
            var key = RecordsCacheKeys.LookupListByCode(tenant, "NO-SUCH-LIST");

            (await services.GetRequiredService<ILookupListLookup>().GetByCodeAsync("no-such-list"))
                .Should().BeNull();

            cache.TryGetValue(key, out LookupListDocument? _).Should().BeFalse(
                "a cached absence is the one wrong answer on this path that looks exactly like "
                + "correct behaviour, so it is never stored");
        });

    /// <summary>
    /// Runs <paramref name="write"/> in one shell scope and a cached read of the same list in
    /// another, overlapping it: the read happens after the write is staged and before the writing
    /// scope is disposed, which is when YesSql commits.
    /// </summary>
    /// <remarks>
    /// Two real scopes rather than a simulation, because the thing under test is the interaction
    /// between Orchard's scope disposal, YesSql's commit and the change token — none of which a
    /// double would reproduce. The handshake is explicit so the overlap is a fact of the test
    /// rather than a matter of timing.
    /// </remarks>
    private async Task WriteWithAnOverlappingReadAsync(
        string code,
        Func<IServiceProvider, Task<RecordResult<LookupListDocument>>> write)
    {
        var staged = new TaskCompletionSource();
        var read = new TaskCompletionSource();

        var writer = Task.Run(() => InTenantAsync(async services =>
        {
            var result = await write(services);

            result.Succeeded.Should().BeTrue(result.Describe());

            // Staged and signalled, but nothing is committed until this scope is disposed.
            staged.SetResult();

            await read.Task;
        }));

        var reader = Task.Run(async () =>
        {
            await staged.Task;

            try
            {
                await InTenantAsync(services =>
                    services.GetRequiredService<ILookupListLookup>().GetByCodeAsync(code));
            }
            finally
            {
                read.SetResult();
            }
        });

        await Task.WhenAll(writer, reader);
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup list invalidation test"))
            {
                await work(services);
            }
        });
}
