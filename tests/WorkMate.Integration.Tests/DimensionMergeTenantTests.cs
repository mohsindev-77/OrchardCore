using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Merging one unit into another, and the dry run that has to describe it exactly.
/// </summary>
/// <remarks>
/// Architecture section 6: a merge "runs in dry-run first and produces a report of exactly what
/// will change". Exactly is the word doing the work. The report is what a human signs off, so a
/// report that only approximately describes the change is worse than no report at all — it
/// converts a careful review into a rubber stamp without anyone noticing.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionMergeTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionMergeTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly MergeDay = new(2026, 4, 1);

    private sealed record Scenario(
        string Structure,
        string Source,
        string Target,
        string ChildOne,
        string ChildTwo);

    /// <summary>A department with two sections and two people, and another department to fold it into.</summary>
    private static async Task<Scenario> BuildAsync(IServiceProvider services, string code)
    {
        var types = await DimensionGraphScenario.TypesAsync(services);
        var structure = await DimensionGraphScenario.StructureAsync(services, code);
        var graph = services.GetRequiredService<IDimensionGraphService>();
        var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

        var division = await DimensionGraphScenario.RecordAsync(services, types.Division, $"{code}-div", Opened);
        var source = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{code}-src", Opened);
        var target = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{code}-tgt", Opened);
        var childOne = await DimensionGraphScenario.RecordAsync(services, types.Section, $"{code}-c1", Opened);
        var childTwo = await DimensionGraphScenario.RecordAsync(services, types.Section, $"{code}-c2", Opened);

        foreach (var (node, parent) in new[]
        {
            (source, division), (target, division), (childOne, source), (childTwo, source),
        })
        {
            (await graph.MoveAsync(structure, node, parent, Opened)).Succeeded.Should().BeTrue();
        }

        foreach (var employee in new[] { $"{code}-emp-a", $"{code}-emp-b" })
        {
            (await assignments.PlaceAsync(employee, structure, source, Opened)).Succeeded.Should().BeTrue();
        }

        return new Scenario(structure, source, target, childOne, childTwo);
    }

    [Fact]
    public async Task TheDryRunListsEveryChildAndEveryEmployeeAndChangesNothing() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "mrg-dryrun");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var plan = await records.PlanMergeAsync(
                scenario.Structure, scenario.Source, scenario.Target, MergeDay);

            plan.Succeeded.Should().BeTrue(
                string.Join("; ", plan.Errors.Select(error => error.Message.Value)));

            plan.Value!.ChildrenReparented.Should().BeEquivalentTo([scenario.ChildOne, scenario.ChildTwo]);
            plan.Value.EmployeesReassigned.Should().BeEquivalentTo(["mrg-dryrun-emp-a", "mrg-dryrun-emp-b"]);
            plan.Value.SourceRetired.Should().BeTrue();

            // Nothing moved. A dry run that changed anything would not be one.
            (await graph.IsUnderAsync(scenario.Structure, scenario.ChildOne, scenario.Source, MergeDay))
                .Should().BeTrue("the dry run must leave the structure exactly as it found it");

            (await records.GetAsync(scenario.Source, MergeDay)).Should().NotBeNull(
                "the source is still open after a dry run");
        });

    [Fact]
    public async Task TheAppliedResultMatchesTheDryRunExactly() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "mrg-matches");
            var records = services.GetRequiredService<IDimensionService>();

            var planned = await records.PlanMergeAsync(
                scenario.Structure, scenario.Source, scenario.Target, MergeDay);

            planned.Succeeded.Should().BeTrue();

            var applied = await records.MergeAsync(
                scenario.Structure, scenario.Source, scenario.Target, MergeDay);

            applied.Succeeded.Should().BeTrue(
                string.Join("; ", applied.Errors.Select(error => error.Message.Value)));

            applied.Value.Should().BeEquivalentTo(
                planned.Value,
                "the report a human signed off has to be what actually happened, field for field");
        });

    [Fact]
    public async Task AfterTheMergeTheChildrenAndEmployeesSitUnderTheTarget() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "mrg-applied");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            (await records.MergeAsync(scenario.Structure, scenario.Source, scenario.Target, MergeDay))
                .Succeeded.Should().BeTrue();

            foreach (var child in new[] { scenario.ChildOne, scenario.ChildTwo })
            {
                (await graph.IsUnderAsync(scenario.Structure, child, scenario.Target, MergeDay))
                    .Should().BeTrue("the children were reparented onto the target");
            }

            var underTarget = await assignments.GetEmployeesUnderAsync(
                scenario.Structure, scenario.Target, MergeDay);

            underTarget.Items.Select(row => row.EmployeeId).Should().Contain(
                ["mrg-applied-emp-a", "mrg-applied-emp-b"]);
        });

    [Fact]
    public async Task PriorPeriodReportingStillResolvesToTheSource() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "mrg-history");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            (await records.MergeAsync(scenario.Structure, scenario.Source, scenario.Target, MergeDay))
                .Succeeded.Should().BeTrue();

            var dayBefore = MergeDay.AddDays(-1);

            (await graph.IsUnderAsync(scenario.Structure, scenario.ChildOne, scenario.Source, dayBefore))
                .Should().BeTrue("architecture section 6: prior-period reporting resolves to the source");

            (await assignments.GetEffectiveAsync("mrg-history-emp-a", scenario.Structure, dayBefore))!
                .RecordId.Should().Be(scenario.Source);

            (await records.GetAsync(scenario.Source, dayBefore)).Should().NotBeNull(
                "the source is retired, not deleted");

            (await records.GetAsync(scenario.Source, MergeDay)).Should().BeNull(
                "and it is closed from the merge date");
        });

    [Fact]
    public async Task TheClosureStillVerifiesAfterAMerge() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "mrg-verified");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await records.MergeAsync(scenario.Structure, scenario.Source, scenario.Target, MergeDay))
                .Succeeded.Should().BeTrue();

            var report = await graph.VerifyAsync(scenario.Structure);

            report.IsConsistent.Should().BeTrue(
                await DimensionGraphScenario.DescribeAsync(services, report));
        });

    [Fact]
    public async Task ARecordCannotBeMergedIntoItself() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var scenario = await BuildAsync(services, "mrg-self");

            var refused = await services.GetRequiredService<IDimensionService>().MergeAsync(
                scenario.Structure, scenario.Source, scenario.Source, MergeDay);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.MergeTarget);
        });

    [Fact]
    public async Task ARecordCannotBeMergedIntoItsOwnDescendant() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // Folding a unit into something inside it would leave the target with no parent
            // chain the moment the source retires.
            var scenario = await BuildAsync(services, "mrg-descendant");

            var refused = await services.GetRequiredService<IDimensionService>().MergeAsync(
                scenario.Structure, scenario.Source, scenario.ChildOne, MergeDay);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.MergeTarget);
        });

    [Fact]
    public async Task MergingAnEmptyUnitIsAllowedAndStillRetiresIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "mrg-empty");
            var records = services.GetRequiredService<IDimensionService>();

            var source = await DimensionGraphScenario.RecordAsync(services, types.Department, "me-src", Opened);
            var target = await DimensionGraphScenario.RecordAsync(services, types.Department, "me-tgt", Opened);

            var plan = await records.PlanMergeAsync(structure, source, target, MergeDay);

            plan.Succeeded.Should().BeTrue();
            plan.Value!.IsEmpty.Should().BeTrue("there is nothing to move");

            var applied = await records.MergeAsync(structure, source, target, MergeDay);

            applied.Succeeded.Should().BeTrue();
            applied.Value.Should().BeEquivalentTo(plan.Value);

            (await records.GetAsync(source, MergeDay)).Should().BeNull(
                "an empty unit is still retired by the merge");
        });
}
