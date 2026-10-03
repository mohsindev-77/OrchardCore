using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The closure follows the structure's levels, not only its records.
/// </summary>
/// <remarks>
/// Adding a level is retrospective by nature: the records of that dimension type already exist
/// and they are part of the axis the moment the level is declared. Removing one is the mirror
/// case, and it closes rather than deletes, so "who was under this node last March" stays
/// answerable after the level has gone.
///
/// Neither of these is in the architecture document explicitly; both follow from it, and both
/// were raised in review because a tenant that reorganises its axes is the normal case rather
/// than the exotic one.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionStructureLevelTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionStructureLevelTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task ANewAxisPicksUpTheRecordsThatAlreadyExist() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // The record exists before the axis does.
            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "lv-before", Opened);

            var structure = await DimensionGraphScenario.StructureAsync(services, "lvl-newaxis");

            // It must be on the new axis as a root, not invisible on an axis it belongs to.
            (await graph.GetDepthAsync(structure, division)).Should().Be(
                0,
                "a record whose type is a level of the axis is on that axis, whenever it was created");

            {
                var report = await graph.VerifyAsync(structure);
                var codes = await DimensionGraphScenario.DescribeAsync(services, report);
                report.IsConsistent.Should().BeTrue(codes);
            }
        });

    [Fact]
    public async Task AddingALevelGivesItsExistingRecordsSelfPairs() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // An axis of divisions only, with a department record that is not on it yet.
            var created = await structures.CreateAsync(
                "lvl-added",
                new BilingualText("Added", "مضاف"),
                [types.Division],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            created.Succeeded.Should().BeTrue();

            var structure = created.Value!.StructureId;
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "lv-added-dep", Opened);

            (await graph.GetDepthAsync(structure, department)).Should().BeNull(
                "the department's type is not a level of this axis yet");

            var updated = await structures.UpdateAsync(
                structure,
                new BilingualText("Added", "مضاف"),
                [types.Division, types.Department],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            updated.Succeeded.Should().BeTrue(
                string.Join("; ", updated.Errors.Select(error => error.Message.Value)));

            (await graph.GetDepthAsync(structure, department)).Should().Be(
                0,
                "declaring the level puts every record of that type on the axis");

            {
                var report = await graph.VerifyAsync(structure);
                var codes = await DimensionGraphScenario.DescribeAsync(services, report);
                report.IsConsistent.Should().BeTrue(codes);
            }
        });

    [Fact]
    public async Task RemovingALevelClosesItsRowsAndLeavesHistoryIntact() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var created = await structures.CreateAsync(
                "lvl-removed",
                new BilingualText("Removed", "محذوف"),
                [types.Division, types.Department, types.Section],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            created.Succeeded.Should().BeTrue();

            var structure = created.Value!.StructureId;

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "lv-rem-div", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "lv-rem-sec", Opened);

            (await graph.MoveAsync(structure, section, division, Opened)).Succeeded.Should().BeTrue();
            (await graph.IsUnderAsync(structure, section, division, Opened)).Should().BeTrue();

            // Drop the section level. Today it is gone from the axis; the day before the change
            // it is still there, because the organisation really did look like that.
            var yesterday = (await services.GetRequiredService<IDimensionAuthorisation>().TodayAsync()).AddDays(-1);

            var updated = await structures.UpdateAsync(
                structure,
                new BilingualText("Removed", "محذوف"),
                [types.Division, types.Department],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            updated.Succeeded.Should().BeTrue(
                string.Join("; ", updated.Errors.Select(error => error.Message.Value)));

            (await graph.IsUnderAsync(structure, section, division, Opened)).Should().BeTrue(
                "removing a level is not a deletion: last year's report must still resolve");

            (await graph.IsUnderAsync(structure, section, division, yesterday)).Should().BeTrue(
                "the day before the change, the section was still on the axis");

            (await graph.GetDepthAsync(structure, section)).Should().BeNull(
                "from the day the level went, the section is no longer on this axis");

            {
                var report = await graph.VerifyAsync(structure);
                var codes = await DimensionGraphScenario.DescribeAsync(services, report);
                report.IsConsistent.Should().BeTrue(codes);
            }
        });

    [Fact]
    public async Task RemovingALevelLeavesTheOtherLevelsAlone() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var created = await structures.CreateAsync(
                "lvl-survivors",
                new BilingualText("Survivors", "الباقون"),
                [types.Division, types.Department, types.Section],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false);

            created.Succeeded.Should().BeTrue();

            var structure = created.Value!.StructureId;

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "lv-sv-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "lv-sv-dep", Opened);

            (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();

            (await structures.UpdateAsync(
                structure,
                new BilingualText("Survivors", "الباقون"),
                [types.Division, types.Department],
                allowSkipLevel: true,
                isStrict: false,
                isPrimaryOrganisation: false)).Succeeded.Should().BeTrue();

            (await graph.IsUnderAsync(structure, department, division)).Should().BeTrue(
                "the section level went; the department level did not");

            {
                var report = await graph.VerifyAsync(structure);
                var codes = await DimensionGraphScenario.DescribeAsync(services, report);
                report.IsConsistent.Should().BeTrue(codes);
            }
        });
}
