using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Slice F6b: an employee joins the organisation only through a position, and everything about
/// where they sit is derived from it. Decision D30, ADR-0030.
/// </summary>
/// <remarks>
/// <b>The rule and its boundary, which are equally important.</b> D30 makes a unit not somewhere an
/// employee goes — on an axis that <em>has</em> positions. ADR-0029 keeps every row already written
/// valid, and a tenant with no positions keeps today's screens exactly. Three claims, and getting
/// the second or third wrong would break a live tenant while the first still passed.
/// </remarks>
public sealed class EmployeesJoinThroughPositionsTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    private static readonly DateOnly Today = new(2026, 10, 15);

    /// <summary>Three people in one post — D26 D's own example, and the several-holders case.</summary>
    private static readonly string[] ThreeDirectors = ["f6b-dir-a", "f6b-dir-b", "f6b-dir-c"];

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- the rule ------------------------------------------------------------------------------

    /// <summary>
    /// A new placement at a department of a position-based axis is refused, and says what to do.
    /// </summary>
    /// <remarks>
    /// <b>Server-side, not only in the UI.</b> CLAUDE.md rule 5 and ADR-0030: the screen, the recipe
    /// step and any later import all go through <c>ReallocateAsync</c>, so stating the rule once in
    /// the validator is what makes all three refuse it in the same words. A screen that merely
    /// stopped offering units would leave the old shape reachable by every other route.
    /// </remarks>
    [Fact]
    public async Task ANewPlacementAtADepartmentOfAPositionBasedAxisIsRefused() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var civil = (await records.GetByCodeAsync("zenith-dept-civil"))!.RecordId;

            var refused = await assignments.PlaceAsync("f6b-at-a-unit", organisation, civil, Today);

            refused.Succeeded.Should().BeFalse("D30: a unit is not somewhere an employee goes");

            var message = string.Join("; ", refused.Errors.Select(error => error.Message.Value));

            message.Should().Contain("Civil", "the message names the unit");
            message.Should().Contain("choose a position instead", "and says what to do about it");

            // Nothing written: a refused placement leaves no half-made row.
            (await assignments.GetEffectiveAsync("f6b-at-a-unit", organisation, Today))
                .Should().BeNull();
        });

    /// <summary>A placement at a position on the same axis is accepted, which is the other half.</summary>
    [Fact]
    public async Task APlacementAtAPositionOnThatAxisIsAccepted() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var hrm2 = (await records.GetByCodeAsync("zenith-grade-hrm-2"))!.RecordId;

            var placed = await assignments.PlaceAsync(
                "f6b-at-a-position", organisation, hrManager, Today, gradeRecordId: hrm2);

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));
        });

    /// <summary>
    /// A placement already at a department is untouched by the rule, and still resolves.
    /// </summary>
    /// <remarks>
    /// <b>ADR-0029, as a test, and the one that would break a live tenant if D30 were written
    /// wrongly.</b> The refusal is on the write, so a row that is already there keeps working: the
    /// person is still placed, the department still counts them, and nothing has been invalidated.
    /// Written here by reaching round the validator the only way a tenant legitimately has one —
    /// the row predates the rule — which is what the recipe-built demo employees are.
    /// </remarks>
    [Fact]
    public async Task APlacementAlreadyAtADepartmentKeepsWorking()
    {
        var organisationId = string.Empty;
        var civilId = string.Empty;

        // Placed while the axis still accepted units: positions are taken off the attachable set
        // for the length of the write, which is exactly the state every pre-F6 row was written in.
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var structures = services.GetRequiredService<IStructureService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var types = services.GetRequiredService<IDimensionTypeService>();

            var organisation = (await structures.GetByCodeAsync("zenith-org"))!;

            organisationId = organisation.StructureId;
            civilId = (await records.GetByCodeAsync("zenith-dept-civil"))!.RecordId;

            var positionType = (await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId;

            // Drop the position level, so the axis is not position-based for this one write.
            var legacy = await structures.UpdateAsync(
                organisation.StructureId,
                organisation.Name,
                [.. organisation.DimensionTypeIds.Where(id => id != positionType)],
                organisation.AllowSkipLevel,
                organisation.IsStrict,
                organisation.IsPrimaryOrganisation,
                new StructureShape(
                    organisation.RootDimensionTypeIds,
                    [.. organisation.Containment.Where(pair =>
                        pair.ParentDimensionTypeId != positionType &&
                        pair.ChildDimensionTypeId != positionType)],
                    [.. organisation.EmployeeAttachableDimensionTypeIds.Where(id => id != positionType)]),
                organisation.ShortName);

            legacy.Succeeded.Should().BeTrue(
                string.Join("; ", legacy.Errors.Select(error => error.Message.Value)));

            (await assignments.PlaceAsync("f6b-legacy", organisationId, civilId, Today))
                .Succeeded.Should().BeTrue("this is the row every pre-F6 employee has");
        });

        // Put the position level back, which is the upgraded state.
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");

        await InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var organisation = services.GetRequiredService<IEmployeeOrganisationService>();

            var still = await assignments.GetEffectiveAsync("f6b-legacy", organisationId, Today);

            still.Should().NotBeNull("ADR-0029: nothing already written is invalidated");
            still!.RecordId.Should().Be(civilId, "same unit, same row");

            // And the department still counts them, so no roll-up changed underneath anybody.
            (await assignments.CountEmployeesAtAsync(organisationId, [civilId], Today))[civilId]
                .Should().Be(1);

            // The screen shows it as the state it is, rather than as an empty derived block.
            var derived = await organisation.GetAsync("f6b-legacy", Today);

            derived!.HasNoPositionYet.Should().BeTrue("D28 C: made legible rather than hidden");
            derived.LegacyUnitPath.Should().Contain("Civil");
            derived.Position.Should().BeNull();
        });
    }

    // ---- the derived block ---------------------------------------------------------------------

    /// <summary>
    /// The organisation chain is computed from the position and labelled with the tenant's own
    /// dimension names. Decisions D28 D and D30.
    /// </summary>
    /// <remarks>
    /// <b>Both halves.</b> Computed, so nothing is stored on the employee and the block is correct
    /// the moment a position moves. Labelled with the tenant's own names, because a chart with
    /// Regions and Branches is as ordinary as one with Divisions, and FRS Rule 6 lets a customer
    /// rename either — four hard-coded words would be right for Zenith and wrong for Crescent.
    /// </remarks>
    [Fact]
    public async Task TheOrganisationChainIsComputedAndUsesTheTenantsOwnDimensionNames() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var organisation = services.GetRequiredService<IEmployeeOrganisationService>();

            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;

            var preview = await organisation.PreviewAsync(hrManager, Today);

            preview.Chain.Select(step => step.Name).Should().Equal(
                ["Corporate", "HR"], "root first, down to the unit the post sits in");

            preview.Chain.Select(step => step.DimensionName).Should().Equal(
                ["Division", "Department"],
                "the tenant's own names for those levels, never four hard-coded words");
        });

    /// <summary>All four Reports-to cases. Decision D28 A.</summary>
    /// <remarks>
    /// <b>The third is the one with a decision in it.</b> With several holders nothing picks one:
    /// any rule that chose — longest-serving, highest grade — would invent a line manager the
    /// customer never stated, and would be wrong silently. The post is named with its count.
    /// </remarks>
    [Fact]
    public async Task AllFourReportsToCasesAnswerAsD28ASays() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var organisation = services.GetRequiredService<IEmployeeOrganisationService>();

            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var ceo = (await records.GetByCodeAsync("zenith-pos-ceo"))!.RecordId;
            var director = (await records.GetByCodeAsync("zenith-pos-dir-tech"))!.RecordId;
            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var ceo1 = (await records.GetByCodeAsync("zenith-grade-ceo-1"))!.RecordId;

            // 1. The CEO reports to nobody.
            (await organisation.PreviewAsync(ceo, Today)).ReportsTo.Kind
                .Should().Be(ReportsToKind.Top);

            // 2. The Director reports to the CEO, which nobody holds yet.
            var vacant = (await organisation.PreviewAsync(director, Today)).ReportsTo;

            vacant.Kind.Should().Be(ReportsToKind.Vacant);
            vacant.PositionName.Should().Be("CEO");
            vacant.DefaultLineManager.Should().BeNull("there is nobody to default to");

            // 3. Somebody takes the CEO post, so the Director now reports to a person.
            (await assignments.PlaceAsync("f6b-the-ceo", axis, ceo, Today, gradeRecordId: ceo1))
                .Succeeded.Should().BeTrue();

            var one = (await organisation.PreviewAsync(director, Today)).ReportsTo;

            one.Kind.Should().Be(ReportsToKind.OneHolder);
            one.DefaultLineManager!.EmployeeId.Should().Be(
                "f6b-the-ceo", "exactly one holder is a default line manager — D28 A");

            // 4. HR Manager reports to the Director, and three people hold the Director's post.
            var dir1 = (await records.GetByCodeAsync("zenith-grade-dir-1"))!.RecordId;

            foreach (var who in ThreeDirectors)
            {
                (await assignments.PlaceAsync(who, axis, director, Today, gradeRecordId: dir1))
                    .Succeeded.Should().BeTrue();
            }

            var several = (await organisation.PreviewAsync(hrManager, Today)).ReportsTo;

            several.Kind.Should().Be(ReportsToKind.SeveralHolders);
            several.Holders.Should().HaveCount(3);

            several.DefaultLineManager.Should().BeNull(
                "nothing picks one of several — that would invent a line manager, silently");
        });

    /// <summary>A full position previews a warning and still saves. Decision D26 F.</summary>
    /// <remarks>
    /// Refusing would stop the product recording what an organisation has actually done, which is
    /// not the engine's call to make. The warning is the point: somebody should know.
    /// </remarks>
    [Fact]
    public async Task AFullPositionWarnsAndStillSaves() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var organisation = services.GetRequiredService<IEmployeeOrganisationService>();

            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            // The CEO post is approved for one.
            var ceo = (await records.GetByCodeAsync("zenith-pos-ceo"))!.RecordId;
            var ceo1 = (await records.GetByCodeAsync("zenith-grade-ceo-1"))!.RecordId;

            (await assignments.PlaceAsync("f6b-full-first", axis, ceo, Today, gradeRecordId: ceo1))
                .Succeeded.Should().BeTrue();

            (await organisation.PreviewAsync(ceo, Today)).IsFull.Should().BeTrue(
                "one of one filled — the form shows a warning from this");

            // And a second holder is allowed anyway.
            var second = await assignments.PlaceAsync(
                "f6b-full-second", axis, ceo, Today, gradeRecordId: ceo1);

            second.Succeeded.Should().BeTrue(
                "D26 F: a full position warns and is never a hard block — "
                + string.Join("; ", second.Errors.Select(error => error.Message.Value)));
        });

    // ---- the axes the screens offer -------------------------------------------------------------

    /// <summary>
    /// A grade ladder and a reporting line are never offered as somewhere to place an employee.
    /// </summary>
    /// <remarks>
    /// Both are axes the Position type is a level of, and neither is anywhere a person goes:
    /// nothing ever writes an assignment on a reporting line (ADR-0028) and a grade is a reference
    /// rather than a placement (D26 C). They were always wrong to offer and became visible the
    /// moment the chooser was the only control left on the screen.
    /// </remarks>
    [Fact]
    public async Task TheGradeLadderAndTheReportingLineAreNotOfferedForPlacement() =>
        await InTenantAsSystemAsync(async services =>
        {
            var axes = await services.GetRequiredService<IPositionService>().GetEmployeeAxesAsync();

            var codes = axes.Select(axis => axis.Structure.Code).ToList();

            codes.Should().Contain("zenith-org", "the organisation chart is where people go");
            codes.Should().NotContain("zenith-reporting", "nothing ever writes an assignment there");
            codes.Should().NotContain("zenith-grades", "a grade is a reference, not a placement");

            axes.Single(axis => axis.Structure.Code == "zenith-org")
                .IsPositionBased.Should().BeTrue("it has a Position level, so D30 applies to it");
        });

    /// <summary>
    /// A tenant with no positions is unchanged: its axis is offered and is not position-based.
    /// </summary>
    /// <remarks>
    /// <b>D30's own scope, as a test.</b> The decision is about a structure that has positions; a
    /// tenant that has not built any keeps today's Placement screen and today's employee form
    /// exactly, with no errors and no empty drop-downs.
    /// </remarks>
    [Fact]
    public async Task ATenantWithNoPositionsIsUnchanged()
    {
        using var plain = new BaseTenantFixture();

        await plain.InitializeAsync();

        await ApplyToAsync(plain, "organisation-designer-demo.recipe.json");

        await InTenantAsSystemAsync(plain, async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var axes = await services.GetRequiredService<IPositionService>().GetEmployeeAxesAsync();

            var demo = axes.SingleOrDefault(axis => axis.Structure.Code == "demo-org");

            demo.Should().NotBeNull("the demo organisation is still somewhere people go");
            demo!.IsPositionBased.Should().BeFalse("it has no Position level, so D30 does not apply");

            // And placing somebody at a unit still works, exactly as it did.
            var department = (await records.GetByCodeAsync("demo-dept-retail"))!.RecordId;

            var placed = await assignments.PlaceAsync(
                "f6b-plain", demo.StructureId, department, Today);

            placed.Succeeded.Should().BeTrue(
                "a tenant with no positions keeps today's behaviour — "
                + string.Join("; ", placed.Errors.Select(error => error.Message.Value)));
        });

        await plain.DisposeAsync();
    }

    // ---- the screens ----------------------------------------------------------------------------

    /// <summary>
    /// The Placement screen asks for a position, shows the derived chain, and offers no unit
    /// picker, no Within and no allocation controls.
    /// </summary>
    [Fact]
    public async Task ThePlacementScreenAsksForAPositionAndNothingElse()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-screen", "Sara Ahmed");

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Placement/Place/{employeeId}");

        page.Should().Contain("data-position-picker", "D30: the question is which position");
        page.Should().Contain("Job and organisation");

        page.Should().NotContain("data-unit-picker", "a unit is not somewhere an employee goes");
        page.Should().NotContain("data-unit-picker-within", "and Within is off on every employee screen");
        page.Should().NotContain("AllocationPercent", "the answer is always 100 on this screen");
        page.Should().NotContain("This is the main placement", "and always the main one");

        // One eligible axis on this tenant, so no chooser at all.
        page.Should().NotContain("Choose a structure…");
    }

    /// <summary>
    /// The Placement screen shows the derived block for somebody who holds a position, and the
    /// "No position yet" state for somebody who does not.
    /// </summary>
    [Fact]
    public async Task ThePlacementScreenShowsTheDerivedBlockAndTheNoPositionState()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-derived", "Omar Said");

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;
            var hrm2 = (await records.GetByCodeAsync("zenith-grade-hrm-2"))!.RecordId;

            (await assignments.PlaceAsync(employeeId, axis, hrManager, Today, gradeRecordId: hrm2))
                .Succeeded.Should().BeTrue();
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Placement/Index/{employeeId}");

        page.Should().Contain("placement-job");
        page.Should().Contain("HR Manager");
        page.Should().Contain("HRM-2");
        page.Should().Contain("job-organisation-chain", "the derived chain");
        page.Should().Contain("Division:", "labelled with the tenant's own dimension names");
        page.Should().Contain("Department:");
        page.Should().Contain("placement-change-position");
        page.Should().Contain("placement-change-grade");
        page.Should().Contain("job-history", "and every dated period on record");

        page.Should().NotContain("placement-no-position");
    }

    // ---- item 1: the fixed "Job and organisation" block on Create and Edit ---------------------

    /// <summary>
    /// The Add-employee form carries the block: a position drop-down, a grade tied to it, and the
    /// derived chain beneath them.
    /// </summary>
    /// <remarks>
    /// <b>Why the question is on this form at all</b>, when the design note is emphatic that the
    /// editor stays read-only about placement: because this form already asks for a join date, and
    /// that is the effective date the write needs. CLAUDE.md rule 3 bans defaulting on a write, and
    /// nothing here defaults — the person typed the date. The editor has no date to offer and so
    /// links to the dated forms instead, which
    /// <see cref="TheEmployeePageShowsTheBlockReadOnlyAndLinksToTheDatedForms"/> holds.
    ///
    /// <b>Optional, and that is load-bearing.</b> A new employee with no position must stay
    /// creatable: making it required would break every tenant that hires before HR has built its
    /// position list, and would make ADR-0029's mixed state impossible to live with.
    /// </remarks>
    [Fact]
    public async Task TheAddEmployeeFormAsksForAPositionAndAGradeOfThatPosition()
    {
        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, "/Admin/Employees/Create");

        page.Should().Contain("create-job-heading", "the block is part of the fixed core");
        page.Should().Contain("data-position-picker", "D26 F: a position drop-down");
        page.Should().Contain("data-grade-picker", "D26 G: a grade limited to that position's");

        // D26 G: empty and disabled until a position is chosen. Nothing has been chosen on a form
        // that has only just been opened, so the control must arrive disabled.
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        var grade = document.QuerySelector("select[data-grade-picker]");

        grade.Should().NotBeNull();
        grade!.HasAttribute("disabled").Should()
            .BeTrue("a grade means nothing until there is a position to read its levels from");

        // Optional: the picker offers "no position", because an unplaced employee stays creatable.
        // Asserted on the WORDS rather than on the empty option, which is rendered either way: the
        // control's default wording for it is "— reports to nobody —", which is the right sentence
        // on the screen it was built for and nonsense on a form asking which post somebody holds.
        document.QuerySelector("select[data-position-picker] option[value='']")?.TextContent
            .Should().Be("— no position yet —",
                "leaving it blank creates an unplaced employee, and the option has to say so");

        // One eligible axis on this tenant, so the chooser is not shown at all.
        page.Should().NotContain("Choose a structure…");
    }

    /// <summary>
    /// Choosing a position on the Add form draws the organisation chain, <em>Reports to</em> and
    /// the access-roles notice — all from the position, before anybody has been created.
    /// </summary>
    /// <remarks>
    /// The redraw is a POST carrying <c>IsRefresh</c>, which is navigation rather than a save: the
    /// defect it was written against is a form that answered <em>"an employee code is needed"</em>
    /// the instant somebody picked a position, before they had been given the chance to type one.
    /// </remarks>
    [Fact]
    public async Task ChoosingAPositionOnTheAddFormDrawsTheDerivedBlockWithoutSaving()
    {
        var hrManager = await RecordIdAsync("zenith-pos-hr-manager");

        var page = await PostToCreateAsync(new Dictionary<string, string>
        {
            ["JoinDate"] = Today.ToIso(),
            ["PositionRecordId"] = hrManager,
            ["IsRefresh"] = "true",
        });

        // The chain, labelled with THIS tenant's own names for its levels — never four hard-coded
        // words. D28 D: a chart with Regions and Branches is as ordinary as one with Divisions.
        page.Should().Contain("job-organisation-chain");
        page.Should().Contain("Division:");
        page.Should().Contain("Department:");

        // D28 A, and HR Manager is the several-holders case only once somebody holds the post above
        // it; what must always be present is the row itself.
        page.Should().Contain("job-organisation-reports-to");

        // D26 I: raised before the save as well as after it.
        page.Should().Contain("create-access-roles");

        // And nothing was written: this was navigation.
        page.Should().NotContain("an employee code is needed", "a redraw does not validate a save");
    }

    /// <summary>
    /// A grade that is not one of the chosen position's is refused on the Add form, before the
    /// employee exists.
    /// </summary>
    /// <remarks>
    /// <b>The validator is still the authority</b> — it refuses the same pair on the write, which
    /// is what covers a recipe, an import and a replayed post, and
    /// <c>EmployeesHoldPositionsTenantTests.AGradeThePositionDoesNotAllowIsRefused</c> holds that
    /// end. What this holds is that the ordinary mistake is answered on the form that made it,
    /// rather than on the editor of a person who has just been created and half-placed.
    /// </remarks>
    [Fact]
    public async Task AGradeFromAnotherPositionIsRefusedOnTheAddFormAndNobodyIsCreated()
    {
        var hrManager = await RecordIdAsync("zenith-pos-hr-manager");
        var ceoGrade = await RecordIdAsync("zenith-grade-ceo-1");

        var page = await PostToCreateAsync(new Dictionary<string, string>
        {
            ["Code"] = "f6b-wrong-grade",
            ["NameEn"] = "Wrong Grade",
            ["JoinDate"] = Today.ToIso(),
            ["PositionRecordId"] = hrManager,
            ["GradeRecordId"] = ceoGrade,
        });

        // Read as a browser would. The message has an apostrophe in it, which the encoder spells
        // &#x27; — asserting on the raw markup would be an assertion about the encoder.
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        document.QuerySelector("[data-valmsg-for='GradeRecordId']")?.TextContent
            .Should().Be("Choose one of this position's own grades.",
                "the message belongs beside the control that caused it");

        await InTenantAsSystemAsync(async services =>
            (await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("f6b-wrong-grade"))
                .Should().BeNull("the form answered before anybody was created"));
    }

    /// <summary>
    /// Adding somebody with a position places them from their join date, through the same write
    /// path the Placement screen uses, and raises the access-roles reminder.
    /// </summary>
    [Fact]
    public async Task AddingSomebodyWithAPositionPlacesThemFromTheirJoinDate()
    {
        var hrManager = await RecordIdAsync("zenith-pos-hr-manager");
        var hrm1 = await RecordIdAsync("zenith-grade-hrm-1");
        var joinDate = new DateOnly(2026, 3, 1);

        await PostToCreateAsync(new Dictionary<string, string>
        {
            ["Code"] = "f6b-hired",
            ["NameEn"] = "Noura Khalid",
            ["JoinDate"] = joinDate.ToIso(),
            ["PositionRecordId"] = hrManager,
            ["GradeRecordId"] = hrm1,
        });

        await InTenantAsSystemAsync(async services =>
        {
            var employee = await services.GetRequiredService<IEmployeeService>()
                .GetByCodeAsync("f6b-hired");

            employee.Should().NotBeNull("the form created them");

            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var held = await services.GetRequiredService<IEmployeeAssignmentService>()
                .GetEffectiveAsync(employee!.EmployeeId, axis, joinDate);

            held.Should().NotBeNull("they hold the post from the day they joined");
            held!.RecordId.Should().Be(hrManager);
            held.GradeRecordId.Should().Be(hrm1);

            // THE JOIN DATE, not today. A person hired in March holds the post from March, and a
            // write that quietly started it today would misdate every back-dated hire.
            held.Range.From.Should().Be(joinDate);

            // And nothing on the day before, because the row starts when they joined.
            (await services.GetRequiredService<IEmployeeAssignmentService>()
                .GetEffectiveAsync(employee.EmployeeId, axis, joinDate.AddDays(-1)))
                .Should().BeNull();
        });
    }

    /// <summary>
    /// The employee page shows the block read-only, with the dated forms a link away.
    /// </summary>
    /// <remarks>
    /// Design note §2.1, stated as a test because it is the rule most likely to be "fixed" by
    /// somebody adding a position drop-down to the editor: a display driver's <c>UpdateAsync</c> has
    /// nowhere to put a required effective date, so the editor shows and links rather than writes.
    /// </remarks>
    [Fact]
    public async Task TheEmployeePageShowsTheBlockReadOnlyAndLinksToTheDatedForms()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-page", "Hana Yusuf");

        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                employeeId,
                axis,
                await RecordIdAsync(services, "zenith-pos-hr-manager"),
                Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-hrm-2")))
                .Succeeded.Should().BeTrue();
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        page.Should().Contain("employee-organisation", "the block is on the record's own page");
        page.Should().Contain("HR Manager");
        page.Should().Contain("HRM-2");
        page.Should().Contain("job-organisation-chain");
        page.Should().Contain("Department:");
        page.Should().Contain("employee-placement-actions", "the dated forms are a link away");

        // READ-ONLY. No control on this page writes a placement, because none of them could date it.
        page.Should().NotContain("data-position-picker");
        page.Should().NotContain("data-grade-picker");
    }

    /// <summary>
    /// Somebody on a position-based axis who holds no position is told so, rather than shown a
    /// blank block.
    /// </summary>
    /// <remarks>
    /// ADR-0029 moves nobody, so this is the state every pre-F6 employee is in the day after
    /// upgrade. D28 C: the mixed state is made legible rather than hidden, and that sentence is the
    /// whole cost of having moved nobody.
    /// </remarks>
    [Fact]
    public async Task SomebodyWithNoPositionIsToldSoOnTheirPage()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-unplaced", "Faisal Noor");

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        page.Should().Contain("employee-organisation");
        page.Should().Contain("employee-unplaced", "said in a sentence rather than left blank");
        page.Should().Contain("employee-placement-actions");
    }

    /// <summary>
    /// A tenant with no positions keeps today's Add and Edit forms exactly: no block, no pickers.
    /// </summary>
    /// <remarks>
    /// <b>The half of D30 that protects a live tenant.</b> The decision is about an axis that
    /// <em>has</em> positions; one that has none must see no change at all. Without this, "the new
    /// block works" and "the new block is everywhere" are indistinguishable.
    /// </remarks>
    [Fact]
    public async Task ATenantWithNoPositionsKeepsTodaysCreateAndEditForms()
    {
        using var plain = new BaseTenantFixture();

        await plain.InitializeAsync();

        await ApplyToAsync(plain, "organisation-designer-demo.recipe.json");

        var create = await BaseTenantFixture.GetPageAsync(plain.Administrator, "/Admin/Employees/Create");

        create.Should().NotContain("create-job-heading", "there are no positions to ask about");
        create.Should().NotContain("data-position-picker");
        create.Should().NotContain("data-grade-picker");

        // And the form it has always been is still all there.
        create.Should().Contain("Employee code");
        create.Should().Contain("Join date");

        var employeeId = string.Empty;

        await InTenantAsSystemAsync(plain, async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "f6b-plain-edit",
                new WorkMate.Core.BilingualText("Plain Person", "Plain Person"),
                new DateOnly(2024, 1, 1),
                new EmployeeDetails(null, null, WorkMate.Records.Models.Gender.Unspecified, null));

            created.Succeeded.Should().BeTrue();
            employeeId = created.Value!.EmployeeId;
        });

        var edit = await BaseTenantFixture.GetPageAsync(
            plain.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        edit.Should().NotContain("employee-organisation", "D30 does not apply to this tenant");

        // The sections loop is NOT conditional on positions: every tenant has sections.
        edit.Should().Contain("employee-sections");

        await plain.DisposeAsync();
    }

    // ---- polish A: the line manager follows the position (D31) ---------------------------------

    /// <summary>
    /// The free A–Z line-manager picker is gone from above the block, and the control lives inside
    /// it. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>The defect, plainly.</b> The form asked twice: an any-employee picker above, and a block
    /// below that had just derived who the post answers to. The two were unrelated and the free one
    /// was the one people filled in, so the stored line manager routinely contradicted the
    /// reporting line on the same screen.
    /// </remarks>
    [Fact]
    public async Task TheFreeLineManagerPickerIsGoneAndTheControlFollowsThePosition()
    {
        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, "/Admin/Employees/Create");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        // Nothing is chosen yet, so the control says what it will do rather than offering an
        // A–Z list whose answer is about to be replaced.
        document.QuerySelector("#line-manager-waiting").Should().NotBeNull(
            "with no position chosen the control says it follows the position");

        document.QuerySelectorAll("[data-employee-picker]").Should().BeEmpty(
            "the free picker of every employee in the tenant is not on this form any more");
    }

    /// <summary>
    /// One holder of the post above is derived and shown; several are offered and only they are.
    /// Decisions D28 A and D31.
    /// </summary>
    /// <remarks>
    /// Both halves in one test, because the thing being tested is the difference: D28 A says one
    /// holder is a fact and several is not, and D31 adds that the list offered for the second case
    /// is theirs alone — where the old control offered the whole tenant.
    /// </remarks>
    [Fact]
    public async Task OneHolderIsDerivedAndSeveralAreOfferedAndOnlyThey()
    {
        var director = await RecordIdAsync("zenith-pos-dir-tech");
        var hrManager = await RecordIdAsync("zenith-pos-hr-manager");

        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            // One person in the CEO post, which Director Technology reports to.
            (await assignments.PlaceAsync(
                "f6b-lm-ceo", axis, await RecordIdAsync(services, "zenith-pos-ceo"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-ceo-1")))
                .Succeeded.Should().BeTrue();

            // And three in the Director's post, which HR Manager reports to.
            foreach (var who in ThreeDirectors)
            {
                (await assignments.PlaceAsync(
                    who, axis, director, Today,
                    gradeRecordId: await RecordIdAsync(services, "zenith-grade-dir-1")))
                    .Succeeded.Should().BeTrue();
            }
        });

        // One holder: derived, named, and posted as a hidden field rather than chosen.
        var derived = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = director,
                ["IsRefresh"] = "true",
            }));

        // Shown rather than asked — and shown ONCE, in the Reports to row itself, which names the
        // post and the person who answers for it. Polish 3: the control used to print the name
        // again underneath, so one person looked like two.
        derived.QuerySelector("#job-reports-to-one")!.TextContent
            .Should().Contain("CEO", "the post above").And.Contain("line manager");

        derived.QuerySelector("#line-manager-derived").Should().BeNull(
            "the row above has already named them");

        derived.QuerySelector("#line-manager input[name='LineManagerEmployeeId']")!
            .GetAttribute("value").Should().Be("f6b-lm-ceo",
                "the hidden field is the control; the sentence was only its label");

        // Several holders: a list of exactly those three, and nobody else in the tenant.
        var several = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = hrManager,
                ["IsRefresh"] = "true",
            }));

        var offered = several.QuerySelectorAll("select[data-line-manager-choice] option")
            .Select(option => option.GetAttribute("value"))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToList();

        offered.Should().BeEquivalentTo(
            ThreeDirectors,
            "D31: the list is the holders of the post above and nobody else — the old control "
            + "offered every employee in the tenant");
    }

    /// <summary>
    /// The top of the line and a vacant post above both mean no line manager, and say so.
    /// Decision D28 A's first two cases, through D31's control.
    /// </summary>
    [Fact]
    public async Task TheTopOfTheLineAndAVacantPostAboveAreDifferentAnswers()
    {
        // The top of the line: genuinely nobody above, which is a fact rather than a gap.
        var top = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = await RecordIdAsync("zenith-pos-ceo"),
                ["IsRefresh"] = "true",
            }));

        top.QuerySelector("#job-reports-to-top")!.TextContent.Trim()
            .Should().Be("Nobody — top of the reporting line");

        top.QuerySelector("select[data-line-manager-choice]").Should().BeNull(
            "there is nothing above to choose between");

        // A VACANT POST ABOVE IS NOT THE SAME ANSWER, and saying it was is the defect this test
        // was rewritten for. Director Technology's own post above — the CEO — is unheld on this
        // tenant, so the row names it vacant and nothing above it is held either.
        var vacant = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = await RecordIdAsync("zenith-pos-dir-tech"),
                ["IsRefresh"] = "true",
            }));

        vacant.QuerySelector("#job-reports-to-vacant")!.TextContent
            .Should().Contain("CEO").And.Contain("vacant");

        vacant.QuerySelector("#job-reports-to-top").Should().BeNull(
            "a vacant post above is not the top of the line");

        vacant.QuerySelector("#line-manager-nobody-above").Should().NotBeNull(
            "nothing above the CEO is held either, so there is nobody to act");

        // THE WORDING GUARD. Whatever else changes, a vacant post must never again be described as
        // answering to no one — that sentence is true only at the top of the line.
        vacant.Body!.TextContent.Should().NotContain(
            "answers to no one",
            "a post under a vacant one answers to whoever is above the gap, not to nobody");
    }

    /// <summary>
    /// A vacant post above is filled by the nearest held post up the line, labelled acting.
    /// D31 addendum.
    /// </summary>
    /// <remarks>
    /// <b>The reported defect, and the fix.</b> Software Engineer reports to Manager Technology,
    /// which nobody holds, and the screen said the engineer answered to no one. They answer to
    /// whoever is above the gap — the Director — which is how an organisation works while a
    /// manager's chair is empty.
    ///
    /// One holder up the chain is a fact and is shown, exactly as one holder of the post itself
    /// would be; what differs is the <em>acting</em> label naming the post that person really
    /// holds, so a manager two levels up never appears unexplained.
    /// </remarks>
    [Fact]
    public async Task AVacantPostAboveFallsBackToTheNearestHeldPostAsActing()
    {
        var engineer = await RecordIdAsync("zenith-pos-se");

        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            // Director Technology sits two levels above Software Engineer: SE → Manager Technology
            // → Director Technology. Nobody holds Manager Technology, so the Director acts.
            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                "f6b-acting-dir", axis, await RecordIdAsync(services, "zenith-pos-dir-tech"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-dir-1")))
                .Succeeded.Should().BeTrue();
        });

        var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = engineer,
                ["IsRefresh"] = "true",
            }));

        // The row still names the post above and its state — that is a fact about the post.
        page.QuerySelector("#job-reports-to-vacant")!.TextContent
            .Should().Contain("Manager Technology").And.Contain("vacant");

        // And the person acting for it is named, with the post they actually hold.
        var derived = page.QuerySelector("#line-manager-derived")!.TextContent;

        derived.Should().Contain("acting", "a manager above the gap is acting, not the post holder");
        derived.Should().Contain("Director Technology", "and the label names the post they hold");

        page.QuerySelector("#line-manager-nobody-above").Should().BeNull(
            "somebody above the gap does hold a post");

        page.QuerySelector("input[name='LineManagerEmployeeId']")!.GetAttribute("value")
            .Should().Be("f6b-acting-dir", "the acting manager is what the form would save");

        page.Body!.TextContent.Should().NotContain("answers to no one");
    }

    /// <summary>
    /// Several holders of the acting post are offered, and only they. D31 addendum.
    /// </summary>
    /// <remarks>
    /// D28 A's refusal to pick survives the walk up the line: with three people holding the post
    /// above the gap, nothing chooses between them and the operator does — from those three.
    /// </remarks>
    [Fact]
    public async Task SeveralHoldersOfTheActingPostAreOfferedAndOnlyThey()
    {
        var engineer = await RecordIdAsync("zenith-pos-se");

        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var director = await RecordIdAsync(services, "zenith-pos-dir-tech");
            var grade = await RecordIdAsync(services, "zenith-grade-dir-1");

            foreach (var who in ThreeDirectors)
            {
                (await assignments.PlaceAsync(who, axis, director, Today, gradeRecordId: grade))
                    .Succeeded.Should().BeTrue();
            }
        });

        var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = engineer,
                ["IsRefresh"] = "true",
            }));

        page.QuerySelectorAll("select[data-line-manager-choice] option")
            .Select(option => option.GetAttribute("value"))
            .Where(value => !string.IsNullOrEmpty(value))
            .Should().BeEquivalentTo(
                ThreeDirectors,
                "the holders of the acting post and nobody else — D28 A's refusal to pick survives "
                + "the walk up the line");

        page.Body!.TextContent.Should().Contain(
            "acting", "and the hint says they are acting for the vacant post");
    }

    /// <summary>
    /// A chain of empty posts all the way to the top really does leave nobody. D31 addendum.
    /// </summary>
    /// <remarks>
    /// The third acting case, and the one that keeps the other two honest: the walk up the line
    /// has to be able to find nothing. It says the posts are unfilled — which somebody can act on
    /// — rather than that the employee answers to no one, which would be the original defect
    /// wearing a different sentence.
    /// </remarks>
    [Fact]
    public async Task WhenNoPostAboveIsHeldAtAllTheRowSaysSoWithoutClaimingNobodyIsAbove()
    {
        // Nobody holds anything on this tenant's reporting line yet, so Software Engineer's walk
        // goes Manager Technology → Director Technology → CEO and finds nobody.
        var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = await RecordIdAsync("zenith-pos-se"),
                ["IsRefresh"] = "true",
            }));

        page.QuerySelector("#job-reports-to-vacant")!.TextContent
            .Should().Contain("Manager Technology").And.Contain("vacant");

        page.QuerySelector("#line-manager-nobody-above")!.TextContent.Trim()
            .Should().Be("No one above holds a post yet");

        page.QuerySelector("#line-manager-derived").Should().BeNull("nobody is acting");
        page.QuerySelector("select[data-line-manager-choice]").Should().BeNull("and none to choose");

        page.Body!.TextContent.Should().NotContain(
            "answers to no one",
            "the posts exist and are empty, which is not the same as having none above");
    }

    /// <summary>
    /// The Placement form shows the same single row and the same acting answer. D31 addendum.
    /// </summary>
    [Fact]
    public async Task ThePlacementFormShowsTheSameRowAndTheSameActingAnswer()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-place-row", "Nabil Aziz");
        var structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            structureId = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                "f6b-place-dir", structureId,
                await RecordIdAsync(services, "zenith-pos-dir-tech"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-dir-1")))
                .Succeeded.Should().BeTrue();
        });

        var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await BaseTenantFixture.PostFormAsync(
                _tenant.Administrator,
                $"/Admin/Employees/Placement/Place/{employeeId}",
                new Dictionary<string, string>
                {
                    ["EmployeeId"] = employeeId,
                    ["StructureId"] = structureId,
                    ["EffectiveFrom"] = Today.ToIso(),
                    ["RecordId"] = await RecordIdAsync("zenith-pos-se"),
                    ["IsRefresh"] = "true",
                }));

        page.QuerySelectorAll("#job-organisation-reports-to").Should().HaveCount(
            1, "one row here too, not a separate Line manager field");

        page.QuerySelector("#job-reports-to-vacant")!.TextContent
            .Should().Contain("Manager Technology").And.Contain("vacant");

        page.QuerySelector("#line-manager-derived")!.TextContent
            .Should().Contain("acting").And.Contain("Director Technology");

        page.Body!.TextContent.Should().NotContain("answers to no one");
    }

    /// <summary>
    /// The employee page and the Placement screen show the same single row, read-only.
    /// D31 addendum.
    /// </summary>
    /// <remarks>
    /// <b>Four screens, one partial, one sentence.</b> The page used to carry a <em>Line
    /// manager</em> row of its own, which said "Nobody — this post answers to no one on the
    /// reporting line" whenever nothing was stored — false for every post under a vacant one, and
    /// a second place for the wording to drift.
    /// </remarks>
    [Fact]
    public async Task TheEmployeePageAndThePlacementScreenShowTheSameSingleRow()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-one-row", "Zahra Omar");

        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            // Somebody in the Director's post, so the engineer below the vacant Manager Technology
            // has an acting manager to show.
            (await assignments.PlaceAsync(
                "f6b-one-row-dir", axis, await RecordIdAsync(services, "zenith-pos-dir-tech"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-dir-1")))
                .Succeeded.Should().BeTrue();

            (await assignments.PlaceAsync(
                employeeId, axis, await RecordIdAsync(services, "zenith-pos-se"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-se-1")))
                .Succeeded.Should().BeTrue();
        });

        foreach (var screen in new[]
        {
            $"/Admin/Employees/Edit/{employeeId}",
            $"/Admin/Employees/Placement/Index/{employeeId}",
        })
        {
            var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
                await BaseTenantFixture.GetPageAsync(_tenant.Administrator, screen));

            // ONE row, carrying the post above and its state.
            page.QuerySelectorAll("#job-organisation-reports-to").Should().HaveCount(
                1, "Reports to and Line manager are one row on {0}", screen);

            page.QuerySelector("#job-reports-to-vacant")!.TextContent
                .Should().Contain("Manager Technology").And.Contain("vacant");

            // Read-only: no control to change it here.
            page.QuerySelector("select[data-line-manager-choice]").Should().BeNull(
                "{0} states what is true rather than asking", screen);

            page.QuerySelector("#line-manager-change").Should().BeNull(
                "and the override link belongs on the dated forms");

            page.Body!.TextContent.Should().NotContain(
                "answers to no one",
                "a post under a vacant one answers to whoever is above the gap, on {0} too", screen);
        }
    }

    /// <summary>
    /// Choosing a different position replaces the line manager rather than keeping the last one.
    /// Decision D31 — "follows the position" is the whole claim.
    /// </summary>
    /// <remarks>
    /// The defect this guards is subtle and would survive every other test here: the old code
    /// defaulted the line manager only into an <em>empty</em> field, so changing the position kept
    /// the previous position's manager and the form quietly asserted a reporting line that no
    /// longer existed.
    /// </remarks>
    [Fact]
    public async Task ChangingThePositionReplacesTheDerivedLineManager()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                "f6b-lm-follow-ceo", axis, await RecordIdAsync(services, "zenith-pos-ceo"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-ceo-1")))
                .Succeeded.Should().BeTrue();
        });

        // Director Technology reports to the CEO, who is held: the manager is derived.
        var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = await RecordIdAsync("zenith-pos-dir-tech"),
                ["IsRefresh"] = "true",
            }));

        page.QuerySelector("#line-manager input[name='LineManagerEmployeeId']")!
            .GetAttribute("value").Should().Be("f6b-lm-follow-ceo");

        // Now switch to the CEO post, which is the top of the line — carrying the previous answer
        // here would assert a reporting line the chart does not have.
        var switched = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = await RecordIdAsync("zenith-pos-ceo"),
                ["LineManagerEmployeeId"] = "f6b-lm-follow-ceo",
                ["IsRefresh"] = "true",
            }));

        switched.QuerySelector("#job-reports-to-top").Should().NotBeNull(
            "the CEO is the top of the line, whatever the last position implied");

        switched.QuerySelectorAll("input[name='LineManagerEmployeeId'][value='f6b-lm-follow-ceo']")
            .Should().BeEmpty("the previous position's manager is not carried across");
    }

    /// <summary>
    /// A line manager outside the reporting line is allowed, and is labelled as an exception on the
    /// form and on the employee's page. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>The escape hatch is the reason the rule can be strict.</b> A dotted line, a matrix and a
    /// secondment are all real, and a product that refused them would be refusing to record what
    /// organisations do — which is the same argument D26 F makes about a full position. What makes
    /// it safe is that it is visible: an override that looked like the ordinary case would be a
    /// silent contradiction of the block above it.
    /// </remarks>
    [Fact]
    public async Task AnOverrideIsAllowedAndIsLabelledOnTheFormAndOnThePage()
    {
        var outsider = await GivenAnEmployeeAsync("f6b-lm-outsider", "Rami Habib");
        var director = await RecordIdAsync("zenith-pos-dir-tech");

        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                "f6b-lm-override-ceo", axis, await RecordIdAsync(services, "zenith-pos-ceo"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-ceo-1")))
                .Succeeded.Should().BeTrue();
        });

        // Asking for the override swaps the control for the any-employee picker.
        var form = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = director,
                ["LineManagerOverride"] = "true",
                ["LineManagerEmployeeId"] = outsider,
                ["IsRefresh"] = "true",
            }));

        form.QuerySelector("#line-manager-differs").Should().NotBeNull(
            "somebody the reporting line does not imply is flagged on the form");

        // And it saves, and the employee's own page says the same thing.
        await PostToCreateAsync(new Dictionary<string, string>
        {
            ["Code"] = "f6b-lm-dotted",
            ["NameEn"] = "Dotted Line",
            ["JoinDate"] = Today.ToIso(),
            ["PositionRecordId"] = director,
            ["GradeRecordId"] = await RecordIdAsync("zenith-grade-dir-1"),
            ["LineManagerOverride"] = "true",
            ["LineManagerEmployeeId"] = outsider,
        });

        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
            employeeId = (await services.GetRequiredService<IEmployeeService>()
                .GetByCodeAsync("f6b-lm-dotted"))!.EmployeeId);

        var record = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator, $"/Admin/Employees/Edit/{employeeId}"));

        // The ONE "Reports to" row on the page names the person on the record — not the holder
        // the line implies, which would make the page contradict the record it is drawn from.
        // Its own element since polish 3: an override is by definition NOT the person the row
        // above names, so it cannot share the mode of the case where it is.
        record.QuerySelector("#line-manager-override-name")!.TextContent
            .Should().Contain("Rami Habib");

        record.QuerySelector("#line-manager-differs").Should().NotBeNull(
            "and the page says it departs from the reporting line, so it never reads as a "
            + "contradiction of the row it sits in");
    }

    /// <summary>
    /// A tenant with no positions keeps today's free line-manager field. Decisions D30 and D31.
    /// </summary>
    [Fact]
    public async Task ATenantWithNoPositionsKeepsTheFreeLineManagerField()
    {
        using var plain = new BaseTenantFixture();

        await plain.InitializeAsync();

        await ApplyToAsync(plain, "organisation-designer-demo.recipe.json");

        var create = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(plain.Administrator, "/Admin/Employees/Create"));

        create.QuerySelectorAll("[data-employee-picker]").Should().NotBeEmpty(
            "there is no reporting line to follow, so the free picker is not a second answer to a "
            + "question already asked — it is the only answer there is");

        create.QuerySelector("#line-manager").Should().BeNull("and the derived control is absent");

        await plain.DisposeAsync();
    }

    // ---- polish 3 A: the Reports to row names the person once ----------------------------------

    /// <summary>
    /// With one holder of the post above, that person is named exactly once in the <em>Reports
    /// to</em> row — on all four screens that render it.
    /// </summary>
    /// <remarks>
    /// <b>The defect.</b> The row reads "Director Technology — Ali Tahir (line manager)": the post,
    /// and the person who answers for it, which is the whole fact. The line-manager control then
    /// printed "Ali Tahir" again underneath it, so one person looked like two and the screen read
    /// as though it had derived the same answer twice from two different places.
    ///
    /// <b>Why the count and not the absence of a marker.</b> Asserting that some element is gone
    /// would pass the day somebody reintroduced the name in a different element. The claim is about
    /// what a reader sees, so the test counts what a reader sees.
    ///
    /// <b>All four screens, from one partial, deliberately.</b> That is the arrangement D31's
    /// addendum put in place precisely so a wording fix lands in four places at once — and a test
    /// that checked one of them would not notice if it stopped being true of the other three.
    /// </remarks>
    [Fact]
    public async Task WithOneHolderThePersonIsNamedExactlyOnceInTheReportsToRow()
    {
        var holder = await GivenAnEmployeeAsync("f6b-once-ceo", "Ali Tahir");
        var director = await RecordIdAsync("zenith-pos-dir-tech");
        var axis = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                holder, axis, await RecordIdAsync(services, "zenith-pos-ceo"), Today,
                gradeRecordId: await RecordIdAsync(services, "zenith-grade-ceo-1")))
                .Succeeded.Should().BeTrue();
        });

        // 1. Add employee, with the position chosen.
        var create = await PostToCreateAsync(new Dictionary<string, string>
        {
            ["JoinDate"] = Today.ToIso(),
            ["PositionRecordId"] = director,
            ["IsRefresh"] = "true",
        });

        ReportsToRowIn(create).Should().Be(1, "on the Add employee form");

        // And it still posts what it would save, which is what the sentence used to double as.
        new AngleSharp.Html.Parser.HtmlParser().ParseDocument(create)
            .QuerySelector("#line-manager input[name='LineManagerEmployeeId']")!
            .GetAttribute("value").Should().Be(holder,
                "the hidden field is the control; the sentence was only its label");

        // Save, so the three screens about an existing employee have one to be about.
        await PostToCreateAsync(new Dictionary<string, string>
        {
            ["Code"] = "f6b-once-se",
            ["NameEn"] = "Named Once",
            ["NameAr"] = "Named Once",
            ["JoinDate"] = Today.ToIso(),
            ["PositionRecordId"] = director,
            ["GradeRecordId"] = await RecordIdAsync("zenith-grade-dir-1"),
        });

        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
            employeeId = (await services.GetRequiredService<IEmployeeService>()
                .GetByCodeAsync("f6b-once-se"))!.EmployeeId);

        // 2. The employee page. 3. The Placement screen. 4. Change position, with the post chosen.
        var screens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["the employee page"] = $"/Admin/Employees/Edit/{employeeId}",
            ["the Placement screen"] = $"/Admin/Employees/Placement/Index/{employeeId}",
            ["the Change position form"] =
                $"/Admin/Employees/Placement/Place/{employeeId}?structureId={axis}&recordId={director}",
        };

        foreach (var (screen, url) in screens)
        {
            ReportsToRowIn(await BaseTenantFixture.GetPageAsync(_tenant.Administrator, url))
                .Should().Be(1, "on {0}", screen);
        }

        static int ReportsToRowIn(string html)
        {
            var row = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html)
                .QuerySelector("#job-organisation-reports-to");

            row.Should().NotBeNull("the one Reports to row is on this screen");

            // The whole row, control included — the duplicate was one element below the other,
            // and anything that counted only the row's own text would have missed it.
            var text = row!.TextContent;
            var count = 0;

            for (var at = text.IndexOf("Ali Tahir", StringComparison.Ordinal);
                at >= 0;
                at = text.IndexOf("Ali Tahir", at + 1, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }

    // ---- polish 3 B: a reporting line and a grade ladder are not places ------------------------

    /// <summary>
    /// A placement on the reporting line or on the grade ladder is refused, in the same words,
    /// through the service, the screen and the recipe step. Decision D30.
    /// </summary>
    /// <remarks>
    /// <b>Three callers and one rule, which is the whole claim.</b> CLAUDE.md rule 5: the screen,
    /// the <c>employee-assignments</c> recipe step and any later import all reach
    /// <c>ReallocateAsync</c>, so stating the rule once in the validator is what makes all three
    /// refuse it identically. A designer that merely stopped offering the action would leave the
    /// old shape reachable by every other route — which is exactly how it was reachable here, since
    /// the designer DID offer it.
    ///
    /// <b>Nothing already written is touched.</b> ADR-0029: this runs on the write.
    /// </remarks>
    [Fact]
    public async Task APlacementOnTheReportingLineOrTheGradeLadderIsRefusedEverywhere()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-noplace", "Wrong Chart");

        string reporting = null!, grades = null!, ceo = null!, grade = null!;

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();

            reporting = (await structures.GetByCodeAsync("zenith-reporting"))!.StructureId;
            grades = (await structures.GetByCodeAsync("zenith-grades"))!.StructureId;
            ceo = await RecordIdAsync(services, "zenith-pos-ceo");
            grade = await RecordIdAsync(services, "zenith-grade-ceo-1");
        });

        // 1. The service, which is the path all three callers share.
        await InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var onTheLine = await assignments.PlaceAsync(employeeId, reporting, ceo, Today);

            onTheLine.Succeeded.Should().BeFalse(
                "a reporting line records what each post answers to, not where anybody sits");

            string.Join(" ", onTheLine.Errors.Select(error => error.Message.Value))
                .Should().Contain("reporting line");

            var onTheLadder = await assignments.PlaceAsync(employeeId, grades, grade, Today);

            onTheLadder.Succeeded.Should().BeFalse(
                "and a grade is carried on the placement, not somewhere a person is put");

            string.Join(" ", onTheLadder.Errors.Select(error => error.Message.Value))
                .Should().Contain("grade ladder");
        });

        // 2. The screen, posted directly — the picker does not offer these axes, and a rule that
        // lived only in the picker would be no rule at all.
        var posted = await BaseTenantFixture.PostFormAsync(
            _tenant.Administrator,
            "/Admin/Employees/Placement/Place",
            new Dictionary<string, string>
            {
                ["EmployeeId"] = employeeId,
                ["StructureId"] = reporting,
                ["RecordId"] = ceo,
                ["EffectiveFrom"] = Today.ToIso(),
                ["AllocationPercent"] = "100",
                ["IsPrimary"] = "true",
            });

        posted.Should().Contain("reporting line", "the form says why rather than silently failing");

        // 3. The recipe step, which is also the import path.
        var recipe = await ApplyInlineAsync(
            "f6b-noplace-recipe",
            $$"""
            {
              "steps": [
                {
                  "name": "employee-assignments",
                  "assignments": [
                    {
                      "employeeCode": "f6b-noplace",
                      "structureCode": "zenith-reporting",
                      "changes": [
                        {
                          "effectiveFrom": "{{Today.ToIso()}}",
                          "split": [ { "recordCode": "zenith-pos-ceo", "allocationPercent": 100, "isPrimary": true } ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        recipe.Should().NotBeNull("the recipe step refuses rather than importing it");
        recipe!.Should().Contain("reporting line");

        // AND NOTHING WAS WRITTEN, by any of the three.
        await InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            (await assignments.GetAllAxesAsync(employeeId, Today))
                .Should().BeEmpty("a refusal that still wrote the row would be worse than none");
        });
    }

    // ---- item 9: the fixed core first, then a LOOP over this tenant's sections ------------------

    /// <summary>
    /// The employee page renders its fixed core first and everything else from a section loop read
    /// off the tenant's content definition.
    /// </summary>
    /// <remarks>
    /// <b>Design note §9.2, Rule 2, and the reason it is a test rather than a convention.</b> The
    /// six sections WorkMate ships are what a tenant has <em>today</em>; the form designer publishes
    /// more in B3, and a page that named six in markup would need editing — and the editing would be
    /// inside the fixed core, which is exactly what Rule 1 exists to keep nobody out of. So the
    /// assertion is on the ORDER (core above the loop) and on the SOURCE (the definition, which is
    /// why the loop finds the stereotype-carrying bags and nothing else).
    /// </remarks>
    [Fact]
    public async Task TheEmployeePageRendersTheFixedCoreFirstAndThenALoopOverThisTenantsSections()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-sections", "Layla Omar");

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        var sections = document.QuerySelectorAll("#employee-sections .employee-section")
            .Select(item => item.GetAttribute("data-section"))
            .ToList();

        // What this tenant HAS, read from its definition — which today is the six the product
        // ships, in the order the definition states them.
        sections.Should().Equal(
            ["JobDetails", "Documents", "BankDetails", "Qualifications", "Dependants", "Contacts"]);

        // THE ORDER, which is the half that stops the loop being moved above the core by accident.
        var identity = page.IndexOf("employee-edit", StringComparison.Ordinal);
        var organisation = page.IndexOf("employee-organisation", StringComparison.Ordinal);
        var loop = page.IndexOf("employee-sections", StringComparison.Ordinal);

        identity.Should().BeGreaterThan(-1);
        organisation.Should().BeGreaterThan(identity, "the derived block follows identity");
        loop.Should().BeGreaterThan(organisation, "and the designed sections follow the fixed core");
    }

    /// <summary>
    /// The loop reads the content definition, so a section added to the Employee type appears
    /// without a line of view code changing.
    /// </summary>
    /// <remarks>
    /// <b>B3's future, exercised now.</b> This is the claim §9.2 Rule 2 actually makes, and the only
    /// way to hold it is to add a seventh section and look. A bag of something that is <em>not</em>
    /// an employee section is added alongside it and must be ignored: the stereotype is the test,
    /// never the name, which is what stops this screen rendering somebody else's part as a section.
    /// </remarks>
    [Fact]
    public async Task ASeventhSectionAddedToTheTypeAppearsInTheLoopAndANonSectionBagDoesNot()
    {
        var employeeId = await GivenAnEmployeeAsync("f6b-seventh", "Mariam Adel");

        await InTenantAsSystemAsync(async services =>
        {
            var definitions = services
                .GetRequiredService<OrchardCore.ContentManagement.Metadata.IContentDefinitionManager>();

            // A section the way B3 will make one: an item type carrying the stereotype, and a bag
            // on the Employee type that contains it.
            await definitions.AlterTypeDefinitionAsync("EmployeeVisaDetail", type => type
                .WithDisplayName("Visa detail")
                .WithSettings(new OrchardCore.ContentManagement.Metadata.Settings.ContentTypeSettings
                {
                    Creatable = false,
                    Listable = false,
                    Stereotype = WorkMate.Records.Models.EmployeeSections.Stereotype,
                }));

            // And one that is NOT a section: a bag of something with no stereotype at all.
            await definitions.AlterTypeDefinitionAsync("EmployeeScratchNote", type => type
                .WithDisplayName("Scratch note")
                .WithSettings(new OrchardCore.ContentManagement.Metadata.Settings.ContentTypeSettings
                {
                    Creatable = false,
                    Listable = false,
                }));

            await definitions.AlterTypeDefinitionAsync("Employee", type => type
                .WithPart("VisaDetails", nameof(OrchardCore.Flows.Models.BagPart), bag => bag
                    .WithSettings(new OrchardCore.Flows.Models.BagPartSettings
                    {
                        ContainedContentTypes = ["EmployeeVisaDetail"],
                    }))
                .WithPart("ScratchNotes", nameof(OrchardCore.Flows.Models.BagPart), bag => bag
                    .WithSettings(new OrchardCore.Flows.Models.BagPartSettings
                    {
                        ContainedContentTypes = ["EmployeeScratchNote"],
                    })));
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        var sections = document.QuerySelectorAll("#employee-sections .employee-section")
            .Select(item => item.GetAttribute("data-section"))
            .ToList();

        sections.Should().Contain("VisaDetails", "a section the tenant added appears by itself");

        sections.Should().NotContain(
            "ScratchNotes",
            "the stereotype is the test — a bag of something else is not this screen's to render");

        // And the heading reads as words rather than as an identifier.
        page.Should().Contain("Visa details");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Posts the Add-employee form, with the antiforgery token the screen carries.</summary>
    private async Task<string> PostToCreateAsync(IDictionary<string, string> fields) =>
        await BaseTenantFixture.PostFormAsync(
            _tenant.Administrator, "/Admin/Employees/Create", fields);

    private async Task<string> RecordIdAsync(string code)
    {
        var recordId = string.Empty;

        await InTenantAsSystemAsync(async services => recordId = await RecordIdAsync(services, code));

        return recordId;
    }

    private static async Task<string> RecordIdAsync(IServiceProvider services, string code) =>
        (await services.GetRequiredService<IDimensionService>().GetByCodeAsync(code))!.RecordId;

    private async Task<string> GivenAnEmployeeAsync(string code, string name)
    {
        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                code,
                new WorkMate.Core.BilingualText(name, name),
                new DateOnly(2024, 1, 1),
                new EmployeeDetails(null, null, WorkMate.Records.Models.Gender.Unspecified, null));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            employeeId = created.Value!.EmployeeId;
        });

        return employeeId;
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        InTenantAsSystemAsync(_tenant, work);

    private static Task InTenantAsSystemAsync(
        BaseTenantFixture tenant,
        Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("F6b test"))
            {
                await work(services);
            }
        });

    private Task ApplyAsync(string fileName) => ApplyToAsync(_tenant, fileName);

    /// <summary>
    /// Runs a recipe written for this test, and returns what it refused — or null if it applied.
    /// </summary>
    /// <remarks>
    /// Written to a temp folder rather than shipped under <c>/recipes</c>, because this one is
    /// <em>meant</em> to fail: the pipeline applies every shipped recipe to a fresh tenant and a
    /// recipe that cannot apply fails the build, which is exactly the guard that should stay.
    ///
    /// The step itself is the real one, reached through the real executor, so this exercises the
    /// import path and not a re-implementation of it.
    /// </remarks>
    private async Task<string?> ApplyInlineAsync(string name, string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"wm-recipe-{Guid.NewGuid():n}");

        Directory.CreateDirectory(folder);

        var fileName = $"{name}.recipe.json";

        await File.WriteAllTextAsync(Path.Combine(folder, fileName), json);

        try
        {
            string? refusal = null;

            await _tenant.InTenantAsync(async services =>
            {
                var fileProvider = new PhysicalFileProvider(folder);

                var descriptor = new RecipeDescriptor
                {
                    Name = name,
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                    RequireNewScope = false,
                };

                try
                {
                    await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                        Guid.NewGuid().ToString("n"),
                        descriptor,
                        new Dictionary<string, object>(),
                        CancellationToken.None);
                }
                catch (Exception failure)
                {
                    // The step reports through RecipeStepFailures.Throw, and the executor wraps
                    // it — so the message worth asserting on can be at either level.
                    refusal = failure.ToString();
                }
            });

            return refusal;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

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
