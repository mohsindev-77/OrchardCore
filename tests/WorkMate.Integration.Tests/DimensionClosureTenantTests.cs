using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The dated closure index: that a closure row's range really is the intersection of the link
/// ranges along the path, and that it stays right through backdated moves, future-dated moves
/// and repeated moves of the same node.
/// </summary>
/// <remarks>
/// ADR-0005 departs from the architecture's four-column closure by carrying an effective range
/// on every row. These are the tests that earn that departure. Architecture section 4 names the
/// failure this guards against: "An index that has silently drifted from the links is the worst
/// failure mode in this design, because every downstream number stays plausible while being
/// wrong."
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionClosureTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionClosureTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly Midpoint = new(2025, 7, 1);

    [Fact]
    public async Task AClosureRangeIsTheIntersectionOfTheLinksAlongThePath() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-intersect");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // A division from 2024, a department that joins it in 2025, and a section that
            // joins the department in 2026. The section is only under the division from 2026 —
            // the latest start on the path wins, which is what intersection means.
            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "ci-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "ci-dep", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "ci-sec", Opened);

            (await graph.MoveAsync(structure, department, division, new DateOnly(2025, 1, 1))).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, section, department, new DateOnly(2026, 1, 1))).Succeeded.Should().BeTrue();

            // The near edge: the section is under the department from 2026.
            (await graph.IsUnderAsync(structure, section, department, new DateOnly(2025, 12, 31))).Should().BeFalse();
            (await graph.IsUnderAsync(structure, section, department, new DateOnly(2026, 1, 1))).Should().BeTrue();

            // The far edge: the intersection with the department's own link to the division.
            (await graph.IsUnderAsync(structure, section, division, new DateOnly(2025, 12, 31))).Should().BeFalse(
                "the section was not yet under the department, so it was not under the division either");

            (await graph.IsUnderAsync(structure, section, division, new DateOnly(2026, 1, 1))).Should().BeTrue();

            (await graph.GetDepthAsync(structure, section, new DateOnly(2026, 1, 1))).Should().Be(2);
        });

    [Fact]
    public async Task AParentJoiningLaterLimitsTheGrandparentRelationship() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-latejoin");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "lj-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "lj-dep", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "lj-sec", Opened);

            // This time the section joins first and the department joins the division later.
            // The section's relationship to the division starts on the later of the two.
            (await graph.MoveAsync(structure, section, department, new DateOnly(2024, 6, 1))).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, department, division, new DateOnly(2025, 3, 1))).Succeeded.Should().BeTrue();

            (await graph.IsUnderAsync(structure, section, department, new DateOnly(2024, 6, 1))).Should().BeTrue();

            (await graph.IsUnderAsync(structure, section, division, new DateOnly(2025, 2, 28))).Should().BeFalse(
                "the department was not under the division yet");

            (await graph.IsUnderAsync(structure, section, division, new DateOnly(2025, 3, 1))).Should().BeTrue();
        });

    [Fact]
    public async Task AFutureDatedMoveResolvesAsEmptyTodayAndCorrectlyOnTheDay() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-future");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "fu-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "fu-dep", Opened);

            var reorganisation = new DateOnly(2030, 1, 1);

            (await graph.MoveAsync(structure, department, division, reorganisation)).Succeeded.Should().BeTrue();

            // Pre-building next year's structure is legitimate and must not show up early.
            (await graph.IsUnderAsync(structure, department, division, reorganisation.AddDays(-1))).Should().BeFalse();
            (await graph.IsUnderAsync(structure, department, division, reorganisation)).Should().BeTrue();

            var todaysChildren = await graph.GetChildrenAsync(structure, division, new DateOnly(2026, 10, 2));
            todaysChildren.Should().BeEmpty("the reorganisation has not happened yet");

            var onTheDay = await graph.GetChildrenAsync(structure, division, reorganisation);
            onTheDay.Select(child => child.RecordId).Should().Contain(department);
        });

    [Fact]
    public async Task ABackdatedMoveRewritesOnlyThePeriodItCovers() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-backdated");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var first = await DimensionGraphScenario.RecordAsync(services, types.Division, "bd-one", Opened);
            var second = await DimensionGraphScenario.RecordAsync(services, types.Division, "bd-two", Opened);
            var third = await DimensionGraphScenario.RecordAsync(services, types.Division, "bd-three", Opened);
            var node = await DimensionGraphScenario.RecordAsync(services, types.Department, "bd-node", Opened);

            // Under the first from 2024, moved to the second in July 2025.
            (await graph.MoveAsync(structure, node, first, Opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, node, second, Midpoint)).Succeeded.Should().BeTrue();

            // Now a correction: it was actually under the third from March 2025. This must
            // change only the period between March and the July move, and leave July onwards
            // pointing at the second.
            (await graph.MoveAsync(structure, node, third, new DateOnly(2025, 3, 1))).Succeeded.Should().BeTrue();

            (await graph.IsUnderAsync(structure, node, first, new DateOnly(2025, 2, 28))).Should().BeTrue(
                "the period before the correction is untouched");

            (await graph.IsUnderAsync(structure, node, third, new DateOnly(2025, 3, 1))).Should().BeTrue();
            (await graph.IsUnderAsync(structure, node, third, Midpoint.AddDays(-1))).Should().BeTrue();

            (await graph.IsUnderAsync(structure, node, second, Midpoint)).Should().BeTrue(
                "the later move survives a backdated correction; it is not swallowed by it");

            (await graph.IsUnderAsync(structure, node, third, Midpoint)).Should().BeFalse();
            (await graph.IsUnderAsync(structure, node, first, new DateOnly(2025, 3, 1))).Should().BeFalse();
        });

    [Fact]
    public async Task TwoMovesOfTheSameNodeLeaveThreeContiguousPeriods() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-twomoves");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var a = await DimensionGraphScenario.RecordAsync(services, types.Division, "tm-a", Opened);
            var b = await DimensionGraphScenario.RecordAsync(services, types.Division, "tm-b", Opened);
            var c = await DimensionGraphScenario.RecordAsync(services, types.Division, "tm-c", Opened);
            var node = await DimensionGraphScenario.RecordAsync(services, types.Department, "tm-node", Opened);

            var toB = new DateOnly(2025, 1, 1);
            var toC = new DateOnly(2026, 1, 1);

            (await graph.MoveAsync(structure, node, a, Opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, node, b, toB)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, node, c, toC)).Succeeded.Should().BeTrue();

            // The day before and after each change, which is where dated logic goes wrong.
            await AssertParent(a, Opened);
            await AssertParent(a, toB.AddDays(-1));
            await AssertParent(b, toB);
            await AssertParent(b, toC.AddDays(-1));
            await AssertParent(c, toC);
            await AssertParent(c, toC.AddDays(1));

            async Task AssertParent(string expected, DateOnly date)
            {
                var ancestors = await graph.GetAncestorsAsync(structure, node, date);

                ancestors.Select(ancestor => ancestor.RecordId).Should().BeEquivalentTo(
                    [expected],
                    "on {0} the node should sit under exactly one parent", date);
            }
        });

    [Fact]
    public async Task AMoveTouchesOnlyTheMovedSubtree() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-subtree");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // A division with two departments; one of them has two sections.
            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "st-div", Opened);
            var other = await DimensionGraphScenario.RecordAsync(services, types.Division, "st-other", Opened);
            var moved = await DimensionGraphScenario.RecordAsync(services, types.Department, "st-moved", Opened);
            var sibling = await DimensionGraphScenario.RecordAsync(services, types.Department, "st-sibling", Opened);
            var childOne = await DimensionGraphScenario.RecordAsync(services, types.Section, "st-child1", Opened);
            var childTwo = await DimensionGraphScenario.RecordAsync(services, types.Section, "st-child2", Opened);

            foreach (var (node, parent) in new[]
            {
                (moved, division), (sibling, division), (childOne, moved), (childTwo, moved),
            })
            {
                (await graph.MoveAsync(structure, node, parent, Opened)).Succeeded.Should().BeTrue();
            }

            // Architecture section 4: "Only the subtree is touched, not the whole structure."
            // MoveAsync reports how many closure documents it rewrote, so the claim is
            // measurable rather than assumed.
            var result = await graph.MoveAsync(structure, moved, other, Midpoint);

            result.Succeeded.Should().BeTrue();
            result.Value.Should().Be(
                3,
                "the moved department and its two sections, and nothing else: not the two "
                + "divisions, and not the sibling department");
        });

    [Fact]
    public async Task ACycleIsRefusedEvenWhenItWouldOnlyExistOnAPastDate() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-cycle");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var parent = await DimensionGraphScenario.RecordAsync(services, types.Division, "cy-parent", Opened);
            var child = await DimensionGraphScenario.RecordAsync(services, types.Department, "cy-child", Opened);
            var elsewhere = await DimensionGraphScenario.RecordAsync(services, types.Division, "cy-elsewhere", Opened);

            // The child sits under the parent for 2024 only, then moves away.
            (await graph.MoveAsync(structure, child, parent, Opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, child, elsewhere, new DateOnly(2025, 1, 1))).Succeeded.Should().BeTrue();

            // Today the child is not under the parent at all, so a check that only looked at
            // today would allow this. It must not: the move would make the parent its own
            // ancestor for every date in 2024, and ancestor resolution on those dates would
            // never terminate.
            var refused = await graph.MoveAsync(structure, parent, child, new DateOnly(2026, 1, 1));

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.Cycle);
        });

    [Fact]
    public async Task ARecordCannotBecomeItsOwnParent() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-self");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var node = await DimensionGraphScenario.RecordAsync(services, types.Division, "sf-node", Opened);

            var refused = await graph.MoveAsync(structure, node, node, Opened);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.Cycle);
        });

    [Fact]
    public async Task ARetiredRecordStopsResolvingUnderItsParentButHistoryKeeps() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "clo-retire");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "rt-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "rt-dep", Opened);

            (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();

            var closes = new DateOnly(2025, 10, 1);

            (await records.RetireAsync(department, closes)).Succeeded.Should().BeTrue();

            (await graph.IsUnderAsync(structure, department, division, closes.AddDays(-1))).Should().BeTrue(
                "a retired unit still resolves for the period it existed");

            (await graph.IsUnderAsync(structure, department, division, closes)).Should().BeFalse(
                "and stops resolving from the day it closed");
        });
}
