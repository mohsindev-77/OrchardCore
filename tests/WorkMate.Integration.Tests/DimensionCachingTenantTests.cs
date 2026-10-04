using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Per-tenant caching of dimension types and structures: read constantly, written rarely, and
/// every write invalidates what it could possibly affect.
/// </summary>
/// <remarks>
/// The tenant-qualified key shape itself is pinned directly in
/// <c>DimensionCacheKeysTests</c>, with no shell involved. What this file proves is the thing a
/// pure key test cannot: that a real cached read, through the real <c>IMemoryCache</c> and the
/// real <c>ISignal</c> wiring, actually reflects a write made after it was cached — one test per
/// kind of mutation, per the module README's caching section.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionCachingTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionCachingTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task CreatingADimensionTypeIsVisibleInAnAlreadyCachedList() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            // Warms the list cache before the type being asserted on exists.
            await types.ListAsync(includeRetired: true);

            var created = await types.CreateAsync(
                "cache-type-create", new BilingualText("Cache create", "اختبار الإنشاء"), [], allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

            var after = await types.ListAsync(includeRetired: true);

            after.Select(type => type.Code).Should().Contain(
                "cache-type-create",
                "creating a type must invalidate the cached list, not leave the pre-creation snapshot standing");
        });

    [Fact]
    public async Task UpdatingADimensionTypeIsVisibleOnTheNextCachedRead() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var created = await types.CreateAsync(
                "cache-type-update", new BilingualText("Before", "قبل"), [], allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue();
            var id = created.Value!.DimensionTypeId;

            (await types.GetAsync(id))!.Name.En.Should().Be("Before"); // warms the cache

            var updated = await types.UpdateAsync(id, new BilingualText("After", "بعد"), [], allowsSelfNesting: false);

            updated.Succeeded.Should().BeTrue(string.Join("; ", updated.Errors.Select(e => e.Message.Value)));

            (await types.GetAsync(id))!.Name.En.Should().Be(
                "After", "a cached read after an update must see the change, not the value cached before it");
        });

    [Fact]
    public async Task RetiringADimensionTypeIsVisibleOnTheNextCachedRead() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var authorisation = services.GetRequiredService<IDimensionAuthorisation>();

            var created = await types.CreateAsync(
                "cache-type-retire", new BilingualText("Cache retire", "اختبار الإيقاف"), [], allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue();
            var id = created.Value!.DimensionTypeId;

            (await types.GetAsync(id)).Should().NotBeNull(); // warms the cache, current

            var retired = await types.RetireAsync(id, await authorisation.TodayAsync());

            retired.Succeeded.Should().BeTrue(string.Join("; ", retired.Errors.Select(e => e.Message.Value)));

            (await types.GetAsync(id)).Should().BeNull(
                "a cached read after a retirement must see it, not the still-current value cached before it");
        });

    [Fact]
    public async Task CreatingAStructureIsVisibleInAnAlreadyCachedList() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();

            await structures.ListAsync(); // warms the list cache

            var created = await structures.CreateAsync(
                "cache-structure-create",
                new BilingualText("Cache structure create", "اختبار"),
                [types.Division],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

            var after = await structures.ListAsync();

            after.Select(structure => structure.Code).Should().Contain(
                "cache-structure-create",
                "creating a structure must invalidate the cached list, not leave the pre-creation snapshot standing");
        });

    [Fact]
    public async Task ChangingAStructuresLevelsIsVisibleOnTheNextCachedRead() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();

            var created = await structures.CreateAsync(
                "cache-structure-levels",
                new BilingualText("Cache structure levels", "اختبار"),
                [types.Division],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
            var id = created.Value!.StructureId;

            (await structures.GetAsync(id))!.Levels.Should().HaveCount(1); // warms the cache

            var updated = await structures.UpdateAsync(
                id,
                created.Value.Name,
                [types.Division, types.Department],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            updated.Succeeded.Should().BeTrue(string.Join("; ", updated.Errors.Select(e => e.Message.Value)));

            (await structures.GetAsync(id))!.Levels.Should().HaveCount(
                2, "a cached read after the structure's levels change must see the new levels");
        });

    [Fact]
    public async Task RecordAndGraphMutationsDoNotDisturbTheCachedTypeOrStructureReads() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // Move, merge, cancel a move and rename all write records and graph tables, none of
            // which this module caches: dated graph queries are left out of the cache until a
            // performance test shows a need (see the README). This pins the other half of that
            // decision — that none of these operations needlessly invalidates the separately
            // scoped dimension-type and structure caches either, since that would defeat the
            // point of caching data that changes rarely.
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var scenarioTypes = await DimensionGraphScenario.TypesAsync(services);
            var structureId = await DimensionGraphScenario.StructureAsync(services, "cache-untouched");

            var typeNameBefore = (await types.GetAsync(scenarioTypes.Division))!.Name.En; // warms
            var structureCodeBefore = (await structures.GetAsync(structureId))!.Code; // warms

            var division = await DimensionGraphScenario.RecordAsync(services, scenarioTypes.Division, "cache-untouched-div", Opened);
            var deptA = await DimensionGraphScenario.RecordAsync(services, scenarioTypes.Department, "cache-untouched-a", Opened);
            var deptB = await DimensionGraphScenario.RecordAsync(services, scenarioTypes.Department, "cache-untouched-b", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, scenarioTypes.Section, "cache-untouched-sec", Opened);
            var mergeSource = await DimensionGraphScenario.RecordAsync(services, scenarioTypes.Department, "cache-untouched-src", Opened);
            var mergeTarget = await DimensionGraphScenario.RecordAsync(services, scenarioTypes.Department, "cache-untouched-tgt", Opened);

            foreach (var node in new[] { deptA, deptB, mergeSource, mergeTarget })
            {
                (await graph.MoveAsync(structureId, node, division, Opened)).Succeeded.Should().BeTrue();
            }

            (await graph.MoveAsync(structureId, section, deptA, Opened)).Succeeded.Should().BeTrue();

            var moveDay = new DateOnly(2024, 6, 1);

            (await graph.MoveAsync(structureId, section, deptB, moveDay)).Succeeded.Should().BeTrue(); // move
            (await records.RenameAsync(deptA, new BilingualText("Renamed", "أعيدت تسميته"), moveDay))
                .Succeeded.Should().BeTrue(); // rename
            (await records.CancelMoveAsync(structureId, section, moveDay, "testing cache isolation"))
                .Succeeded.Should().BeTrue(); // cancel a move
            (await records.MergeAsync(structureId, mergeSource, mergeTarget, moveDay))
                .Succeeded.Should().BeTrue(); // merge

            (await types.GetAsync(scenarioTypes.Division))!.Name.En.Should().Be(
                typeNameBefore, "nothing about a record or the graph changes a dimension type");

            (await structures.GetAsync(structureId))!.Code.Should().Be(
                structureCodeBefore, "nothing about a record or the graph changes a structure's own document");
        });
}
