using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The four rules ADR-0012 rests on, asserted rather than implied.
/// </summary>
/// <remarks>
/// Each of these follows from "a head appointment is its own dated record, not an assignment", and
/// none of them was pinned by a test until now. They are the claims the decision was argued from,
/// so they are the ones most worth holding: if any stops being true, the argument for the design
/// has gone and nothing else would have said so.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class UnitHeadRulesTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public UnitHeadRulesTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// One person may head several units at once. Two people heading one unit is what is refused.
    /// </summary>
    /// <remarks>
    /// The rule is "one head per unit per date", not "one unit per head" — a general manager over
    /// Civil, Electrical and Mechanical is the ordinary shape of an engineering division, and a
    /// rule that refused it would be refusing the case the demo organisation exists to show.
    /// </remarks>
    [Fact]
    public async Task OnePersonMayHeadSeveralUnitsAtOnce() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "rule-many");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var civil = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-many-civil", Opened);
            var electrical = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-many-elec", Opened);
            var mechanical = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-many-mech", Opened);

            var manager = await EmployeeScenario.ActiveEmployeeAsync(services, "rule-many-gm", nameEn: "Imran Qureshi");

            foreach (var unit in new[] { civil, electrical, mechanical })
            {
                var appointed = await assignments.SetHeadAsync(structure, unit, manager, Opened);

                appointed.Succeeded.Should().BeTrue(
                    string.Join("; ", appointed.Errors.Select(error => error.Message.Value)));
            }

            (await assignments.GetHeadshipsOfAsync(manager, Opened)).Should().HaveCount(3);

            foreach (var unit in new[] { civil, electrical, mechanical })
            {
                (await assignments.GetHeadAsync(structure, unit, Opened))!.EmployeeId.Should().Be(manager);
            }

            // The rule that is enforced, stated beside the one that is not: a second person on one
            // of those units, on a date the first already holds, is refused.
            var second = await EmployeeScenario.ActiveEmployeeAsync(services, "rule-many-second");

            var clash = await assignments.SetHeadAsync(structure, civil, second, Opened.AddDays(-10));

            clash.Succeeded.Should().BeFalse();
            clash.Errors.Should().Contain(error => error.Rule == DimensionRule.SingleHeadPerUnit);
        });

    /// <summary>
    /// A head appointment counts toward nothing: not headcount, and so not cost.
    /// </summary>
    /// <remarks>
    /// The claim that decided the design. Under the rejected "flag on an assignment row" model this
    /// person would have had to carry an allocation at each of the three departments, and would then
    /// have been counted by each of their headcounts and charged to each of their cost centres —
    /// three thirds of a general manager appearing in three places that never employed them.
    ///
    /// Both halves are asserted: heading three and being placed in none counts nowhere, and being
    /// placed in one of the three counts once, there, and still nowhere else.
    /// </remarks>
    [Fact]
    public async Task AHeadIsCountedByNoUnitTheyHeadAndOnceByTheUnitTheyWorkIn() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "rule-count");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var civil = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-count-civil", Opened);
            var electrical = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-count-elec", Opened);
            var mechanical = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-count-mech", Opened);

            var units = new[] { civil, electrical, mechanical };
            var manager = await EmployeeScenario.ActiveEmployeeAsync(services, "rule-count-gm");

            foreach (var unit in units)
            {
                (await assignments.SetHeadAsync(structure, unit, manager, Opened)).Succeeded.Should().BeTrue();
            }

            var headingOnly = await assignments.CountEmployeesAtAsync(structure, units, Opened);

            headingOnly.Should().BeEmpty(
                "heading three departments and working in none puts nobody in any of them — a node "
                + "with nobody at it is absent rather than present with zero");

            // Now place them in one of the three. They are a member of that one, and still only a
            // head of the other two.
            (await assignments.PlaceAsync(manager, structure, civil, Opened)).Succeeded.Should().BeTrue();

            var placed = await assignments.CountEmployeesAtAsync(structure, units, Opened);

            placed[civil].Should().Be(1, "they work here, and the appointment adds nothing to that");
            placed.Should().NotContainKey(electrical);
            placed.Should().NotContainKey(mechanical);
        });

    /// <summary>
    /// A head need not work in the unit they head, and the card still names them.
    /// </summary>
    /// <remarks>
    /// Architecture section 8's seed data requires "a unit head who is not a member of the unit",
    /// and an acting head borrowed from another department is ordinary. This is the case the
    /// rejected design could not express at all.
    /// </remarks>
    [Fact]
    public async Task AnActingHeadFromAnotherDepartmentIsAllowedAndNamed() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "rule-acting");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var lookups = services.GetRequiredService<IEnumerable<IEmployeeLookup>>();

            var theirs = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-acting-home", Opened);
            var covered = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-acting-covered", Opened);

            var acting = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rule-acting-1", nameEn: "Salma Darwish");

            (await assignments.PlaceAsync(acting, structure, theirs, Opened)).Succeeded.Should().BeTrue();

            var appointed = await assignments.SetHeadAsync(structure, covered, acting, Opened);

            appointed.Succeeded.Should().BeTrue(
                string.Join("; ", appointed.Errors.Select(error => error.Message.Value)));

            // Named on the unit they cover, and counted only on the one they work in.
            var head = await assignments.GetHeadAsync(structure, covered, Opened);

            head!.EmployeeId.Should().Be(acting);

            var name = await lookups.First().GetAsync(acting);
            name!.NameEn.Should().Be("Salma Darwish");

            var counts = await assignments.CountEmployeesAtAsync(structure, [theirs, covered], Opened);

            counts[theirs].Should().Be(1);
            counts.Should().NotContainKey(covered);
        });

    /// <summary>
    /// The appointment is still there in the next request, and the batched read finds it.
    /// </summary>
    /// <remarks>
    /// Every other test here appoints and reads inside one shell scope, which is one YesSql
    /// session: the document is in the identity map and the index rows have not had to survive a
    /// commit to be found. A designer card never reads that way — the appointment is one request
    /// and the card that shows it is the next — so this one deliberately uses two scopes.
    ///
    /// Both reads, because they are different queries over the same write: <c>GetHeadAsync</c>
    /// fetches the document, and <c>GetHeadsAsync</c> — the one a row of cards uses — never loads
    /// a document at all and answers from <c>UnitHeadIndex</c> alone.
    /// </remarks>
    [Fact]
    public async Task AnAppointmentIsFoundByBothReadsInALaterRequest()
    {
        string structure = null!, unit = null!, head = null!;

        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);

            structure = await DimensionGraphScenario.StructureAsync(services, "rule-next");
            unit = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-next-civil", Opened);
            head = await EmployeeScenario.ActiveEmployeeAsync(services, "rule-next-1", nameEn: "Adeel Mahmood");

            var appointed = await services.GetRequiredService<IEmployeeAssignmentService>()
                .SetHeadAsync(structure, unit, head, Opened);

            appointed.Succeeded.Should().BeTrue(
                string.Join("; ", appointed.Errors.Select(error => error.Message.Value)));
        });

        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            (await assignments.GetHeadAsync(structure, unit, Opened))
                .Should().NotBeNull("the appointment outlives the request that made it");

            (await assignments.GetHeadsAsync(structure, [unit], Opened))
                .Should().ContainKey(unit, "which is the read every designer card is drawn from");

            // And the name beside it, which is the other half of what the card shows.
            var head = (await assignments.GetHeadsAsync(structure, [unit], Opened))[unit].EmployeeId;
            var person = await services.GetRequiredService<IEnumerable<IEmployeeLookup>>().First()
                .GetManyAsync([head]);

            person.Should().ContainKey(head);
            person[head].NameEn.Should().Be("Adeel Mahmood");
        });
    }

    /// <summary>
    /// The exit dry run lists every headship, and the exit ends every one of them.
    /// </summary>
    /// <remarks>
    /// Across several units and several axes, because "every" is the word that matters: an exit that
    /// closed the first headship it found would pass a single-unit test and leave a leaver running
    /// two departments. Prompt 5's routing reads the same source, so the consequence is approvals
    /// routing to somebody who no longer works here, silently.
    /// </remarks>
    [Fact]
    public async Task TheExitListsAndEndsEveryHeadshipOnEveryAxis() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var organisation = await DimensionGraphScenario.StructureAsync(services, "rule-exit-org");
            var cost = await DimensionGraphScenario.StructureAsync(services, "rule-exit-cost");

            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var first = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-exit-a", Opened);
            var second = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-exit-b", Opened);
            var centre = await DimensionGraphScenario.RecordAsync(services, types.Department, "rule-exit-cc", Opened);

            var leaver = await EmployeeScenario.ActiveEmployeeAsync(services, "rule-exit-1");

            (await assignments.SetHeadAsync(organisation, first, leaver, Opened)).Succeeded.Should().BeTrue();
            (await assignments.SetHeadAsync(organisation, second, leaver, Opened)).Succeeded.Should().BeTrue();
            (await assignments.SetHeadAsync(cost, centre, leaver, Opened)).Succeeded.Should().BeTrue();

            var lastDay = new DateOnly(2026, 7, 31);

            var plan = await employees.PlanExitAsync(leaver, lastDay);

            plan.Succeeded.Should().BeTrue(plan.Describe());

            plan.Value!.UnitsLosingTheirHead.Select(unit => unit.RecordCode)
                .Should().BeEquivalentTo(
                    ["rule-exit-a", "rule-exit-b", "rule-exit-cc"],
                    "all three, across both axes — the dry run is what somebody acts on");

            var exit = await employees.ExitAsync(leaver, lastDay);

            exit.Succeeded.Should().BeTrue(exit.Describe());
            exit.Value!.HeadshipsClosed.Should().Be(3);

            foreach (var (structure, unit) in new[]
            {
                (organisation, first),
                (organisation, second),
                (cost, centre),
            })
            {
                (await assignments.GetHeadAsync(structure, unit, lastDay))
                    .Should().NotBeNull("they still led it on their last day");

                (await assignments.GetHeadAsync(structure, unit, lastDay.AddDays(1)))
                    .Should().BeNull("and nothing they led outlives them");
            }

            (await assignments.GetHeadshipsOfAsync(leaver, lastDay.AddDays(1))).Should().BeEmpty();
        });
}
