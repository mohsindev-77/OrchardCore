using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// D31 addendum 2: a derived line manager is never written down, so it cannot go stale.
/// </summary>
/// <remarks>
/// <b>The defect these were written for.</b> D31 and its first addendum derived the line manager
/// correctly and then saved the answer — on every create and every placement, in every case. A
/// derivation that is written down is a derivation that has stopped happening: the holder of the
/// post above leaves, and every employee under that post still names them, for ever, on every
/// screen. Nothing announces it.
///
/// So the claims under test are about what happens with <b>nobody editing anything</b>. Each test
/// sets the chart up, reads a screen, changes the chart through the dimension engine alone, and
/// reads the same screen again. A product that stored the answer passes the first read and fails
/// the second.
/// </remarks>
public sealed class LineManagersDoNotGoStaleTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>
    /// Dates relative to the real clock, with margins.
    /// </summary>
    /// <remarks>
    /// The screens read "today" through <c>IAuthorisationService.TodayAsync</c>, which is the
    /// tenant's own timezone — Asia/Bahrain on this fixture — and so may be a day either side of
    /// this process's UTC date. Every date here is therefore several days clear of the boundary, so
    /// no test turns on which of the two days the server thinks it is.
    /// </remarks>
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly DateOnly LongAgo = Today.AddDays(-60);

    /// <summary>The last day of the outgoing holder, well before any reading of "today".</summary>
    private static readonly DateOnly HandoverEve = Today.AddDays(-4);

    /// <summary>The first day of the incoming holder. Already true whatever today turns out to be.</summary>
    private static readonly DateOnly Handover = Today.AddDays(-3);

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- the holder of the post above changes --------------------------------------------------

    /// <summary>
    /// When the post above changes hands, the line manager follows — with nobody editing the
    /// employee, on the form and on the employee page alike.
    /// </summary>
    /// <remarks>
    /// <b>The whole addendum in one test.</b> The sole holder of the post above is a fact the chart
    /// already states, so the record stores nothing — and because it stores nothing there is
    /// nothing to go stale. The assertion on the empty field is not an implementation detail: it is
    /// the only thing that makes the second half possible.
    /// </remarks>
    [Fact]
    public async Task WhenThePostAboveChangesHandsTheLineManagerFollowsWithNoEdit()
    {
        var first = await GivenAnEmployeeAsync("lm-first", "Mona Saleh");
        var second = await GivenAnEmployeeAsync("lm-second", "Khalid Aziz");

        await PlaceAsync(first, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", LongAgo);

        var engineer = await GivenAnEmployeeThroughTheFormAsync(
            "lm-stale-se", "Dana Fahd", "zenith-pos-se", "zenith-grade-se-1");

        // NOTHING IS STORED, because one holder of the post above is something the chart says.
        (await StoredLineManagerAsync(engineer)).EmployeeId.Should().BeNullOrEmpty(
            "a sole holder is derived on every read — storing it is what froze it before");

        (await EmployeePageAsync(engineer)).Should().Contain("Mona Saleh");
        (await PlacementPageAsync(engineer)).Should().Contain("Mona Saleh");

        // The chart changes, and NOBODY TOUCHES THE EMPLOYEE.
        await EndOnTheOrgAxisAsync(first, HandoverEve);
        await PlaceAsync(second, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", Handover);

        var page = await EmployeePageAsync(engineer);

        page.Should().Contain("Khalid Aziz", "the post above has a new holder and the line follows");
        page.Should().NotContain("Mona Saleh", "and the person who left it is not still named");

        (await PlacementPageAsync(engineer)).Should().Contain("Khalid Aziz")
            .And.NotContain("Mona Saleh");

        // And the Add-employee form, which is where the old code wrote the value in the first place.
        var form = Parse(await PostToCreateAsync(new Dictionary<string, string>
        {
            ["JoinDate"] = Today.ToIso(),
            ["PositionRecordId"] = await RecordIdAsync("zenith-pos-se"),
            ["IsRefresh"] = "true",
        }));

        // In the Reports to row itself: one holder of the post above is named there and nowhere
        // else, which is polish 3's duplicate fix.
        form.QuerySelector("#job-organisation-reports-to")!.TextContent
            .Should().Contain("Khalid Aziz");

        (await StoredLineManagerAsync(engineer)).EmployeeId.Should().BeNullOrEmpty(
            "and reading a screen never writes one down");
    }

    // ---- a vacant post above is filled ---------------------------------------------------------

    /// <summary>
    /// An acting line manager is replaced by the real one the moment the vacant post is filled.
    /// </summary>
    /// <remarks>
    /// The first addendum's walk up the line is a read, not a decision: it answers "who is standing
    /// in" and stops being asked the moment nobody needs to stand in. Saving its answer turned a
    /// temporary arrangement into a permanent fact.
    /// </remarks>
    [Fact]
    public async Task WhenAVacantPostAboveIsFilledTheActingManagerIsReplacedAutomatically()
    {
        var director = await GivenAnEmployeeAsync("lm-acting-dir", "Hana Rashid");
        var manager = await GivenAnEmployeeAsync("lm-real-mgr", "Omar Nasser");

        // Manager Technology is vacant, so the Director above the gap acts.
        await PlaceAsync(director, "zenith-pos-dir-tech", "zenith-grade-dir-1", LongAgo);

        var engineer = await GivenAnEmployeeThroughTheFormAsync(
            "lm-acting-se", "Yara Qasim", "zenith-pos-se", "zenith-grade-se-1");

        (await StoredLineManagerAsync(engineer)).EmployeeId.Should().BeNullOrEmpty(
            "one acting holder is derived too — an acting manager is not a fact to write down");

        var whileVacant = await EmployeePageAsync(engineer);

        whileVacant.Should().Contain("Hana Rashid");
        whileVacant.Should().Contain("acting", "a manager above the gap is never unexplained");

        // The chair is filled, and nobody touches the employee.
        await PlaceAsync(manager, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", Handover);

        var page = Parse(await EmployeePageAsync(engineer));

        page.QuerySelector("#job-organisation-reports-to")!.TextContent
            .Should().Contain("Omar Nasser", "the post above is held again, so its holder answers")
            .And.NotContain("acting", "and nobody is standing in any more");

        page.Body!.TextContent.Should().NotContain("Hana Rashid");

        page.QuerySelector("#job-reports-to-vacant").Should().BeNull(
            "the post above is not vacant any more either");
    }

    /// <summary>
    /// A chosen acting manager is stopped being obeyed when the post above is filled, and is
    /// cleared on the next save.
    /// </summary>
    /// <remarks>
    /// <b>Two halves, and they are not the same claim.</b> The read stops obeying it immediately —
    /// nobody should see a stand-in named while the real holder is in post. The record is tidied on
    /// the next save, because a read must not write: a screen that cleaned up what it rendered
    /// would make every list view a write path.
    /// </remarks>
    [Fact]
    public async Task AChosenActingManagerIsDroppedOnceThePostAboveIsFilled()
    {
        var directors = await GivenThreeHoldersAsync(
            "zenith-pos-dir-tech", "zenith-grade-dir-1", "lm-ca-dir", "Acting Director");

        var manager = await GivenAnEmployeeAsync("lm-ca-mgr", "Salim Haddad");

        // Manager Technology is vacant and three Directors are above the gap: D28 A refuses to pick
        // between them, so somebody does — and that choice is stored, because nothing else states it.
        var engineer = await GivenAnEmployeeThroughTheFormAsync(
            "lm-ca-se", "Noor Jamal", "zenith-pos-se", "zenith-grade-se-1",
            lineManagerEmployeeId: directors[1]);

        var saved = await StoredLineManagerAsync(engineer);

        saved.EmployeeId.Should().Be(
            directors[1], "a pick among several is the one thing the chart cannot say");

        saved.Kind.Should().Be(
            StoredLineManagerKind.ChosenActing,
            "and the form records that it was made while the post above was vacant — nothing can "
            + "work that out afterwards, because once the post is filled a stand-in and a departed "
            + "holder both read as 'not a holder'");

        (await EmployeePageAsync(engineer)).Should().Contain("Acting Director 2");

        // The chair is filled. The acting arrangement is over.
        await PlaceAsync(manager, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", Handover);

        var page = await EmployeePageAsync(engineer);

        page.Should().Contain("Salim Haddad", "the real holder of the post above answers now");
        page.Should().NotContain("Acting Director 2", "the stand-in is not obeyed any more");

        (await StoredLineManagerAsync(engineer)).EmployeeId.Should().Be(
            directors[1], "and reading the page did not write to the record");

        // The next save is where it is tidied away.
        await PostToEditAsync(engineer, "Noor Jamal");

        (await StoredLineManagerAsync(engineer)).EmployeeId.Should().BeNullOrEmpty(
            "a choice about a situation that has ended is cleared on the next save");

        (await EmployeePageAsync(engineer)).Should().Contain("Salim Haddad");
    }

    // ---- a chosen person leaves the post the choice was made from ------------------------------

    /// <summary>
    /// When the person chosen among several leaves the post above, the screen asks again by name
    /// and never falls back silently.
    /// </summary>
    /// <remarks>
    /// <b>The alternative was worse than it looks.</b> Quietly promoting one of the remaining
    /// holders would be the product choosing a line manager nobody stated — exactly what D28 A
    /// refuses — and would do it invisibly, so the record would never be corrected. Naming the
    /// person who left, and asking, is the only answer that is not a guess.
    /// </remarks>
    [Fact]
    public async Task WhenTheChosenHolderLeavesThePostTheScreenAsksForANewLineManager()
    {
        var directors = await GivenThreeHoldersAsync(
            "zenith-pos-dir-tech", "zenith-grade-dir-1", "lm-leaver-dir", "Stale Director");

        // HR Manager reports to Director Technology, which three people hold.
        var hr = await GivenAnEmployeeThroughTheFormAsync(
            "lm-leaver-hr", "Reem Adel", "zenith-pos-hr-manager", "zenith-grade-hrm-1",
            lineManagerEmployeeId: directors[0]);

        (await EmployeePageAsync(hr)).Should().Contain("Stale Director 1");

        // The chosen person leaves the post. The other two still hold it.
        await EndOnTheOrgAxisAsync(directors[0], HandoverEve);

        var page = Parse(await EmployeePageAsync(hr));

        var asks = page.QuerySelector("#line-manager-needs-choice");

        asks.Should().NotBeNull("the stored choice has stopped being true and says so");

        asks!.TextContent.Should().Contain("Stale Director 1", "by name — the record needs attention")
            .And.Contain("Director Technology", "and names the post they no longer hold");

        page.Body!.TextContent.Should().NotContain(
            "Stale Director 2",
            "NEVER a silent fallback: picking another holder would invent a line manager nobody "
            + "stated, which is what D28 A refuses");

        (await StoredLineManagerAsync(hr)).EmployeeId.Should().Be(
            directors[0], "and the stale value is kept, because the question is about that person");

        // And the question comes with the holders who are left, so it can be answered.
        var answer = await LineManagerAnswerAsync(hr);

        answer.How.Should().Be(LineManagerHow.NeedsChoice);

        answer.Candidates.Select(holder => holder.EmployeeId)
            .Should().BeEquivalentTo(
                [directors[1], directors[2]],
                "the picker offers the current holders of the post above and nobody else");
    }

    // ---- an override survives all of it --------------------------------------------------------

    /// <summary>
    /// A deliberate override outlives the post above changing hands, emptying and being filled
    /// again — and stays labelled throughout.
    /// </summary>
    /// <remarks>
    /// A dotted line is a statement about people, not about the chart, so nothing the chart does to
    /// itself is evidence against it. This is the test that keeps the staleness rules from becoming
    /// a machine that quietly deletes the one thing a human stated on purpose.
    /// </remarks>
    [Fact]
    public async Task AnOverrideSurvivesThePostAboveChangingHandsEmptyingAndBeingFilled()
    {
        var outsider = await GivenAnEmployeeAsync("lm-outsider", "Rami Habib");
        var first = await GivenAnEmployeeAsync("lm-ov-first", "First Manager");
        var second = await GivenAnEmployeeAsync("lm-ov-second", "Second Manager");

        await PlaceAsync(first, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", LongAgo);

        var engineer = await GivenAnEmployeeThroughTheFormAsync(
            "lm-ov-se", "Dotted Line", "zenith-pos-se", "zenith-grade-se-1",
            lineManagerEmployeeId: outsider,
            isOverride: true);

        await ShouldStillBeTheOverrideAsync(engineer, outsider, "as saved");

        // The post above changes hands.
        await EndOnTheOrgAxisAsync(first, HandoverEve);
        await PlaceAsync(second, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", Handover);

        await ShouldStillBeTheOverrideAsync(engineer, outsider, "after the post above changed hands");

        // And empties.
        await EndOnTheOrgAxisAsync(second, Today.AddDays(-2));

        await ShouldStillBeTheOverrideAsync(engineer, outsider, "after the post above emptied");

        // And is filled again, by somebody else.
        await PlaceAsync(
            await GivenAnEmployeeAsync("lm-ov-third", "Third Manager"),
            "zenith-pos-mgr-tech", "zenith-grade-mgr-1", Today.AddDays(-1));

        await ShouldStillBeTheOverrideAsync(engineer, outsider, "after it was filled again");

        // An ordinary save of the identity form does not quietly drop it either — the form does not
        // offer the control at all on a tenant with positions, so an absent field is not an answer.
        await PostToEditAsync(engineer, "Dotted Line");

        await ShouldStillBeTheOverrideAsync(
            engineer, outsider, "after an ordinary save of the record");
    }

    private async Task ShouldStillBeTheOverrideAsync(
        string employeeId,
        string outsider,
        string because)
    {
        var page = Parse(await EmployeePageAsync(employeeId));

        page.Body!.TextContent.Should().Contain("Rami Habib", "the override stands {0}", because);

        page.QuerySelector("#line-manager-differs").Should().NotBeNull(
            "and is still labelled as departing from the reporting line {0}", because);

        var stored = await StoredLineManagerAsync(employeeId);

        stored.EmployeeId.Should().Be(outsider, "the named person survives {0}", because);

        stored.Kind.Should().Be(
            StoredLineManagerKind.Override, "and so does what kind of answer it is {0}", because);
    }

    // ---- an employee with no position ----------------------------------------------------------

    /// <summary>
    /// An employee who holds no position keeps their stored line manager exactly, with no label and
    /// nothing derived.
    /// </summary>
    /// <remarks>
    /// Every pre-F6 record and every no-positions tenant is in this state — ADR-0029's mid-migration
    /// shape, which the engine is required to leave alone. There is no reporting line for the stored
    /// person to be an exception to, so calling them an exception would be inventing a rule they
    /// break.
    /// </remarks>
    [Fact]
    public async Task AnEmployeeWithNoPositionKeepsTheStoredValueWithNoLabel()
    {
        var manager = await GivenAnEmployeeAsync("lm-np-mgr", "Legacy Manager");

        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "lm-np-employee",
                new BilingualText("Old Record", "Old Record"),
                LongAgo,
                new EmployeeDetails(LineManagerEmployeeId: manager));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            employeeId = created.Value!.EmployeeId;
        });

        var answer = await LineManagerAnswerAsync(employeeId);

        answer.How.Should().Be(
            LineManagerHow.Stored,
            "no position means no reporting line, so there is nothing to derive and nothing to "
            + "be an exception to");

        answer.EmployeeId.Should().Be(manager, "and the stored value is the answer, untouched");

        var page = Parse(await EmployeePageAsync(employeeId));

        page.Body!.TextContent.Should().Contain("Legacy Manager");

        page.QuerySelector("#line-manager-differs").Should().BeNull(
            "there is no reporting line for them to differ from");

        page.QuerySelector("#line-manager-needs-choice").Should().BeNull(
            "and nothing has gone stale, because nothing was derived");
    }

    // ---- the migration -------------------------------------------------------------------------

    /// <summary>
    /// The one-time repair clears only the stored values that the chart derives anyway, and only
    /// for employees who hold a position.
    /// </summary>
    /// <remarks>
    /// <b>ADR-0029's rule, applied to a field.</b> It moves nobody and touches one property. The
    /// four counts are the point of the test as much as the four records are: an operator upgrading
    /// a tenant is entitled to see that the numbers add up and that nothing else was reached.
    /// </remarks>
    [Fact]
    public async Task TheMigrationClearsOnlyMatchingValuesForPositionHolders()
    {
        var soleHolder = await GivenAnEmployeeAsync("lm-mig-sole", "Sole Holder");
        var outsider = await GivenAnEmployeeAsync("lm-mig-outsider", "Off The Line");

        await PlaceAsync(soleHolder, "zenith-pos-mgr-tech", "zenith-grade-mgr-1", LongAgo);

        var directors = await GivenThreeHoldersAsync(
            "zenith-pos-dir-tech", "zenith-grade-dir-1", "lm-mig-dir", "Mig Director");

        // Four employees, written the way the old code wrote them: the value is on the record in
        // every case, including the two the chart already answered.
        var derived = await GivenALegacyEmployeeAsync(
            "lm-mig-derived", "Written Down", "zenith-pos-se", "zenith-grade-se-1", soleHolder);

        var chosen = await GivenALegacyEmployeeAsync(
            "lm-mig-chosen", "Picked One", "zenith-pos-hr-manager", "zenith-grade-hrm-1", directors[2]);

        var dotted = await GivenALegacyEmployeeAsync(
            "lm-mig-dotted", "Dotted", "zenith-pos-se", "zenith-grade-se-1", outsider);

        var noPosition = await GivenALegacyEmployeeAsync(
            "lm-mig-nopos", "No Position", position: null, grade: null, soleHolder);

        LineManagerBackfillCounts counts = LineManagerBackfillCounts.Nothing;

        await InTenantAsSystemAsync(async services =>
            counts = await services.GetRequiredService<ILineManagerBackfill>().RunAsync());

        counts.Cleared.Should().Be(1, "only the written-down derivation is freed");
        counts.KeptAsChosen.Should().Be(1, "a pick among several is somebody's answer");
        counts.KeptAsOverride.Should().Be(1, "and so is a person off the reporting line");
        counts.Untouched.Should().Be(1, "an employee with no position is not this step's business");

        (await StoredLineManagerAsync(derived)).EmployeeId.Should().BeNullOrEmpty();
        (await StoredLineManagerAsync(chosen)).EmployeeId.Should().Be(directors[2]);
        (await StoredLineManagerAsync(dotted)).EmployeeId.Should().Be(outsider);
        (await StoredLineManagerAsync(noPosition)).EmployeeId.Should().Be(soleHolder);

        // NOTHING WAS MOVED. The employee whose value was cleared shows the same manager as before,
        // because the chart was always what said it.
        (await EmployeePageAsync(derived)).Should().Contain("Sole Holder");

        // And it is safe to re-enter: a second pass finds nothing left to clear.
        LineManagerBackfillCounts again = LineManagerBackfillCounts.Nothing;

        await InTenantAsSystemAsync(async services =>
            again = await services.GetRequiredService<ILineManagerBackfill>().RunAsync());

        again.Cleared.Should().Be(0, "the first pass already did it");
        again.Examined.Should().Be(3, "and the one it freed no longer has a stored value at all");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static AngleSharp.Html.Dom.IHtmlDocument Parse(string html) =>
        new HtmlParser().ParseDocument(html);

    private Task<string> EmployeePageAsync(string employeeId) =>
        BaseTenantFixture.GetPageAsync(_tenant.Administrator, $"/Admin/Employees/Edit/{employeeId}");

    private Task<string> PlacementPageAsync(string employeeId) =>
        BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Employees/Placement/Index/{employeeId}");

    private Task<string> PostToCreateAsync(IDictionary<string, string> fields) =>
        BaseTenantFixture.PostFormAsync(_tenant.Administrator, "/Admin/Employees/Create", fields);

    private Task<string> PostToEditAsync(string employeeId, string name) =>
        BaseTenantFixture.PostFormAsync(
            _tenant.Administrator,
            $"/Admin/Employees/Edit/{employeeId}",
            new Dictionary<string, string>
            {
                ["EmployeeId"] = employeeId,
                ["NameEn"] = name,
                ["NameAr"] = name,
                ["Gender"] = nameof(Gender.Unspecified),
            });

    /// <summary>Creates an employee through the Add-employee screen, holding a position.</summary>
    private async Task<string> GivenAnEmployeeThroughTheFormAsync(
        string code,
        string name,
        string positionCode,
        string gradeCode,
        string? lineManagerEmployeeId = null,
        bool isOverride = false)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Code"] = code,
            ["NameEn"] = name,
            ["NameAr"] = name,
            ["JoinDate"] = LongAgo.ToIso(),
            ["PositionRecordId"] = await RecordIdAsync(positionCode),
            ["GradeRecordId"] = await RecordIdAsync(gradeCode),
        };

        if (!string.IsNullOrEmpty(lineManagerEmployeeId))
        {
            fields["LineManagerEmployeeId"] = lineManagerEmployeeId;
            fields["LineManagerOverride"] = isOverride ? "true" : "false";
        }

        await PostToCreateAsync(fields);

        return await EmployeeIdOfAsync(code);
    }

    /// <summary>
    /// An employee written the way the code wrote them <em>before</em> this addendum: the line
    /// manager on the record in every case, whether or not the chart already said it.
    /// </summary>
    /// <remarks>
    /// Through the service rather than the screen, because the screen no longer writes that shape —
    /// which is the whole fix. This is what the migration finds on a tenant upgrading into it.
    /// </remarks>
    private async Task<string> GivenALegacyEmployeeAsync(
        string code,
        string name,
        string? position,
        string? grade,
        string lineManagerEmployeeId)
    {
        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                code,
                new BilingualText(name, name),
                LongAgo,
                new EmployeeDetails(LineManagerEmployeeId: lineManagerEmployeeId));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            employeeId = created.Value!.EmployeeId;
        });

        if (position is not null)
        {
            await PlaceAsync(employeeId, position, grade!, LongAgo);
        }

        return employeeId;
    }

    /// <summary>Three people in one post — D26 D's own example, and the several-holders case.</summary>
    private async Task<string[]> GivenThreeHoldersAsync(
        string positionCode,
        string gradeCode,
        string codePrefix,
        string namePrefix)
    {
        var holders = new string[3];

        for (var index = 0; index < holders.Length; index++)
        {
            holders[index] = await GivenAnEmployeeAsync(
                $"{codePrefix}-{index + 1}", $"{namePrefix} {index + 1}");

            await PlaceAsync(holders[index], positionCode, gradeCode, LongAgo);
        }

        return holders;
    }

    private async Task<string> GivenAnEmployeeAsync(string code, string name)
    {
        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                code, new BilingualText(name, name), LongAgo, new EmployeeDetails());

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            employeeId = created.Value!.EmployeeId;
        });

        return employeeId;
    }

    private async Task<string> EmployeeIdOfAsync(string code)
    {
        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var found = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync(code);

            found.Should().NotBeNull("the Add-employee form saved '{0}'", code);

            employeeId = found!.EmployeeId;
        });

        return employeeId;
    }

    private async Task<(string? EmployeeId, StoredLineManagerKind Kind)> StoredLineManagerAsync(
        string employeeId)
    {
        (string? EmployeeId, StoredLineManagerKind Kind) stored = (null, StoredLineManagerKind.None);

        await InTenantAsSystemAsync(async services =>
        {
            var record = await services.GetRequiredService<IEmployeeService>().GetAsync(employeeId);

            stored = (record!.LineManagerEmployeeId, record.LineManagerKind);
        });

        return stored;
    }

    private async Task<LineManagerAnswer> LineManagerAnswerAsync(string employeeId)
    {
        var answer = LineManagerAnswer.Nobody;

        await InTenantAsSystemAsync(async services =>
            answer = await services.GetRequiredService<IEmployeeOrganisationService>()
                .GetLineManagerAsync(employeeId, Today));

        return answer;
    }

    private async Task PlaceAsync(
        string employeeId,
        string positionCode,
        string gradeCode,
        DateOnly from) =>
        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var placed = await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                employeeId,
                axis,
                await RecordIdAsync(services, positionCode),
                from,
                gradeRecordId: await RecordIdAsync(services, gradeCode));

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));
        });

    private async Task EndOnTheOrgAxisAsync(string employeeId, DateOnly lastDay) =>
        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>()
                .EndAsync(employeeId, axis, lastDay))
                .Succeeded.Should().BeTrue();
        });

    private async Task<string> RecordIdAsync(string code)
    {
        var recordId = string.Empty;

        await InTenantAsSystemAsync(async services => recordId = await RecordIdAsync(services, code));

        return recordId;
    }

    private static async Task<string> RecordIdAsync(IServiceProvider services, string code) =>
        (await services.GetRequiredService<IDimensionService>().GetByCodeAsync(code))!.RecordId;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("D31 addendum 2 test"))
            {
                await work(services);
            }
        });

    private Task ApplyAsync(string fileName)
    {
        var path = Path.Combine(RecipesFolder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' ships with the module", fileName);

        return _tenant.InTenantAsync(async services =>
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
