using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using Xunit;
using Xunit.Abstractions;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Placing people on an axis, and the hot query that reads them back.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeAssignmentTenantTests
{
    private readonly BaseTenantFixture _tenant;
    private readonly ITestOutputHelper _output;

    public EmployeeAssignmentTenantTests(BaseTenantFixture tenant, ITestOutputHelper output)
    {
        _tenant = tenant;
        _output = output;
    }

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task AMidMonthTransferResolvesToEachDepartmentOnItsOwnDays() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The worked example from architecture section 5, end to end.
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "emp-transfer");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var departmentA = await DimensionGraphScenario.RecordAsync(services, types.Department, "tr-a", Opened);
            var departmentB = await DimensionGraphScenario.RecordAsync(services, types.Department, "tr-b", Opened);

            const string employee = "employee-transfer";

            (await assignments.PlaceAsync(employee, structure, departmentA, Opened)).Succeeded.Should().BeTrue();

            var transfer = new DateOnly(2026, 3, 16);

            (await assignments.PlaceAsync(employee, structure, departmentB, transfer)).Succeeded.Should().BeTrue();

            (await assignments.GetEffectiveAsync(employee, structure, new DateOnly(2026, 3, 10)))!
                .RecordId.Should().Be(departmentA, "where did they work on 10 March");

            (await assignments.GetEffectiveAsync(employee, structure, new DateOnly(2026, 3, 15)))!
                .RecordId.Should().Be(departmentA, "the old row runs to the 15th, inclusive");

            (await assignments.GetEffectiveAsync(employee, structure, transfer))!
                .RecordId.Should().Be(departmentB, "and the new one from the 16th");
        });

    [Fact]
    public async Task ASplitAllocationMustTotalOneHundredAndNameOnePrimary() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "emp-split");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var one = await DimensionGraphScenario.RecordAsync(services, types.Department, "sp-one", Opened);
            var two = await DimensionGraphScenario.RecordAsync(services, types.Department, "sp-two", Opened);

            const string employee = "employee-split";

            var short60 = await assignments.ReallocateAsync(
                employee, structure, [(one, 60m, true), (two, 30m, false)], Opened);

            short60.Succeeded.Should().BeFalse();
            short60.Errors.Should().Contain(error => error.Rule == DimensionRule.AllocationTotal);

            var twoPrimaries = await assignments.ReallocateAsync(
                employee, structure, [(one, 60m, true), (two, 40m, true)], Opened);

            twoPrimaries.Succeeded.Should().BeFalse();
            twoPrimaries.Errors.Should().Contain(error => error.Rule == DimensionRule.SinglePrimaryAssignment);

            var valid = await assignments.ReallocateAsync(
                employee, structure, [(one, 60m, true), (two, 40m, false)], Opened);

            valid.Succeeded.Should().BeTrue(
                string.Join("; ", valid.Errors.Select(error => error.Message.Value)));

            (await assignments.GetAllEffectiveAsync(employee, structure, Opened)).Should().HaveCount(2);

            (await assignments.GetEffectiveAsync(employee, structure, Opened))!
                .RecordId.Should().Be(one, "the primary placement is the one that answers 'where do they work'");
        });

    [Fact]
    public async Task EndingAPlacementClosesItOnTheDayGiven() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "emp-end");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "en-dep", Opened);

            const string employee = "employee-leaver";
            var lastDay = new DateOnly(2026, 6, 30);

            (await assignments.PlaceAsync(employee, structure, department, Opened)).Succeeded.Should().BeTrue();
            (await assignments.EndAsync(employee, structure, lastDay)).Succeeded.Should().BeTrue();

            (await assignments.GetEffectiveAsync(employee, structure, lastDay)).Should().NotBeNull(
                "the last day is included");

            (await assignments.GetEffectiveAsync(employee, structure, lastDay.AddDays(1))).Should().BeNull();
        });

    [Fact]
    public async Task EmployeesUnderANodeIncludeTheWholeSubtreeAsAtTheDate() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "emp-subtree");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "su-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "su-dep", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "su-sec", Opened);

            (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structure, section, department, Opened)).Succeeded.Should().BeTrue();

            (await assignments.PlaceAsync("su-at-section", structure, section, Opened)).Succeeded.Should().BeTrue();
            (await assignments.PlaceAsync("su-at-department", structure, department, Opened)).Succeeded.Should().BeTrue();

            var wholeSubtree = await assignments.GetEmployeesUnderAsync(structure, division, Opened);

            wholeSubtree.Items.Select(row => row.EmployeeId).Should().BeEquivalentTo(
                ["su-at-section", "su-at-department"],
                "the division has nobody placed at it directly, but two people below it");

            var directOnly = await assignments.GetEmployeesUnderAsync(
                structure, division, Opened, includeDescendants: false);

            directOnly.Items.Should().BeEmpty("nobody is placed at the division itself");
        });

    /// <summary>
    /// The reason the subtree query uses a correlated sub-select rather than a list of ids.
    /// </summary>
    /// <remarks>
    /// SQL Server allows 2,100 parameters per command. Resolving the descendants first and
    /// passing them as an IN list works on a small tenant and on the SQLite the suite runs
    /// against, then throws on a customer's database at a scale the acceptance criterion
    /// explicitly names — 5,000 records and 5,000 employees. This test puts more than 2,100
    /// employees behind the query so that reverting to the obvious implementation cannot pass
    /// review by passing the tests.
    ///
    /// It asserts behaviour, not speed. The performance gate is a separate exercise against the
    /// seed generator, which does not exist yet.
    /// </remarks>
    [Fact]
    public async Task TheSubtreeQueryHoldsBeyondTheSqlServerParameterLimit() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            const int employeeCount = 2_500;

            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "emp-scale");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "sc-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "sc-dep", Opened);

            (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();

            var stopwatch = Stopwatch.StartNew();

            for (var index = 0; index < employeeCount; index++)
            {
                var placed = await assignments.PlaceAsync(
                    $"scale-employee-{index:D5}", structure, department, Opened);

                placed.Succeeded.Should().BeTrue();
            }

            _output.WriteLine($"Placed {employeeCount} employees in {stopwatch.ElapsedMilliseconds} ms.");

            var page = await assignments.GetEmployeesUnderAsync(structure, division, Opened, skip: 0, take: 50);

            page.Total.Should().Be(
                employeeCount,
                "the count comes from one statement with a sub-select, so there is no parameter "
                + "list to overflow");

            page.Items.Should().HaveCount(50, "the query is paged and never unbounded");
            page.HasMore.Should().BeTrue();

            // Paging has to work at the far end too, not only on the first page.
            var lastPage = await assignments.GetEmployeesUnderAsync(
                structure, division, Opened, skip: employeeCount - 10, take: 50);

            lastPage.Items.Should().HaveCount(10);
            lastPage.HasMore.Should().BeFalse();

            lastPage.Items.Select(row => row.EmployeeId).Should().BeInAscendingOrder(
                "a page is only meaningful if the order is stable between calls");
        });

    [Fact]
    public async Task AnEmployeeIsFoundOnEveryAxisTheyTouch() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var organisation = await DimensionGraphScenario.StructureAsync(services, "emp-axes-org");
            var cost = await DimensionGraphScenario.StructureAsync(services, "emp-axes-cost");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "ax-dep", Opened);
            var centre = await DimensionGraphScenario.RecordAsync(services, types.Department, "ax-centre", Opened);

            const string employee = "employee-matrix";

            (await assignments.PlaceAsync(employee, organisation, department, Opened)).Succeeded.Should().BeTrue();
            (await assignments.PlaceAsync(employee, cost, centre, Opened)).Succeeded.Should().BeTrue();

            var everywhere = await assignments.GetAllAxesAsync(employee, Opened);

            everywhere.Select(row => row.StructureId).Should().BeEquivalentTo(
                [organisation, cost],
                "one record sits on several axes at once, and so does one employee");
        });
}
