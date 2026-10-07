using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Stage D2: taking a unit off the tree is a dated decision on its record, not the absence of one.
/// </summary>
/// <remarks>
/// It used to be recorded by removing the entry that covered the date. History still resolved — the
/// displaced entry was truncated to the day before, so "where did this sit last March" was always
/// answerable — but the decision itself left no trace, and three things followed from that: cancel
/// move had nothing to offer, the unplaced panel could not tell this from a unit that had never
/// been placed, and nothing on the record said anybody had decided anything.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionLeavingTheTreeTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionLeavingTheTreeTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly Left = new(2024, 6, 1);

    [Fact]
    public async Task LeavingTheTreeIsRecordedAsAMoveWithNoParent() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenAPlacedDepartmentAsync(services, "leave-recorded");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await graph.MoveAsync(world.StructureId, world.Department, null, Left))
                .Succeeded.Should().BeTrue();

            var moves = await graph.GetRecordedMovesAsync(world.StructureId, world.Department);

            moves.Should().HaveCount(2, "the original placement, and the decision to undo it");

            var departure = moves.Single(move => move.EffectiveFrom == Left);

            departure.LeftTheTree.Should().BeTrue();
            departure.ParentRecordId.Should().BeNull();
        });

    /// <summary>
    /// The day before it left, it is still where it was; from the day itself, nowhere.
    /// </summary>
    [Fact]
    public async Task TheDayBeforeItLeftItIsStillPlacedAndFromThatDayItIsNot() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenAPlacedDepartmentAsync(services, "leave-dated");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await graph.MoveAsync(world.StructureId, world.Department, null, Left))
                .Succeeded.Should().BeTrue();

            var dayBefore = await graph.GetAncestorsAsync(world.StructureId, world.Department, Left.AddDays(-1));
            var dayOf = await graph.GetAncestorsAsync(world.StructureId, world.Department, Left);

            dayBefore.Should().ContainSingle().Which.RecordId.Should().Be(world.Division);
            dayOf.Should().BeEmpty("the dated entry says it has no parent from this day, not that it never had one");

            // And it is in the panel rather than on the tree, from that day and not before.
            (await graph.GetUnplacedAsync(world.StructureId, Left.AddDays(-1)))
                .Should().NotContain(node => node.RecordId == world.Department);

            (await graph.GetUnplacedAsync(world.StructureId, Left))
                .Should().Contain(node => node.RecordId == world.Department);
        });

    [Fact]
    public async Task ThePanelCanTellARemovedUnitFromOneThatWasNeverPlaced() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenAPlacedDepartmentAsync(services, "leave-panel");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // A second department that has never been anywhere.
            var neverPlaced = await DimensionGraphScenario.RecordAsync(
                services, world.DepartmentTypeId, "leave-panel-never", Opened);

            (await graph.MoveAsync(world.StructureId, world.Department, null, Left))
                .Succeeded.Should().BeTrue();

            var removed = await graph.GetRemovedFromTreeAsync(
                world.StructureId, [world.Department, neverPlaced], Left);

            removed.Should().ContainKey(world.Department).WhoseValue.Should().Be(Left);
            removed.Should().NotContainKey(neverPlaced,
                "a record nobody ever placed was not removed from anything");

            // And not before the day it happened: "removed on" is only true of a date it has
            // happened by.
            (await graph.GetRemovedFromTreeAsync(world.StructureId, [world.Department], Left.AddDays(-1)))
                .Should().NotContainKey(world.Department);
        });

    [Fact]
    public async Task PlacingItBackEndsTheNoParentPeriodRatherThanRewritingIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenAPlacedDepartmentAsync(services, "leave-return");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var returned = new DateOnly(2024, 9, 1);

            (await graph.MoveAsync(world.StructureId, world.Department, null, Left)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(world.StructureId, world.Department, world.Division, returned)).Succeeded.Should().BeTrue();

            // Three decisions, in order, each still on the record.
            var moves = (await graph.GetRecordedMovesAsync(world.StructureId, world.Department))
                .OrderBy(move => move.EffectiveFrom)
                .ToList();

            moves.Select(move => move.EffectiveFrom).Should().Equal([Opened, Left, returned]);
            moves[1].LeftTheTree.Should().BeTrue();
            moves[2].LeftTheTree.Should().BeFalse();

            // And the gap is a gap: off the tree in the middle, on it either side.
            (await graph.GetAncestorsAsync(world.StructureId, world.Department, Left.AddDays(1)))
                .Should().BeEmpty();

            (await graph.GetAncestorsAsync(world.StructureId, world.Department, returned))
                .Should().ContainSingle().Which.RecordId.Should().Be(world.Division);
        });

    [Fact]
    public async Task CancellingADepartureRestoresWhatItDisplaced() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenAPlacedDepartmentAsync(services, "leave-cancel");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await graph.MoveAsync(world.StructureId, world.Department, null, Left)).Succeeded.Should().BeTrue();

            var preview = await graph.PreviewCancelMoveAsync(world.StructureId, world.Department, Left);

            preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Errors.Select(e => e.Message.Value)));
            preview.Value!.CancelledParentId.Should().BeNull("what is being cancelled is the departure itself");
            preview.Value.RestoredParentId.Should().Be(world.Division);

            var cancelled = await graph.CancelMoveAsync(world.StructureId, world.Department, Left);

            cancelled.Succeeded.Should().BeTrue(string.Join("; ", cancelled.Errors.Select(e => e.Message.Value)));

            // Back under its division on the day it had left, and the entry is gone from the record.
            (await graph.GetAncestorsAsync(world.StructureId, world.Department, Left))
                .Should().ContainSingle().Which.RecordId.Should().Be(world.Division);

            (await graph.GetRecordedMovesAsync(world.StructureId, world.Department))
                .Should().ContainSingle().Which.LeftTheTree.Should().BeFalse();
        });

    /// <summary>
    /// A record that has never been placed and is placed nowhere gets no entry at all.
    /// </summary>
    /// <remarks>
    /// The one case that must not write a decision: every imported record is in this state, and
    /// recording "no parent from today" on each of them would put the whole import into the
    /// panel's "removed from the tree" state for a removal nobody performed.
    /// </remarks>
    [Fact]
    public async Task AUnitThatWasNeverPlacedGainsNoEntryFromBeingPlacedNowhere() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenAPlacedDepartmentAsync(services, "leave-noop");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var neverPlaced = await DimensionGraphScenario.RecordAsync(
                services, world.DepartmentTypeId, "leave-noop-never", Opened);

            await graph.MoveAsync(world.StructureId, neverPlaced, null, Left);

            (await graph.GetRecordedMovesAsync(world.StructureId, neverPlaced))
                .Should().BeEmpty("there was no decision to undo, so there is none to record");

            (await graph.GetRemovedFromTreeAsync(world.StructureId, [neverPlaced], Left))
                .Should().NotContainKey(neverPlaced);
        });

    // ---- scaffolding --------------------------------------------------------------------

    private sealed record World(string StructureId, string Division, string Department, string DepartmentTypeId);

    private static async Task<World> GivenAPlacedDepartmentAsync(IServiceProvider services, string prefix)
    {
        var types = await DimensionGraphScenario.TypesAsync(services);
        var structureId = await DimensionGraphScenario.StructureAsync(services, $"{prefix}-org");
        var graph = services.GetRequiredService<IDimensionGraphService>();

        var division = await DimensionGraphScenario.RecordAsync(services, types.Division, $"{prefix}-div", Opened);
        var department = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{prefix}-dept", Opened);

        (await graph.MoveAsync(structureId, department, division, Opened)).Succeeded.Should().BeTrue();

        return new World(structureId, division, department, types.Department);
    }
}
