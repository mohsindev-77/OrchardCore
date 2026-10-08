using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The unit head: a dated record of its own, not an assignment and not a field on the unit.
/// </summary>
/// <remarks>
/// ADR-0012. These tests are the proof of the two claims that decision rests on — that a head need
/// not be a member of the unit they head, and that a head is counted by no headcount — and of the
/// one prompt 5 depends on, which is that "who led this unit last March" is answerable.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class UnitHeadTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public UnitHeadTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task AUnitWithNoAppointmentHasNoHead() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-vacant");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-vacant-dep", Opened);

            (await assignments.GetHeadAsync(structure, department, Opened)).Should().BeNull(
                "null is 'Vacant', and the card says so rather than saying nothing");
        });

    /// <summary>
    /// The claim ADR-0012 turns on: a head need not be a member of what they lead.
    /// </summary>
    /// <remarks>
    /// Architecture section 8's seed data requires "a unit head who is not a member of the unit",
    /// and an acting head borrowed from another department is ordinary rather than exotic. Under
    /// the rejected design — a flag on an assignment row — this case could only be expressed by
    /// inventing an allocation for somebody who has none there, which would then be counted by the
    /// unit's headcount and charged to its cost centre. Both halves are asserted here.
    /// </remarks>
    [Fact]
    public async Task AHeadNeedNotBeAMemberOfTheUnitTheyHeadAndIsNotCountedByIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-outsider");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var engineering = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-out-eng", Opened);
            var finance = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-out-fin", Opened);

            // A general manager who sits in Engineering and acts as head of Finance.
            var manager = await EmployeeScenario.ActiveEmployeeAsync(services, "head-out-gm");

            (await assignments.PlaceAsync(manager, structure, engineering, Opened)).Succeeded.Should().BeTrue();

            var appointed = await assignments.SetHeadAsync(structure, finance, manager, Opened);

            appointed.Succeeded.Should().BeTrue(
                string.Join("; ", appointed.Errors.Select(error => error.Message.Value)));

            (await assignments.GetHeadAsync(structure, finance, Opened))!
                .EmployeeId.Should().Be(manager);

            var counts = await assignments.CountEmployeesAtAsync(structure, [finance, engineering], Opened);

            counts.TryGetValue(finance, out _).Should().BeFalse(
                "heading a unit is not being allocated to it: Finance has nobody at it");

            counts[engineering].Should().Be(1, "and the manager is still counted exactly once, where they sit");
        });

    /// <summary>
    /// Who led a unit last March is answerable after the head has changed.
    /// </summary>
    /// <remarks>
    /// The whole reason the appointment is dated. Prompt 5's approval routing reads these same
    /// terms, so an approval re-resolving an old period and the chart showing that period cannot
    /// give different answers.
    /// </remarks>
    [Fact]
    public async Task WhoLedTheUnitLastMarchIsStillAnswerableAfterTheHeadChanges() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-history");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-hist-dep", Opened);

            var first = await EmployeeScenario.ActiveEmployeeAsync(services, "head-hist-first");
            var second = await EmployeeScenario.ActiveEmployeeAsync(services, "head-hist-second");

            var handover = new DateOnly(2026, 4, 1);

            (await assignments.SetHeadAsync(structure, department, first, Opened)).Succeeded.Should().BeTrue();
            (await assignments.SetHeadAsync(structure, department, second, handover)).Succeeded.Should().BeTrue();

            (await assignments.GetHeadAsync(structure, department, new DateOnly(2026, 3, 15)))!
                .EmployeeId.Should().Be(first, "who led this unit last March");

            (await assignments.GetHeadAsync(structure, department, handover.AddDays(-1)))!
                .EmployeeId.Should().Be(first, "the outgoing term runs to the day before, inclusive");

            (await assignments.GetHeadAsync(structure, department, handover))!
                .EmployeeId.Should().Be(second, "and the incoming one from the handover date");

            var history = await assignments.GetHeadHistoryAsync(structure, department);

            history.Should().HaveCount(2, "the export carries every term, not only the one in force");
            history[0].Range.To.Should().Be(handover.AddDays(-1));
            history[1].Range.To.Should().BeNull();
        });

    /// <summary>A unit has one head at a time.</summary>
    [Fact]
    public async Task TwoPeopleCannotHeadOneUnitOnOneDate() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-two");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-two-dep", Opened);

            var one = await EmployeeScenario.ActiveEmployeeAsync(services, "head-two-one");
            var two = await EmployeeScenario.ActiveEmployeeAsync(services, "head-two-two");

            (await assignments.SetHeadAsync(structure, department, one, Opened)).Succeeded.Should().BeTrue();

            // Backdated into the term the first head already holds, which is the case a plain
            // "is there a head today" check would miss.
            var clash = await assignments.SetHeadAsync(
                structure, department, two, Opened.AddDays(-30));

            clash.Succeeded.Should().BeFalse();
            clash.Errors.Should().Contain(error => error.Rule == DimensionRule.SingleHeadPerUnit);
        });

    /// <summary>
    /// Re-appointing the head who is already in post changes nothing and succeeds.
    /// </summary>
    /// <remarks>
    /// What makes the <c>unit-heads</c> recipe step safe to re-run under ADR-0008. Treating it as a
    /// clash would fail the second application of a correct file; writing a second identical term
    /// would leave two abutting rows describing one continuous appointment.
    /// </remarks>
    [Fact]
    public async Task ReappointingTheSittingHeadIsANoOp() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-rerun");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-rerun-dep", Opened);
            var head = await EmployeeScenario.ActiveEmployeeAsync(services, "head-rerun-1");

            (await assignments.SetHeadAsync(structure, department, head, Opened)).Succeeded.Should().BeTrue();

            var again = await assignments.SetHeadAsync(structure, department, head, Opened);

            again.Succeeded.Should().BeTrue(
                string.Join("; ", again.Errors.Select(error => error.Message.Value)));

            (await assignments.GetHeadHistoryAsync(structure, department))
                .Should().ContainSingle("a re-run writes nothing, so there is still one term");
        });

    [Fact]
    public async Task ClearingTheHeadLeavesThePostVacantFromTheDayAfter() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-clear");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-clear-dep", Opened);
            var head = await EmployeeScenario.ActiveEmployeeAsync(services, "head-clear-1");

            (await assignments.SetHeadAsync(structure, department, head, Opened)).Succeeded.Should().BeTrue();

            var lastDay = new DateOnly(2026, 5, 31);
            var cleared = await assignments.ClearHeadAsync(structure, department, lastDay);

            cleared.Succeeded.Should().BeTrue();
            cleared.Value.Should().Be(1);

            (await assignments.GetHeadAsync(structure, department, lastDay)).Should().NotBeNull();
            (await assignments.GetHeadAsync(structure, department, lastDay.AddDays(1))).Should().BeNull();
        });

    /// <summary>
    /// The head must be somebody this tenant employs, and must not have already left.
    /// </summary>
    [Fact]
    public async Task AHeadMustBeARealEmployeeWhoHasNotAlreadyLeft() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-eligible");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-elig-dep", Opened);

            var ghost = await assignments.SetHeadAsync(structure, department, "not-an-employee-id", Opened);

            ghost.Succeeded.Should().BeFalse();
            ghost.Errors.Should().Contain(error => error.Rule == DimensionRule.HeadNotEligible);

            var leaver = await EmployeeScenario.ActiveEmployeeAsync(services, "head-elig-leaver");
            var lastDay = new DateOnly(2025, 12, 31);

            (await employees.ExitAsync(leaver, lastDay)).Succeeded.Should().BeTrue();

            var afterLeaving = await assignments.SetHeadAsync(
                structure, department, leaver, lastDay.AddDays(1));

            afterLeaving.Succeeded.Should().BeFalse();
            afterLeaving.Errors.Should().Contain(error => error.Rule == DimensionRule.HeadNotEligible);

            var whileStillHere = await assignments.SetHeadAsync(
                structure, department, leaver, new DateOnly(2025, 1, 1));

            whileStillHere.Succeeded.Should().BeTrue(
                "recording a past leader's term is exactly the history this engine exists to keep");
        });

    /// <summary>
    /// The batched read answers a whole row of designer cards in one query.
    /// </summary>
    /// <remarks>
    /// A unit with no head is absent from the result rather than present with null, which is what
    /// lets the common case — a tenant that has not appointed anybody yet — cost nothing to carry.
    /// </remarks>
    [Fact]
    public async Task HeadsAreReadInOneQueryForARowOfCards() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "head-batch");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var led = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-batch-led", Opened);
            var vacant = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-batch-vacant", Opened);

            var head = await EmployeeScenario.ActiveEmployeeAsync(services, "head-batch-1");

            (await assignments.SetHeadAsync(structure, led, head, Opened)).Succeeded.Should().BeTrue();

            var heads = await assignments.GetHeadsAsync(structure, [led, vacant], Opened);

            heads.Should().ContainKey(led);
            heads[led].EmployeeId.Should().Be(head);
            heads.Should().NotContainKey(vacant, "a vacant post is absent rather than present and null");
        });

    /// <summary>Every unit one person heads, across every axis — what the exit dry run reads.</summary>
    [Fact]
    public async Task EveryUnitAPersonHeadsIsFoundAcrossEveryAxis() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var organisation = await DimensionGraphScenario.StructureAsync(services, "head-axes-org");
            var cost = await DimensionGraphScenario.StructureAsync(services, "head-axes-cost");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-axes-dep", Opened);
            var centre = await DimensionGraphScenario.RecordAsync(services, types.Department, "head-axes-cc", Opened);

            var head = await EmployeeScenario.ActiveEmployeeAsync(services, "head-axes-1");

            (await assignments.SetHeadAsync(organisation, department, head, Opened)).Succeeded.Should().BeTrue();
            (await assignments.SetHeadAsync(cost, centre, head, Opened)).Succeeded.Should().BeTrue();

            var headships = await assignments.GetHeadshipsOfAsync(head, Opened);

            headships.Select(headship => headship.StructureId)
                .Should().BeEquivalentTo([organisation, cost]);
        });
}
