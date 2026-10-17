using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Slice F6a: an employee holds a position, the position's Filled count says so, and the Holders
/// section lists them. Decisions D26 D–H, ADR-0026 and ADR-0029.
/// </summary>
/// <remarks>
/// <b>What F5+F7 left and this closes.</b> That slice built the whole position side and
/// deliberately stopped: <c>EmployeeAttachableDimensionTypeIds</c> was unchanged, so no employee
/// could be placed in a position and every <c>Filled</c> was structurally 0. Repointing them there
/// would have moved every demo employee's placement as a side effect of adding positions. F6a is
/// where the line is added, the grade column arrives (ADR-0026), and the count stops being zero by
/// construction.
///
/// <b>The assertion that is really about the decision</b> is
/// <see cref="ADepartmentCountsSomebodyOnceWhetherTheyArePlacedInItOrInAPositionInsideIt"/>.
/// ADR-0029 leaves a tenant with unit placements and position placements side by side, on purpose,
/// so the roll-up has to be right across two shapes of row at once — and that is the single most
/// likely place this slice double-counts somebody.
/// </remarks>
public sealed class EmployeesHoldPositionsTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    private static readonly DateOnly Today = new(2026, 10, 15);

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- the line F6a turns on ----------------------------------------------------------------

    /// <summary>
    /// A position is somewhere an employee may be placed — and the units that already held them
    /// still do.
    /// </summary>
    /// <remarks>
    /// <b>Both halves.</b> The first is the slice. The second is ADR-0029: the recipe row and the
    /// migration both <em>add</em>, so a tenant that places employees at departments and teams goes
    /// on doing so and nothing it had is withdrawn.
    /// </remarks>
    [Fact]
    public async Task APositionIsSomewhereAnEmployeeMayBePlacedAndTheUnitsStillAre() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!;

            var position = (await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId;
            var department = (await types.GetByCodeAsync("zenith-department"))!.DimensionTypeId;
            var team = (await types.GetByCodeAsync("zenith-team"))!.DimensionTypeId;

            organisation.PermitsEmployeesAt(position).Should().BeTrue("the one line slice F6 turns on");
            organisation.PermitsEmployeesAt(department).Should().BeTrue("and nothing is taken away");
            organisation.PermitsEmployeesAt(team).Should().BeTrue();

            // A division is not somewhere employees go on this chart, and still is not: the row
            // added one type rather than restating the set.
            organisation.PermitsEmployeesAt((await types.GetByCodeAsync("zenith-division"))!.DimensionTypeId)
                .Should().BeFalse("an additive row states what to add, not what the set is");
        });

    /// <summary>
    /// A structure that places employees <em>anywhere</em> already permits a position, so the
    /// positions recipe applies, leaves the set empty, and positions are selectable.
    /// </summary>
    /// <remarks>
    /// <b>A real failure, reported from the Default tenant on 15 October.</b> The additive row
    /// <em>refused</em> this case — <em>"this would narrow it rather than widen it; state the whole
    /// set instead"</em> — and the whole recipe stopped. Refusing was wrong twice over: an empty
    /// <c>EmployeeAttachableDimensionTypeIds</c> means <em>unconstrained</em>, so the goal the row
    /// is asking for was already met; and the remedy it suggested — restate the whole set — is
    /// precisely what the additive form exists to avoid.
    ///
    /// <b>The set must stay empty.</b> Writing <c>[zenith-position]</c> into it would be the only
    /// way to "add" the type and would narrow the axis from <em>anything may hold employees</em> to
    /// <em>only a position may</em> — an additive row taking departments and teams away. So the
    /// assertion is not merely that the recipe succeeded but that nothing was written, which is the
    /// half a passing recipe would hide.
    ///
    /// <c>Migrations.UpdateFrom14Async</c> leaves an empty set alone for the same reason, and
    /// <c>DimensionsMigrationUpgradeTenantTests</c> holds that end.
    /// </remarks>
    [Fact]
    public async Task AStructureThatPlacesEmployeesAnywhereIsAlreadySatisfiedAndIsLeftAlone()
    {
        using var anywhere = new BaseTenantFixture();

        await anywhere.InitializeAsync();

        await ApplyToAsync(anywhere, "organisation-designer-zenith.recipe.json");

        // The reported tenant's shape: zenith-org placing employees anywhere. Set before the
        // positions recipe runs, because it is the state that recipe met and refused.
        await InTenantAsSystemAsync(anywhere, async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();
            var organisation = (await structures.GetByCodeAsync("zenith-org"))!;

            var opened = await structures.UpdateAsync(
                organisation.StructureId,
                organisation.Name,
                organisation.DimensionTypeIds,
                organisation.AllowSkipLevel,
                organisation.IsStrict,
                organisation.IsPrimaryOrganisation,
                new StructureShape(
                    organisation.RootDimensionTypeIds,
                    organisation.Containment,
                    // Unconstrained: employees may sit anywhere on this axis.
                    []),
                organisation.ShortName);

            opened.Succeeded.Should().BeTrue(
                string.Join("; ", opened.Errors.Select(error => error.Message.Value)));

            (await structures.GetByCodeAsync("zenith-org"))!
                .EmployeeAttachableDimensionTypeIds.Should().BeEmpty("the premise of this test");
        });

        // The failure, as reported: this threw at the 'structures' step.
        await ApplyToAsync(anywhere, "organisation-designer-zenith-positions.recipe.json");

        await InTenantAsSystemAsync(anywhere, async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();

            var organisation = (await structures.GetByCodeAsync("zenith-org"))!;
            var position = (await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId;

            organisation.EmployeeAttachableDimensionTypeIds.Should().BeEmpty(
                "an empty set is unconstrained, and narrowing it to one type is what the step must "
                + "never do");

            organisation.PermitsEmployeesAt(position).Should().BeTrue(
                "which is why there was nothing to add: it was already permitted");

            // And the rest of the structure is untouched — a skipped type does not become a reason
            // to skip the level and the containment pairing beside it.
            organisation.DimensionTypeIds.Should().Contain(position, "the level was still added");
        });

        // A position really is selectable for placement, which is the thing the tenant wanted.
        await InTenantAsSystemAsync(anywhere, async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var hrm1 = (await records.GetByCodeAsync("zenith-grade-hrm-1"))!.RecordId;

            var placed = await assignments.PlaceAsync(
                "anywhere-employee", organisation, hrManager, Today, gradeRecordId: hrm1);

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));
        });

        // And a re-run is a full no-op: nothing to add, nothing written, no refusal.
        await ApplyToAsync(anywhere, "organisation-designer-zenith-positions.recipe.json");

        await InTenantAsSystemAsync(anywhere, async services =>
        {
            (await services.GetRequiredService<IStructureService>().GetByCodeAsync("zenith-org"))!
                .EmployeeAttachableDimensionTypeIds.Should().BeEmpty();
        });

        await anywhere.DisposeAsync();
    }

    /// <summary>Re-running the positions recipe on an ordinary tenant changes nothing either.</summary>
    /// <remarks>
    /// The non-empty half of the pair above. The set already names the position after the first
    /// run, so the second adds nothing — and the "nothing to do, return before the write" check is
    /// what makes that a no-op in the audit trail as well as in the data.
    /// </remarks>
    [Fact]
    public async Task ReRunningThePositionsRecipeOnAStatedSetIsANoOp()
    {
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!;

            var position = (await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId;

            organisation.EmployeeAttachableDimensionTypeIds.Should().HaveCount(
                3, "department, team and position — appended once, not once per run");

            organisation.EmployeeAttachableDimensionTypeIds.Should().Contain(position);
            organisation.EmployeeAttachableDimensionTypeIds.Should().OnlyHaveUniqueItems();
        });
    }

    /// <summary>
    /// The headline: assigning somebody to HR Manager at HRM-2 takes Filled from 0 to 1 and puts
    /// them in the Holders list.
    /// </summary>
    /// <remarks>
    /// <b>Through the same write path the placement screen uses</b> — <c>PlaceAsync</c>, which is a
    /// one-row <c>ReallocateAsync</c> rather than a second mechanism. A test that wrote the
    /// assignment document directly would prove the storage and nothing about the slice.
    /// </remarks>
    [Fact]
    public async Task AssigningSomebodyToAPositionTakesFilledFromZeroToOneAndListsThem() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var hrm2 = (await records.GetByCodeAsync("zenith-grade-hrm-2"))!.RecordId;

            // Before: approved for three, held by nobody.
            var before = (await positions.GetEstablishmentAsync([hrManager], Today))[hrManager];

            before.Approved.Should().Be(3);
            before.Filled.Should().Be(0, "nobody holds a position until somebody is assigned one");

            (await positions.GetHoldersAsync(hrManager, Today)).Items.Should().BeEmpty();

            var placed = await assignments.PlaceAsync(
                "f6a-employee-1", organisation, hrManager, Today, gradeRecordId: hrm2);

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));

            // After: one of three filled, two vacant, and the person is named.
            var after = (await positions.GetEstablishmentAsync([hrManager], Today))[hrManager];

            after.Filled.Should().Be(1, "Filled is the number of employees holding the post");
            after.Vacant.Should().Be(2);
            after.IsFull.Should().BeFalse();

            var holders = await positions.GetHoldersAsync(hrManager, Today);

            holders.Total.Should().Be(1);
            holders.Items[0].EmployeeId.Should().Be("f6a-employee-1");
            holders.Items[0].Grade!.Code.Should().Be("zenith-grade-hrm-2", "at the grade they were given");
            holders.Items[0].Since.Should().Be(Today);
        });

    /// <summary>A grade that is not one of the position's allowed grades is refused.</summary>
    /// <remarks>
    /// <b>Blocking, where a full position only warns.</b> D26 F offers a full position with a
    /// warning because refusing it would stop the product recording what an organisation has
    /// actually done. A grade outside the set is not that: it is a level the post does not have,
    /// and there is nothing real for it to be recording.
    ///
    /// Enforced in the validator rather than only on the form, so the screen, the recipe step and
    /// any later import refuse it in the same words — CLAUDE.md rule 5.
    /// </remarks>
    [Fact]
    public async Task AGradeThePositionDoesNotAllowIsRefused() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;

            // SE-1 is the Software Engineer's level, not the HR Manager's.
            var se1 = (await records.GetByCodeAsync("zenith-grade-se-1"))!.RecordId;

            var refused = await assignments.PlaceAsync(
                "f6a-employee-wrong-grade", organisation, hrManager, Today, gradeRecordId: se1);

            refused.Succeeded.Should().BeFalse();

            var message = string.Join("; ", refused.Errors.Select(error => error.Message.Value));

            message.Should().Contain("SE-1", "the message names the grade");
            message.Should().Contain("HR Manager", "and the position that does not allow it");

            // And nothing was written: a refused placement leaves no half-made row behind.
            (await assignments.GetEffectiveAsync("f6a-employee-wrong-grade", organisation, Today))
                .Should().BeNull();
        });

    /// <summary>A grade on a placement at a unit rather than a position is refused.</summary>
    /// <remarks>
    /// A grade belongs to a position (D26 C). Under ADR-0029 a tenant has unit placements and
    /// position placements side by side, so this is not a hypothetical shape — it is the one every
    /// employee on the tenant is in today, and a grade on one of those rows would mean nothing.
    /// </remarks>
    [Fact]
    public async Task AGradeOnAPlacementAtAUnitIsRefused() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var civil = (await records.GetByCodeAsync("zenith-dept-civil"))!.RecordId;
            var hrm1 = (await records.GetByCodeAsync("zenith-grade-hrm-1"))!.RecordId;

            var refused = await assignments.PlaceAsync(
                "f6a-employee-unit-grade", organisation, civil, Today, gradeRecordId: hrm1);

            refused.Succeeded.Should().BeFalse();

            string.Join("; ", refused.Errors.Select(error => error.Message.Value))
                .Should().Contain("A grade belongs to a position");
        });

    /// <summary>
    /// A placement at a unit with no grade still works where it is still allowed, and the row it
    /// writes is unchanged.
    /// </summary>
    /// <remarks>
    /// <b>Two decisions meet here and the test had to move to keep saying something true.</b> It
    /// used to place somebody at a Zenith department, which D30 now refuses — correctly. The claim
    /// is still worth making: a unit placement carries no grade, invents none, and the shape is not
    /// second-class. So it is made on an axis that has no positions, where D30 does not apply and a
    /// tenant goes on working exactly as it did.
    ///
    /// The <em>other</em> half — that a row already written at a Zenith department keeps resolving
    /// — is <c>EmployeesJoinThroughPositionsTenantTests.APlacementAlreadyAtADepartmentKeepsWorking</c>,
    /// which is where ADR-0029's promise is pinned.
    /// </remarks>
    [Fact]
    public async Task APlacementAtAUnitWithNoGradeIsUnchangedWhereUnitsStillHoldPeople()
    {
        await ApplyAsync("organisation-designer-crescent.recipe.json");

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            // Crescent has no positions, so it is not a position-based axis and D30 says nothing
            // about it.
            var crescent = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("crescent-org"))!.StructureId;

            // Named rather than "the first department": Crescent has two Credits under different
            // branches, which is the whole reason that tenant exists, so a test that took whichever
            // came back first would be a test nobody could read.
            var record = await records.GetByCodeAsync("crescent-dept-lhr-credit");

            record.Should().NotBeNull("the Crescent recipe ships this department");

            var department = record!.RecordId;

            var placed = await assignments.PlaceAsync("f6a-legacy", crescent, department, Today);

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));

            var back = await assignments.GetEffectiveAsync("f6a-legacy", crescent, Today);

            back!.RecordId.Should().Be(department);
            back.GradeRecordId.Should().BeNull("no grade was recorded, and none was invented");
        });
    }

    // ---- the counting rule ADR-0029 makes necessary -------------------------------------------

    /// <summary>
    /// A department counts a person once whether they are placed in it directly or in a position
    /// inside it — and counts two different people as two.
    /// </summary>
    /// <remarks>
    /// <b>The test ADR-0029 asks for by name.</b> The decision leaves a tenant holding both shapes
    /// of row at once, deliberately, so the roll-up has to be correct across them. The failure this
    /// guards against is silent: every number stays plausible while being wrong, which is
    /// architecture section 4's own description of the worst failure mode in this design.
    ///
    /// The <c>DISTINCT EmployeeId</c> guarantee (D1, §2.5) is what makes it work, and it has never
    /// before been exercised against a person reachable through two different kinds of node.
    /// </remarks>
    [Fact]
    public async Task ADepartmentCountsSomebodyOnceWhetherTheyArePlacedInItOrInAPositionInsideIt()
    {
        // ITS OWN TENANT, UPGRADED IN ORDER, because that is the only honest way to hold both
        // shapes of row at once. Since D30 a direct department placement can no longer be written
        // as a NEW one — that is the rule — so the legacy row has to be written when it was
        // written: before the positions recipe ever ran. Applying the base Zenith chart, placing
        // somebody in HR, and only then applying the positions recipe IS the upgrade sequence
        // ADR-0029 describes, step for step.
        using var upgrading = new BaseTenantFixture();

        await upgrading.InitializeAsync();

        await ApplyToAsync(upgrading, "organisation-designer-zenith.recipe.json");

        await InTenantAsSystemAsync(upgrading, async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var structures = services.GetRequiredService<IStructureService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await structures.GetByCodeAsync("zenith-org"))!.StructureId;
            var hr = (await records.GetByCodeAsync("zenith-dept-hr"))!.RecordId;

            (await assignments.PlaceAsync("f6a-count-direct", organisation, hr, Today))
                .Succeeded.Should().BeTrue("this is the row every pre-F6 employee has");
        });

        // The upgrade itself: positions arrive on a chart that already has people in departments.
        await ApplyToAsync(upgrading, "organisation-designer-zenith-positions.recipe.json");

        await InTenantAsSystemAsync(upgrading, async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            // HR Manager sits in the HR department, so somebody holding it is in HR.
            var hr = (await records.GetByCodeAsync("zenith-dept-hr"))!.RecordId;
            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var hrm1 = (await records.GetByCodeAsync("zenith-grade-hrm-1"))!.RecordId;

            // One person the new way: placed in a position inside it.
            (await assignments.PlaceAsync(
                "f6a-count-via-position", organisation, hrManager, Today, gradeRecordId: hrm1))
                .Succeeded.Should().BeTrue();

            // The roll-up: both shapes of row reach the department, and they are two people.
            var under = await assignments.GetEmployeesUnderAsync(organisation, hr, Today);

            under.Items.Select(row => row.EmployeeId).Distinct(StringComparer.Ordinal)
                .Should().BeEquivalentTo(["f6a-count-direct", "f6a-count-via-position"]);

            // And the DIRECT count stays what it says on the tin — the node itself, not its
            // descendants. The two numbers are the pair the value page prints side by side, and
            // they must not drift into meaning the same thing: a department whose people all hold
            // positions inside it has a direct count of zero and is not empty, which is exactly the
            // misreading D24 put both numbers on the page to prevent.
            var direct = await assignments.CountEmployeesAtAsync(organisation, [hr], Today);

            direct[hr].Should().Be(1, "only the person placed in the department itself is at it");

            var atThePosition = await assignments.CountEmployeesAtAsync(organisation, [hrManager], Today);

            atThePosition[hrManager].Should().Be(1, "the other one is at the position inside it");
        });
    }

    /// <summary>A split allocation across two positions is one holder of each, not two.</summary>
    /// <remarks>
    /// The Zenith matrix with positions on the end of the rows, which ADR-0022 says is the same
    /// rows and the same rules. A <c>Filled</c> that counted assignment rows would read 1 for a
    /// post held by half a person twice over.
    /// </remarks>
    [Fact]
    public async Task ASplitAcrossTwoPositionsIsOneHolderOfEach() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var engineer = (await records.GetByCodeAsync("zenith-pos-se"))!.RecordId;
            var supervisor = (await records.GetByCodeAsync("zenith-pos-site-supervisor"))!.RecordId;
            var se1 = (await records.GetByCodeAsync("zenith-grade-se-1"))!.RecordId;
            var ss1 = (await records.GetByCodeAsync("zenith-grade-ss-1"))!.RecordId;

            var split = await assignments.ReallocateAsync(
                "f6a-matrix",
                organisation,
                [
                    new AssignmentSplitEntry(engineer, 60m, IsPrimary: true, se1),
                    new AssignmentSplitEntry(supervisor, 40m, IsPrimary: false, ss1),
                ],
                Today);

            split.Succeeded.Should().BeTrue(
                string.Join("; ", split.Errors.Select(error => error.Message.Value)));

            var establishment = await positions.GetEstablishmentAsync([engineer, supervisor], Today);

            establishment[engineer].Filled.Should().Be(1, "one person, not 0.6 of one and not two");
            establishment[supervisor].Filled.Should().Be(1);

            // And each post lists them once, at the grade that post carries — which is the whole
            // reason ADR-0026 put the grade on the row rather than on an axis of its own.
            (await positions.GetHoldersAsync(engineer, Today)).Items.Should().ContainSingle()
                .Which.Grade!.Code.Should().Be("zenith-grade-se-1");

            (await positions.GetHoldersAsync(supervisor, Today)).Items.Should().ContainSingle()
                .Which.Grade!.Code.Should().Be("zenith-grade-ss-1");
        });

    /// <summary>
    /// A grade removed from a position does not break the record of somebody holding it at that
    /// grade.
    /// </summary>
    /// <remarks>
    /// D26 G in terms: <em>"removing a grade from a Position an employee currently holds warns and
    /// never breaks that employee's record. The stored grade stays and keeps resolving; what
    /// changes is what is offered next time."</em>
    ///
    /// The grade is read undated for exactly this, so a holder list shows what was recorded rather
    /// than a blank where the set no longer matches.
    /// </remarks>
    [Fact]
    public async Task AGradeRemovedFromAPositionStillResolvesOnTheRowThatCarriesIt() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var engineer = (await records.GetByCodeAsync("zenith-pos-se"))!.RecordId;
            var se1 = (await records.GetByCodeAsync("zenith-grade-se-1"))!.RecordId;
            var se2 = (await records.GetByCodeAsync("zenith-grade-se-2"))!.RecordId;
            var se3 = (await records.GetByCodeAsync("zenith-grade-se-3"))!.RecordId;

            (await assignments.PlaceAsync("f6a-kept-grade", organisation, engineer, Today, gradeRecordId: se1))
                .Succeeded.Should().BeTrue();

            // SE-1 is taken off the position's allowed set.
            (await records.SetReferencesAsync(engineer, DimensionReferenceKinds.AllowedGrade, [se2, se3]))
                .Succeeded.Should().BeTrue();

            (await positions.GetAllowedGradesAsync(engineer, Today)).Select(grade => grade.Code)
                .Should().NotContain("zenith-grade-se-1", "it is not offered any more");

            var holder = (await positions.GetHoldersAsync(engineer, Today)).Items
                .Single(row => row.EmployeeId == "f6a-kept-grade");

            holder.Grade!.Code.Should().Be(
                "zenith-grade-se-1",
                "the stored grade stays and keeps resolving — D26 G");
        });

    // ---- the page ------------------------------------------------------------------------------

    /// <summary>The position page's Holders section stops being empty and names who, at what, since.</summary>
    [Fact]
    public async Task ThePositionPageListsItsHolders()
    {
        var hrManagerId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            hrManagerId = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var hrm3 = (await records.GetByCodeAsync("zenith-grade-hrm-3"))!.RecordId;

            (await assignments.PlaceAsync(
                "f6a-page-holder", organisation, hrManagerId, Today, gradeRecordId: hrm3))
                .Succeeded.Should().BeTrue();
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Dimensions/Records/Details/{hrManagerId}");

        page.Should().Contain("value-holders", "the section is a table now, not the empty line");
        page.Should().NotContain("value-holders-none");
        page.Should().Contain("HRM-3", "the grade they hold it at");

        // And the headline numbers moved with it.
        page.Should().Contain("1 filled");
        page.Should().Contain("2 vacant");
    }

    // ---- helpers --------------------------------------------------------------------------------

    // A LEGACY-PLACEMENT HELPER USED TO LIVE HERE, and it is worth saying why it does not.
    //
    // It made a pre-F6 row by removing the position level from zenith-org, writing the placement
    // while D30's rule could not see a position on the axis, and restoring the level by re-applying
    // the recipe. The write worked. The restore did not: removing a level closes every link of that
    // type (`OnAxisUntil`, and `Parents` capped the day before — DimensionGraphService
    // .OnStructureLevelsChangedAsync), and re-adding the level only recomputes the closure from
    // those links, which are still closed. So every position came back orphaned, HR no longer
    // contained HR Manager, and the roll-up this test is about could not see anybody.
    //
    // THAT IS A REAL ENGINE DEFECT AND IT IS NOT THIS TEST'S TO FIX — what re-adding a level should
    // do to links its removal closed is a decision, not a tidy-up. It is written up for the
    // reviewer. What this test needed was never the round trip: it was a tenant holding both shapes
    // of row, and the truthful way to get one is to upgrade a tenant in order, which is what
    // ADepartmentCountsSomebodyOnceWhetherTheyArePlacedInItOrInAPositionInsideIt now does.

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        InTenantAsSystemAsync(_tenant, work);

    private static Task InTenantAsSystemAsync(BaseTenantFixture tenant, Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("F6a test"))
            {
                await work(services);
            }
        });

    private Task ApplyAsync(string fileName) => ApplyToAsync(_tenant, fileName);

    private static async Task ApplyToAsync(BaseTenantFixture tenant, string fileName)
    {
        var path = Path.Combine(RecipesFolder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' ships with the module", fileName);

        await tenant.InTenantAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            var descriptor = new RecipeDescriptor
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                BasePath = string.Empty,
                FileProvider = fileProvider,
                RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                RequireNewScope = false,
            };

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                descriptor,
                new Dictionary<string, object>(),
                CancellationToken.None);
        });
    }

    private static string RecipesFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkMate.sln")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull("the test runs inside the repository");

            return Path.Combine(directory!.FullName, "src", "WorkMate.Dimensions", "Recipes");
        }
    }
}
