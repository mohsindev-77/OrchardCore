using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Cancelling a move: the link it created is removed and the placement it displaced is restored
/// to cover its period again, as if the move had never been recorded.
/// </summary>
/// <remarks>
/// Built on the validator, per the same instruction that shaped the merge tests: never a silent
/// delete, a dry run first, and refused when the restored placement would break a rule today.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionCancelMoveTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionCancelMoveTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly MoveDay = new(2026, 5, 1);

    private sealed record Scenario(string Structure, string Record, string OldParent, string NewParent);

    /// <summary>
    /// A section under one department, moved under a second department on <see cref="MoveDay"/>.
    /// The move under test is the one from the first department to the second.
    /// </summary>
    private static async Task<Scenario> BuildAsync(IServiceProvider services, string code)
    {
        var types = await DimensionGraphScenario.TypesAsync(services);
        var structure = await DimensionGraphScenario.StructureAsync(services, code);
        var graph = services.GetRequiredService<IDimensionGraphService>();

        var division = await DimensionGraphScenario.RecordAsync(services, types.Division, $"{code}-div", Opened);
        var oldParent = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{code}-old", Opened);
        var newParent = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{code}-new", Opened);
        var record = await DimensionGraphScenario.RecordAsync(services, types.Section, $"{code}-rec", Opened);

        foreach (var (node, parent) in new[] { (oldParent, division), (newParent, division), (record, oldParent) })
        {
            (await graph.MoveAsync(structure, node, parent, Opened)).Succeeded.Should().BeTrue();
        }

        (await graph.MoveAsync(structure, record, newParent, MoveDay)).Succeeded.Should().BeTrue();

        return new Scenario(structure, record, oldParent, newParent);
    }

    [Fact]
    public async Task TheDryRunReportsTheRestorationAndChangesNothing() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-dryrun");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var plan = await records.PlanCancelMoveAsync(scenario.Structure, scenario.Record, MoveDay);

            plan.Succeeded.Should().BeTrue(
                string.Join("; ", plan.Errors.Select(error => error.Message.Value)));

            plan.Value!.CancelledParentId.Should().Be(scenario.NewParent);
            plan.Value.RestoredParentId.Should().Be(scenario.OldParent);
            plan.Value.RestoredUntil.Should().BeNull("the cancelled move ran open ended");

            // Nothing moved. A dry run that changed anything would not be one.
            (await graph.IsUnderAsync(scenario.Structure, scenario.Record, scenario.NewParent, MoveDay))
                .Should().BeTrue("the dry run must leave the timeline exactly as it found it");
        });

    [Fact]
    public async Task TheAppliedResultMatchesTheDryRunExactly() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-matches");
            var records = services.GetRequiredService<IDimensionService>();

            var planned = await records.PlanCancelMoveAsync(scenario.Structure, scenario.Record, MoveDay);

            planned.Succeeded.Should().BeTrue();

            var applied = await records.CancelMoveAsync(
                scenario.Structure, scenario.Record, MoveDay, "undoing a mistaken transfer");

            applied.Succeeded.Should().BeTrue(
                string.Join("; ", applied.Errors.Select(error => error.Message.Value)));

            applied.Value.Should().BeEquivalentTo(
                planned.Value,
                "the report a human signed off has to be what actually happened, field for field");
        });

    [Fact]
    public async Task CancellingRestoresThePreviousPlacementForItsPeriod() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-restore");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await records.CancelMoveAsync(scenario.Structure, scenario.Record, MoveDay, "restoring the original placement"))
                .Succeeded.Should().BeTrue();

            (await graph.IsUnderAsync(scenario.Structure, scenario.Record, scenario.OldParent, MoveDay))
                .Should().BeTrue("the move never happened, so the old parent covers this date again");

            (await graph.IsUnderAsync(scenario.Structure, scenario.Record, scenario.NewParent, MoveDay))
                .Should().BeFalse("the cancelled move's parent no longer applies");

            // The restored link is open ended again, exactly as it was before the cancelled move
            // ever existed.
            (await graph.IsUnderAsync(scenario.Structure, scenario.Record, scenario.OldParent, MoveDay.AddYears(1)))
                .Should().BeTrue();
        });

    [Fact]
    public async Task TheClosureStillVerifiesAfterACancelledMove() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-verified");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await records.CancelMoveAsync(scenario.Structure, scenario.Record, MoveDay, "cleanup"))
                .Succeeded.Should().BeTrue();

            var report = await graph.VerifyAsync(scenario.Structure);

            report.IsConsistent.Should().BeTrue(
                await DimensionGraphScenario.DescribeAsync(services, report));
        });

    [Fact]
    public async Task CancellingAMovesFirstEverPlacementRestoresTheRecordToARoot() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "cnl-root");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "cnl-root-div", Opened);
            var record = await DimensionGraphScenario.RecordAsync(services, types.Department, "cnl-root-rec", Opened);

            // The record's first-ever placement: there is nothing before it to restore.
            (await graph.MoveAsync(structure, record, division, MoveDay)).Succeeded.Should().BeTrue();

            var plan = await records.PlanCancelMoveAsync(structure, record, MoveDay);

            plan.Succeeded.Should().BeTrue();
            plan.Value!.RestoredParentId.Should().BeNull("there was nothing before the record's only move");

            (await records.CancelMoveAsync(structure, record, MoveDay, "undoing the only move"))
                .Succeeded.Should().BeTrue();

            (await graph.GetAncestorsAsync(structure, record, MoveDay)).Should().BeEmpty(
                "the record is a root again, as if it had never been moved");
        });

    [Fact]
    public async Task ThereIsNoMoveRecordedOnThatDateIsRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-missing");
            var records = services.GetRequiredService<IDimensionService>();

            var refused = await records.PlanCancelMoveAsync(scenario.Structure, scenario.Record, Opened.AddDays(5));

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.MoveNotFound);
        });

    [Fact]
    public async Task CancellingRequiresAReason() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-reason");
            var records = services.GetRequiredService<IDimensionService>();

            var act = async () => await records.CancelMoveAsync(scenario.Structure, scenario.Record, MoveDay, "   ");

            await act.Should().ThrowAsync<ArgumentException>(
                "a cancellation is never a silent delete, so a reason is mandatory, not merely preferred");
        });

    [Fact]
    public async Task ARestorationOntoANowRetiredParentIsRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "cnl-retired");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // The old parent closes after the move but before the cancellation is attempted.
            // Restoring the record under it would leave a live unit under a closed one with no
            // end date, which is not a dating mistake worth only a warning.
            // The record is still under the old parent on the day it closes — the move is a year
            // later — so this retirement has to say what happens to it. Leaving it unplaced is
            // what keeps the link in place for the cancellation to try to restore.
            (await records.RetireAsync(
                scenario.OldParent,
                new DateOnly(2025, 6, 1),
                new Dictionary<string, ChildrenDisposition>(StringComparer.Ordinal)
                {
                    [scenario.Structure] = ChildrenDisposition.Unplaced,
                })).Succeeded.Should().BeTrue();

            var refused = await records.CancelMoveAsync(
                scenario.Structure, scenario.Record, MoveDay, "testing a restoration onto a retired parent");

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.ParentRetired);

            // Refused means untouched: the record still resolves under the new parent today.
            (await graph.IsUnderAsync(scenario.Structure, scenario.Record, scenario.NewParent, MoveDay))
                .Should().BeTrue("a refused cancellation must not alter the timeline");
        });

    [Fact]
    public async Task ARestorationThatWouldBreakARuleTodayIsRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var dimensionTypes = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            // A structure that allows this type inside itself today, so a unit of it can sit under
            // another one. Since ADR-0010's addendum that is the structure's grid and nothing else,
            // so this is what the test flips later — the type's own flag does not decide it.
            var flipType = await dimensionTypes.CreateAsync(
                "cnl-flip", new BilingualText("Flip", "فليب"), [], allowsSelfNesting: true);

            flipType.Succeeded.Should().BeTrue();
            var flipTypeId = flipType.Value!.DimensionTypeId;

            var structure = await structures.CreateAsync(
                "cnl-blocked",
                new BilingualText("cnl-blocked", "cnl-blocked-ar"),
                [types.Division, types.Department, flipTypeId],
                allowSkipLevel: true,
                isStrict: true,
                isPrimaryOrganisation: false);

            structure.Succeeded.Should().BeTrue();
            var structureId = structure.Value!.StructureId;

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "cnl-blk-div", Opened);
            var dept = await DimensionGraphScenario.RecordAsync(services, types.Department, "cnl-blk-dept", Opened);
            var outer = await DimensionGraphScenario.RecordAsync(services, flipTypeId, "cnl-blk-outer", Opened);
            var inner = await DimensionGraphScenario.RecordAsync(services, flipTypeId, "cnl-blk-inner", Opened);

            (await graph.MoveAsync(structureId, dept, division, Opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structureId, outer, dept, Opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structureId, inner, outer, Opened)).Succeeded.Should().BeTrue();

            // The move under test: inner leaves outer for dept directly. Valid when made, because
            // the structure allowed Flip inside Flip throughout — moving onto a different type
            // needs no such rule anyway.
            (await graph.MoveAsync(structureId, inner, dept, MoveDay)).Succeeded.Should().BeTrue();

            // The structure no longer ticks Flip → Flip, as of today. Nothing about the move above
            // is retroactively wrong, but restoring inner back under outer — the same type —
            // would be, if asked for today.
            (await structures.UpdateAsync(
                structureId,
                new BilingualText("cnl-blocked", "cnl-blocked-ar"),
                [types.Division, types.Department, flipTypeId],
                allowSkipLevel: true,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [types.Division],
                    [
                        new StructureContainment(types.Division, types.Department),
                        new StructureContainment(types.Department, flipTypeId),
                    ])))
                .Succeeded.Should().BeTrue();

            var refused = await records.CancelMoveAsync(
                structureId, inner, MoveDay, "testing a restoration that breaks a rule today");

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.ParentTypeNotPermitted);

            // Refused means untouched: the timeline the move actually produced still stands.
            (await graph.IsUnderAsync(structureId, inner, dept, MoveDay))
                .Should().BeTrue("a refused cancellation must not alter the timeline");
        });
}
