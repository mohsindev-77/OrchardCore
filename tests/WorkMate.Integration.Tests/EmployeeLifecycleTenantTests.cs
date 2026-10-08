using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The five lifecycle states, each transition dated, and what an exit closes.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeLifecycleTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public EmployeeLifecycleTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task EachTransitionRecordsTheDateItWasGivenRatherThanToday() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.CreateAsync(services, "life-dates-1");

            var activeFrom = new DateOnly(2024, 2, 1);
            var leaveFrom = new DateOnly(2025, 5, 12);

            (await employees.ActivateAsync(id, activeFrom)).Value!
                .StatusEffectiveFrom.Should().Be(activeFrom);

            var onLeave = await employees.PutOnLeaveAsync(id, leaveFrom);

            onLeave.Succeeded.Should().BeTrue(onLeave.Describe());
            onLeave.Value!.Status.Should().Be(EmploymentStatus.OnLeave);
            onLeave.Value.StatusEffectiveFrom.Should().Be(leaveFrom);
        });

    [Fact]
    public async Task AnIllegalTransitionIsRefusedNamingBothStates() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "life-illegal-1");

            (await employees.SuspendAsync(id, new DateOnly(2025, 1, 1))).Succeeded.Should().BeTrue();

            var straightToLeave = await employees.PutOnLeaveAsync(id, new DateOnly(2025, 2, 1));

            straightToLeave.Succeeded.Should().BeFalse(
                "a suspension is lifted before anything else happens, or its ending leaves no record");

            straightToLeave.Errors.Should().Contain(error =>
                error.Rule == RecordRule.TransitionNotPermitted);
        });

    /// <summary>
    /// A transition cannot be dated before the state it would replace began.
    /// </summary>
    /// <remarks>
    /// The record would otherwise claim two things at once about the same day. This engine keeps
    /// history by adding periods, never by overwriting one, so the only honest answer is to refuse
    /// and say which date is in the way.
    /// </remarks>
    [Fact]
    public async Task ATransitionCannotBeBackdatedBeforeTheCurrentState() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.CreateAsync(services, "life-backdated-1");

            (await employees.ActivateAsync(id, new DateOnly(2025, 6, 1))).Succeeded.Should().BeTrue();

            var backwards = await employees.SuspendAsync(id, new DateOnly(2025, 3, 1));

            backwards.Succeeded.Should().BeFalse();

            backwards.Errors.Should().Contain(error =>
                error.Rule == RecordRule.TransitionOutOfOrder &&
                error.Field == "EffectiveFrom");
        });

    /// <summary>
    /// An exit closes every placement, on every axis, on the last day — inclusive.
    /// </summary>
    /// <remarks>
    /// Every axis, not only the primary one: somebody placed on an organisation axis and a cost
    /// axis who was closed on one of them would keep being charged somewhere after they left, and
    /// nothing would report it.
    /// </remarks>
    [Fact]
    public async Task ExitingClosesEveryPlacementOnEveryAxisOnTheLastDay() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var organisation = await DimensionGraphScenario.StructureAsync(services, "life-exit-org");
            var cost = await DimensionGraphScenario.StructureAsync(services, "life-exit-cost");

            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "life-exit-dep", Opened);
            var centre = await DimensionGraphScenario.RecordAsync(services, types.Department, "life-exit-cc", Opened);

            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "life-exit-1");

            (await assignments.PlaceAsync(id, organisation, department, Opened)).Succeeded.Should().BeTrue();
            (await assignments.PlaceAsync(id, cost, centre, Opened)).Succeeded.Should().BeTrue();

            var lastDay = new DateOnly(2026, 6, 30);
            var exit = await employees.ExitAsync(id, lastDay);

            exit.Succeeded.Should().BeTrue(exit.Describe());
            exit.Value!.AssignmentsClosed.Should().Be(2, "one placement on each of the two axes");

            (await assignments.GetEffectiveAsync(id, organisation, lastDay)).Should().NotBeNull(
                "the last day of service is a day they worked");

            (await assignments.GetEffectiveAsync(id, organisation, lastDay.AddDays(1))).Should().BeNull();
            (await assignments.GetEffectiveAsync(id, cost, lastDay.AddDays(1))).Should().BeNull();
        });

    /// <summary>
    /// An exit closes the units the leaver headed, too.
    /// </summary>
    /// <remarks>
    /// The half that is easiest to forget and worst to get wrong. A leaver who stays recorded as a
    /// head is somebody prompt 5's routing still routes approvals to, and nothing about that failure
    /// is visible: the chart and the routing agree with each other, and both are wrong.
    /// </remarks>
    [Fact]
    public async Task ExitingEndsTheUnitsTheLeaverHeaded() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "life-head-exit");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "life-head-dep", Opened);
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "life-head-1");

            (await assignments.SetHeadAsync(structure, department, id, Opened)).Succeeded.Should().BeTrue();

            var lastDay = new DateOnly(2026, 9, 30);
            var exit = await employees.ExitAsync(id, lastDay);

            exit.Succeeded.Should().BeTrue(exit.Describe());
            exit.Value!.HeadshipsClosed.Should().Be(1);

            exit.Value.UnitsLeftWithoutAHead.Should().ContainSingle()
                .Which.RecordCode.Should().Be("life-head-dep",
                    "the list names the unit, because an id tells the person reading it nothing");

            (await assignments.GetHeadAsync(structure, department, lastDay))
                .Should().NotBeNull("they led it on their last day");

            (await assignments.GetHeadAsync(structure, department, lastDay.AddDays(1)))
                .Should().BeNull("and the post is vacant the day after");
        });

    /// <summary>
    /// The dry run says what the exit would close, and closes nothing.
    /// </summary>
    [Fact]
    public async Task ThePlannedExitReportsWhatItWouldCloseWithoutClosingIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "life-plan");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "life-plan-dep", Opened);
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "life-plan-1");

            (await assignments.PlaceAsync(id, structure, department, Opened)).Succeeded.Should().BeTrue();
            (await assignments.SetHeadAsync(structure, department, id, Opened)).Succeeded.Should().BeTrue();

            var lastDay = new DateOnly(2026, 8, 31);
            var plan = await employees.PlanExitAsync(id, lastDay);

            plan.Succeeded.Should().BeTrue(plan.Describe());
            plan.Value!.Assignments.Should().ContainSingle();
            plan.Value.LeavesAUnitVacant.Should().BeTrue();
            plan.Value.UnitsLosingTheirHead.Should().ContainSingle()
                .Which.NameEn.Should().Be("life-plan-dep");

            (await assignments.GetEffectiveAsync(id, structure, lastDay.AddDays(1)))
                .Should().NotBeNull("a dry run changes nothing");

            (await employees.GetAsync(id))!.Status.Should().Be(EmploymentStatus.Active);
        });

    /// <summary>
    /// Reinstating a leaver does not put them back where they used to sit.
    /// </summary>
    /// <remarks>
    /// Where somebody sat before they left is not where they sit now, and a rehire that silently
    /// restored last year's placements could place them in a unit that has since been retired.
    /// Placing them again is a separate, dated decision.
    /// </remarks>
    [Fact]
    public async Task ReinstatingALeaverDoesNotReopenTheirOldPlacements() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "life-rehire");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "life-rehire-dep", Opened);
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "life-rehire-1");

            (await assignments.PlaceAsync(id, structure, department, Opened)).Succeeded.Should().BeTrue();
            (await employees.ExitAsync(id, new DateOnly(2026, 3, 31))).Succeeded.Should().BeTrue();

            var backFrom = new DateOnly(2026, 9, 1);
            var reinstated = await employees.ReinstateAsync(id, backFrom);

            reinstated.Succeeded.Should().BeTrue(reinstated.Describe());
            reinstated.Value!.Status.Should().Be(EmploymentStatus.Active);

            (await assignments.GetEffectiveAsync(id, structure, backFrom)).Should().BeNull(
                "a rehire is a new placement decision, not a restoration of the old one");
        });

    /// <summary>
    /// A leaver is still employed on their last day, and an ex-employee the day after.
    /// </summary>
    [Fact]
    public async Task TheLastDayOfServiceIsADayTheyWereStillEmployed() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "life-lastday-1");

            var lastDay = new DateOnly(2026, 6, 30);

            (await employees.ExitAsync(id, lastDay)).Succeeded.Should().BeTrue();

            var employee = (await employees.GetAsync(id))!;

            employee.ExitedOn.Should().Be(lastDay);
            employee.StatusEffectiveFrom.Should().Be(lastDay.AddDays(1));
            employee.HasLeftBy(lastDay).Should().BeFalse();
            employee.HasLeftBy(lastDay.AddDays(1)).Should().BeTrue();
        });

    /// <summary>
    /// Every transition reaches a subscriber, with the dates and counts a subscriber needs.
    /// </summary>
    /// <remarks>
    /// Specification section 5: each transition raises "a domain event that leave, attendance and
    /// payroll subscribe to", and exit is what payroll computes a final settlement from. None of
    /// those modules exists yet, so <c>RecordingLifecycleHandler</c> stands in for them — otherwise
    /// the events could be raised into an empty collection for three more prompts and nobody would
    /// know.
    /// </remarks>
    [Fact]
    public async Task EveryLifecycleEventReachesASubscriber()
    {
        const string code = "life-events-1";

        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.CreateAsync(services, code);

            (await employees.ActivateAsync(id, new DateOnly(2024, 2, 1))).Succeeded.Should().BeTrue();
            (await employees.SuspendAsync(id, new DateOnly(2025, 1, 15))).Succeeded.Should().BeTrue();
            (await employees.ActivateAsync(id, new DateOnly(2025, 2, 1))).Succeeded.Should().BeTrue();
            (await employees.ExitAsync(id, new DateOnly(2026, 4, 30))).Succeeded.Should().BeTrue();
        });

        var recorded = _tenant.Services.GetRequiredService<RecordedLifecycleEvents>();

        recorded.WasCreated(code).Should().BeTrue();

        var changes = recorded.ChangesFor(code);

        changes.Select(change => change.To).Should().Equal(
            [EmploymentStatus.Active, EmploymentStatus.Suspended, EmploymentStatus.Active, EmploymentStatus.Exited],
            "a subscriber has to see every move, in order");

        var exit = changes[^1];

        exit.From.Should().Be(EmploymentStatus.Active);
        exit.LastDayOfService.Should().Be(new DateOnly(2026, 4, 30));

        exit.EffectiveFrom.Should().Be(
            new DateOnly(2026, 5, 1),
            "the status takes effect the day after the last day of service, and payroll needs both numbers");
    }
}
