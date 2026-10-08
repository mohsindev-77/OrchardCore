using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Where employees may attach on an axis: the declared rule, and the advisory one.
/// </summary>
/// <remarks>
/// The backlog note's requirement is that employees attach to leaf-capable units and never to a
/// Division, a Region or a Project, "because an employee attached to one would be counted by every
/// roll-up beneath it". These tests pin both halves of the answer and, as much as anything, pin why
/// it could not simply be derived: a Branch with departments and a Branch without are the same type
/// on the same axis, and only one of them holds staff.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeAttachmentRulesTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public EmployeeAttachmentRulesTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// A structure that declares nothing constrains nothing.
    /// </summary>
    /// <remarks>
    /// Every structure written before this rule existed deserialises to an empty list, and nothing
    /// about them changes — the same rule ADR-0008 sets for a recipe row that states nothing. If
    /// empty meant "nothing may hold employees" instead, every existing tenant's next placement
    /// would be refused by an upgrade.
    /// </remarks>
    [Fact]
    public async Task AStructureThatDeclaresNothingPlacesEmployeesAnywhere() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "attach-silent");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "attach-silent-div", Opened);
            var employee = await EmployeeScenario.ActiveEmployeeAsync(services, "attach-silent-1");

            var placed = await assignments.PlaceAsync(employee, structure, division, Opened);

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));
        });

    /// <summary>
    /// An axis that names the types that hold employees refuses the ones it did not name.
    /// </summary>
    /// <remarks>
    /// This is the Zenith and Crescent rule: a Department or a Team holds people, a Division or a
    /// Project does not. The error names the unit, its type and the axis, because "that placement
    /// is not allowed" leaves somebody guessing which of the three is at fault.
    /// </remarks>
    [Fact]
    public async Task AnAxisRefusesAPlacementAtATypeItDidNotDeclare() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var created = await structures.CreateAsync(
                "attach-declared",
                new BilingualText("Attach declared", "محدد"),
                [types.Division, types.Department],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [types.Division],
                    [new StructureContainment(types.Division, types.Department)],
                    // Departments hold people; Divisions are containers.
                    [types.Department]));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            var structure = created.Value!.StructureId;

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "attach-dec-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "attach-dec-dep", Opened);

            (await services.GetRequiredService<IDimensionGraphService>()
                .MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();

            var employee = await EmployeeScenario.ActiveEmployeeAsync(services, "attach-dec-1");

            var atTheDivision = await assignments.PlaceAsync(employee, structure, division, Opened);

            atTheDivision.Succeeded.Should().BeFalse(
                "an employee at a container is counted by it and again by every roll-up beneath it");

            atTheDivision.Errors.Should().Contain(error =>
                error.Rule == DimensionRule.UnitDoesNotHoldEmployees);

            var atTheDepartment = await assignments.PlaceAsync(employee, structure, department, Opened);

            atTheDepartment.Succeeded.Should().BeTrue(
                string.Join("; ", atTheDepartment.Errors.Select(error => error.Message.Value)));
        });

    /// <summary>
    /// The rule is about the type on this axis, not about the type everywhere.
    /// </summary>
    /// <remarks>
    /// The reason it is declared per structure rather than per dimension type, and the same argument
    /// ADR-0010 makes for containment: a record participates in several axes by design, so "may a
    /// Division hold employees" is an axis-specific fact. A cost axis that books staff costs
    /// directly to a division is perfectly ordinary alongside an organisation axis that does not.
    /// </remarks>
    [Fact]
    public async Task OneTypeMayHoldEmployeesOnOneAxisAndNotOnAnother() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var shape = new StructureShape(
                [types.Division],
                [new StructureContainment(types.Division, types.Department)],
                [types.Department]);

            var organisation = await structures.CreateAsync(
                "attach-axis-org", new BilingualText("Org", "تنظيم"),
                [types.Division, types.Department], false, true, false, shape);

            var cost = await structures.CreateAsync(
                "attach-axis-cost", new BilingualText("Cost", "تكلفة"),
                [types.Division, types.Department], false, true, false,
                shape with { EmployeeAttachableDimensionTypeIds = [types.Division, types.Department] });

            organisation.Succeeded.Should().BeTrue();
            cost.Succeeded.Should().BeTrue();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "attach-axis-div", Opened);
            var employee = await EmployeeScenario.ActiveEmployeeAsync(services, "attach-axis-1");

            (await assignments.PlaceAsync(employee, organisation.Value!.StructureId, division, Opened))
                .Succeeded.Should().BeFalse("the organisation axis does not place people on a division");

            (await assignments.PlaceAsync(employee, cost.Value!.StructureId, division, Opened))
                .Succeeded.Should().BeTrue("the cost axis does");
        });

    /// <summary>
    /// A unit with children below it warns, and does not refuse.
    /// </summary>
    /// <remarks>
    /// A department with three sections and a departmental secretary is an ordinary shape, so
    /// blocking would be wrong far more often than it would be right. Saying nothing would hide the
    /// thing the whole rule is about: that person is counted by the department and again by every
    /// roll-up beneath it, and both numbers stay plausible.
    /// </remarks>
    [Fact]
    public async Task PlacingSomebodyAtAUnitWithChildrenWarnsWithoutRefusing() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "attach-container");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "attach-con-dep", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "attach-con-sec", Opened);

            (await graph.MoveAsync(structure, section, department, Opened)).Succeeded.Should().BeTrue();

            var secretary = await EmployeeScenario.ActiveEmployeeAsync(services, "attach-con-1");

            var placed = await assignments.PlaceAsync(secretary, structure, department, Opened);

            placed.Succeeded.Should().BeTrue("the departmental secretary is a real shape, not an error");

            placed.Errors.Should().Contain(error =>
                error.Rule == DimensionRule.EmployeeAtContainerUnit && error.IsAdvisory,
                "but the double-counting risk is worth saying out loud");
        });

    /// <summary>
    /// The rule is asked of every unit in a split, not only the primary one.
    /// </summary>
    /// <remarks>
    /// A secondary placement is counted by exactly the same roll-ups the primary is — the Zenith
    /// matrix is the worked example, and its whole point is that cost rolls up by project over the
    /// secondary rows. A rule that only checked the primary would let the matrix put labour on a
    /// Project directly.
    /// </remarks>
    [Fact]
    public async Task TheRuleIsAskedOfTheSecondaryHalfOfASplitToo() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var created = await structures.CreateAsync(
                "attach-split",
                new BilingualText("Attach split", "تقسيم"),
                [types.Division, types.Department],
                allowSkipLevel: false,
                isStrict: false,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [types.Division],
                    [new StructureContainment(types.Division, types.Department)],
                    [types.Department]));

            created.Succeeded.Should().BeTrue();

            var structure = created.Value!.StructureId;

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "attach-split-dep", Opened);
            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "attach-split-div", Opened);

            var employee = await EmployeeScenario.ActiveEmployeeAsync(services, "attach-split-1");

            var split = await assignments.ReallocateAsync(
                employee,
                structure,
                [
                    new AssignmentSplitEntry(department, 60m, true),
                    new AssignmentSplitEntry(division, 40m, false),
                ],
                Opened);

            split.Succeeded.Should().BeFalse("the secondary half lands on a container");

            split.Errors.Should().Contain(error =>
                error.Rule == DimensionRule.UnitDoesNotHoldEmployees);
        });

    /// <summary>
    /// Updating a structure that says nothing about employee attachment leaves the rule alone.
    /// </summary>
    /// <remarks>
    /// Treating silence as "clear it" would quietly drop a tenant's rule every time somebody
    /// renamed their structure on a screen that predates the setting — which is exactly the kind of
    /// loss nothing reports.
    /// </remarks>
    [Fact]
    public async Task AnUpdateThatSaysNothingAboutAttachmentDoesNotClearIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();

            var created = await structures.CreateAsync(
                "attach-preserve",
                new BilingualText("Attach preserve", "حفظ"),
                [types.Division, types.Department],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [types.Division],
                    [new StructureContainment(types.Division, types.Department)],
                    [types.Department]));

            created.Succeeded.Should().BeTrue();

            var updated = await structures.UpdateAsync(
                created.Value!.StructureId,
                new BilingualText("Attach preserve renamed", "حفظ"),
                [types.Division, types.Department],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                // The shape, stated without the attachment list — which is what a screen or a recipe
                // written before this rule posts.
                new StructureShape(
                    [types.Division],
                    [new StructureContainment(types.Division, types.Department)]));

            updated.Succeeded.Should().BeTrue(
                string.Join("; ", updated.Errors.Select(error => error.Message.Value)));

            updated.Value!.EmployeeAttachableDimensionTypeIds.Should().BeEquivalentTo([types.Department]);
        });
}
