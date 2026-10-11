using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Services;
using WorkMate.Platform;
using WorkMate.Platform.Services;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The change-placement screen: the split it shows, what it writes, and what it refuses.
/// </summary>
/// <remarks>
/// <b>Through the screen, deliberately.</b> <c>ReallocateAsync</c> already has service-level tests
/// in <c>EmployeeAssignmentTenantTests</c>; what is untested until here is that the form reaches
/// it with the split the reader built, through the page's own fields, and that the rules the screen
/// applies first refuse what they say they refuse without a 500.
///
/// <b>And that it is the same write path.</b> There is one call — <c>ReallocateAsync</c>, with the
/// same changes/split shape the <c>employee-assignments</c> recipe step builds — so these assert on
/// the rows the engine ends up holding rather than on anything the screen says about them. A second
/// write path is how a screen and an import come to disagree about what a split means.
///
/// Run on their own tenant with the Zenith recipes, because the matrix only exists there.
/// </remarks>
public sealed class ChangePlacementTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>The site engineer the demo splits 60/40 between Civil and Site Team A.</summary>
    private const string Engineer = "zenith-civil-01";

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-employees.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- what the screen shows ---------------------------------------------------------

    /// <summary>
    /// The screen opens on the employee's current split, one editable row per placement.
    /// </summary>
    [Fact]
    public async Task TheScreenShowsEveryCurrentPlacementAsItsOwnRow()
    {
        var page = await ChangePageAsync(Engineer);

        page.Should().Contain("Change where Ahmed Khan sits");

        RowCount(page).Should().Be(2, "the demo splits this engineer 60/40 across two units");

        page.Should().Contain("Civil", "the home department is one of the rows");
        page.Should().Contain("Site Team A", "and the team they are lent to is the other");
    }

    /// <summary>
    /// Somebody placed on nothing still gets a form, with one empty row on it.
    /// </summary>
    /// <remarks>
    /// An empty page with an "add" button on it is a form that has to be assembled before it can be
    /// used. The first row is the overwhelmingly common case — one unit, 100%, main — so it is
    /// there.
    /// </remarks>
    [Fact]
    public async Task SomebodyPlacedNowhereStillGetsOneEmptyRow()
    {
        var employeeId = await CreateUnplacedAsync("change-unplaced-1", "Unplaced Person");
        var page = await GetAsync($"/Admin/Employees/Placement/Change/{employeeId}");

        RowCount(page).Should().Be(1);
        page.Should().Contain("Change where Unplaced Person sits");
    }

    // ---- what it writes ----------------------------------------------------------------

    /// <summary>
    /// Saving a changed split writes it through <c>ReallocateAsync</c>, and the old rows survive.
    /// </summary>
    /// <remarks>
    /// Both halves matter and they are different claims. The new rows are what the engine resolves
    /// from the effective date; the old ones are what it still resolves from the day before, which
    /// is the whole reason placement is dated rather than overwritten. A screen that got the first
    /// right and the second wrong would look correct on every screen and be wrong in every
    /// historical report.
    /// </remarks>
    [Fact]
    public async Task SavingANewSplitOpensTheNewRowsAndClosesTheOldOnesTheDayBefore()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, _) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);
        var effectiveFrom = ValueOf(page, "EffectiveFrom");

        var fields = RenderedForm.FieldsOf(page)
            .With("Rows[0].AllocationPercent", "70")
            .With("Rows[1].AllocationPercent", "30");

        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        LandedOnTheOverview(response).Should().BeTrue("a saved change redirects to the overview");

        var from = DateOnly.Parse(effectiveFrom, System.Globalization.CultureInfo.InvariantCulture);

        await InTenantAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var now = await assignments.GetAllEffectiveAsync(employeeId, structureId, from);

            now.Select(row => row.AllocationPercent).Should().BeEquivalentTo(
                [70m, 30m], "the split the form stated is what the engine now resolves");

            now.Count(row => row.IsPrimary).Should().Be(1, "exactly one row is main");

            // The day before still resolves the old 60/40, which is the point of a dated write.
            var yesterday = await assignments.GetAllEffectiveAsync(
                employeeId, structureId, from.AddDays(-1));

            yesterday.Select(row => row.AllocationPercent).Should().BeEquivalentTo(
                [60m, 40m], "the previous split runs to the day before and is not deleted");

            yesterday.Should().AllSatisfy(row => row.Range.To.Should().Be(from.AddDays(-1)));
        });

        var after = await HistoryAsync(employeeId, structureId);

        after.Should().BeGreaterThan(before, "nothing is deleted: a change adds rows");
    }

    /// <summary>
    /// Moving the main row to the other unit is a change like any other, and leaves exactly one.
    /// </summary>
    [Fact]
    public async Task MovingTheMainRowToTheOtherUnitLeavesExactlyOneMain()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, siteTeam) = await IdsAsync("zenith-org", "zenith-team-site-a");

        var page = await ChangePageAsync(Engineer);
        var effectiveFrom = ValueOf(page, "EffectiveFrom");

        // The row holding the site team, whichever order the screen drew them in.
        var siteRow = RowIndexOf(page, siteTeam);

        siteRow.Should().BeGreaterThanOrEqualTo(0, "the site team is one of the rows on this form");

        var fields = RenderedForm.FieldsOf(page)
            .With("mainRow", siteRow.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        LandedOnTheOverview(response).Should().BeTrue();

        var from = DateOnly.Parse(effectiveFrom, System.Globalization.CultureInfo.InvariantCulture);

        await InTenantAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var rows = await assignments.GetAllEffectiveAsync(employeeId, structureId, from);

            rows.Count(row => row.IsPrimary).Should().Be(1);

            rows.Single(row => row.IsPrimary).RecordId.Should().Be(
                siteTeam, "the row the radio chose is the one that answers \"where do they work\"");
        });
    }

    /// <summary>
    /// Adding a row is a round trip that writes nothing, and the new row starts on the remainder.
    /// </summary>
    [Fact]
    public async Task AddingARowRedrawsTheFormAndWritesNothing()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, _) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);

        var fields = RenderedForm.FieldsOf(page).With("command", "add");
        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "adding a row re-renders rather than saving");

        RowCount(await response.Content.ReadAsStringAsync()).Should().Be(3);

        (await HistoryAsync(employeeId, structureId)).Should().Be(
            before, "a round trip to add a row has written nothing");
    }

    /// <summary>Removing a row is the same round trip in reverse.</summary>
    [Fact]
    public async Task RemovingARowRedrawsTheFormAndWritesNothing()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, _) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);

        var fields = RenderedForm.FieldsOf(page).With("command", "remove:1");
        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        RowCount(await response.Content.ReadAsStringAsync()).Should().Be(1);

        (await HistoryAsync(employeeId, structureId)).Should().Be(before);
    }

    // ---- what it refuses ---------------------------------------------------------------

    /// <summary>
    /// A split that does not total 100 comes back as a message on the form. Never a 500.
    /// </summary>
    [Fact]
    public async Task ASplitThatDoesNotTotalAHundredIsRefusedOnTheForm()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, _) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);

        var fields = RenderedForm.FieldsOf(page)
            .With("Rows[0].AllocationPercent", "70")
            .With("Rows[1].AllocationPercent", "20");

        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a refusal is the form again, not an error page");

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("must total 100", "the form says what is wrong");
        html.Should().Contain("90", "and what the figures actually come to");

        (await HistoryAsync(employeeId, structureId)).Should().Be(before, "nothing was written");
    }

    /// <summary>Two main rows is refused with the count named.</summary>
    [Fact]
    public async Task TwoMainRowsAreRefusedOnTheForm()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var page = await ChangePageAsync(Engineer);

        // Both radios posted, which is what a form with two checked boxes would send and what the
        // screen must survive even though its own markup cannot produce it.
        var fields = RenderedForm.FieldsOf(page);

        fields.Add(new KeyValuePair<string, string>("mainRow", "1"));

        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The same unit on two rows is refused rather than stored twice.</summary>
    [Fact]
    public async Task TheSameUnitOnTwoRowsIsRefusedOnTheForm()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, civil) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);

        var fields = RenderedForm.FieldsOf(page)
            .With("Rows[0].RecordId", civil)
            .With("Rows[1].RecordId", civil)
            .With("Rows[0].AllocationPercent", "60")
            .With("Rows[1].AllocationPercent", "40");

        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await response.Content.ReadAsStringAsync())
            .Should().Contain("already on another row");

        (await HistoryAsync(employeeId, structureId)).Should().Be(before);
    }

    /// <summary>
    /// A date before a change already on record is refused, and the form says what the limit is.
    /// </summary>
    /// <remarks>
    /// The rule exists because <c>ReallocateAsync</c> replaces every row starting on or after its
    /// effective date. Backdating behind the March change would discard it — the "overwrite"
    /// behaviour ADR-0005's addendum examined and rejected for moves, reached through a different
    /// door. The screen refuses rather than doing it quietly.
    /// </remarks>
    [Fact]
    public async Task ADateBeforeTheLatestRecordedChangeIsRefusedOnTheForm()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, _) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);

        page.Should().Contain(
            "A new one cannot be dated before it",
            "the screen says so before the reader types a date, not only after they press Save");

        var fields = RenderedForm.FieldsOf(page).With("EffectiveFrom", "2024-02-01");
        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await response.Content.ReadAsStringAsync())
            .Should().Contain("would replace that one rather than slot in behind it");

        (await HistoryAsync(employeeId, structureId)).Should().Be(before, "the March change still stands");
    }

    /// <summary>
    /// A refusal from the engine itself reaches the reader rather than becoming a 500.
    /// </summary>
    /// <remarks>
    /// The screen's own rules are about the shape of the set; the engine's are about the units in
    /// it — that a record exists, and that the structure places employees on its type. The screen
    /// deliberately does not restate those, so what has to be true is that what the engine says
    /// comes back on the form. A unit id that names nothing is the one such refusal available on
    /// every tenant, because no demo structure constrains employee attachment.
    /// </remarks>
    [Fact]
    public async Task AUnitTheEngineRefusesComesBackOnTheFormWithItsOwnReason()
    {
        var employeeId = await EmployeeIdAsync(Engineer);
        var (structureId, _) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var before = await HistoryAsync(employeeId, structureId);
        var page = await ChangePageAsync(Engineer);

        var fields = RenderedForm.FieldsOf(page)
            .With("Rows[0].RecordId", "not-a-record-id")
            .With("Rows[0].AllocationPercent", "60")
            .With("Rows[1].AllocationPercent", "40");

        var response = await PostAsync($"/Admin/Employees/Placement/Change/{employeeId}", fields);

        LandedOnTheOverview(response).Should().BeFalse("the engine refused it");

        (await response.Content.ReadAsStringAsync()).Should().Contain(
            "There is no dimension record",
            "the engine's own reason, not a second wording of the same rule");

        (await HistoryAsync(employeeId, structureId)).Should().Be(before);
    }

    // ---- who may use it ----------------------------------------------------------------

    /// <summary>
    /// Without <c>AssignEmployees</c> the screen is refused, and the list offers no way to it.
    /// </summary>
    [Fact]
    public async Task WithoutThePermissionTheScreenIsRefusedAndTheListOffersNoLink()
    {
        var employeeId = await EmployeeIdAsync(Engineer);

        // The Auditor: given ViewEmployees on purpose — reading the staff list is most of the job —
        // and not given AssignEmployees, which is exactly the pair that makes this a test of the
        // second rather than of being signed in at all.
        using var reader = await _tenant.CreateSignedInClientAsync(
            "change-placement-reader", "Workmate!Integration1", PlatformRoles.Auditor, allowAutoRedirect: false);

        var refused = await reader.GetAsync($"/Admin/Employees/Placement/Change/{employeeId}");

        refused.StatusCode.Should().NotBe(HttpStatusCode.OK, "AssignEmployees is what opens this screen");

        var list = await reader.GetAsync("/Admin/Employees/Index");
        var html = await list.Content.ReadAsStringAsync();

        html.Should().NotContain(
            "employee-change-placement",
            "a link that lands on a refusal is a worse answer than no link");
    }

    // ---- arriving from a unit's card ---------------------------------------------------

    /// <summary>
    /// The Place screen opened from a card knows the unit and asks who, rather than the reverse.
    /// </summary>
    [Fact]
    public async Task ThePlaceScreenOpenedFromACardPrefillsTheStructureAndUnit()
    {
        var (structureId, civil) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var page = await GetAsync(
            $"/Admin/Employees/Placement/Place?structureId={structureId}&recordId={civil}");

        page.Should().Contain("Place an employee in", "the heading names the unit the card was on");
        page.Should().Contain("Civil");

        page.Should().Contain(
            "data-employee-picker-url",
            "there is no employee yet, so the form asks for one");

        page.Should().Contain($"value=\"{civil}\"", "the unit the card named is already chosen");
    }

    // ---- harness ---------------------------------------------------------------------------

    private static int RowCount(string html) =>
        System.Text.RegularExpressions.Regex.Count(html, "<tr class=\"change-row\"");

    /// <summary>Which row's unit picker holds <paramref name="recordId"/>, or -1.</summary>
    private static int RowIndexOf(string html, string recordId)
    {
        for (var index = 0; index < RowCount(html); index++)
        {
            var name = $"Rows[{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}].RecordId";
            var start = html.IndexOf($"name=\"{name}\"", StringComparison.Ordinal);

            if (start < 0)
            {
                continue;
            }

            var end = html.IndexOf("</select>", start, StringComparison.Ordinal);
            var markup = html[start..(end < 0 ? html.Length : end)];

            if (markup.Contains($"value=\"{recordId}\" selected", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The value of a rendered input, which is how a test reads a suggested date.</summary>
    private static string ValueOf(string html, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, $"name=\"{name}\"[^>]*value=\"([^\"]*)\"");

        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(
                html, $"value=\"([^\"]*)\"[^>]*name=\"{name}\"");
        }

        match.Success.Should().BeTrue("'{0}' is a field on this page", name);

        return match.Groups[1].Value;
    }

    private Task<string> ChangePageAsync(string employeeCode) =>
        EmployeeIdAsync(employeeCode)
            .ContinueWith(id => GetAsync($"/Admin/Employees/Placement/Change/{id.Result}"), TaskScheduler.Default)
            .Unwrap();

    private async Task<string> GetAsync(string path)
    {
        var response = await _tenant.Administrator.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0}", path);

        return await response.Content.ReadAsStringAsync();
    }

    private Task<HttpResponseMessage> PostAsync(
        string path, IEnumerable<KeyValuePair<string, string>> fields) =>
        _tenant.Administrator.PostAsync(path, new FormUrlEncodedContent(fields));

    /// <summary>
    /// Whether a post was accepted, by where it ended up rather than by its status code.
    /// </summary>
    /// <remarks>
    /// The fixture's client follows redirects, so a save and a refusal both answer 200: the save
    /// because it redirected to the overview and the overview rendered, the refusal because it
    /// re-rendered the form. The two are told apart by the URL the client finished on, which is
    /// exactly what a reader sees.
    /// </remarks>
    private static bool LandedOnTheOverview(HttpResponseMessage response) =>
        response.RequestMessage?.RequestUri?.AbsolutePath
            .Contains("/Placement/Index", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<string> EmployeeIdAsync(string code)
    {
        string employeeId = null!;

        await InTenantAsync(async services =>
        {
            employeeId = (await services.GetRequiredService<IEmployeeService>().GetByCodeAsync(code))!.EmployeeId;
        });

        return employeeId;
    }

    private async Task<string> CreateUnplacedAsync(string code, string nameEn)
    {
        string employeeId = null!;

        await InTenantAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                code,
                new WorkMate.Core.BilingualText(nameEn, string.Empty),
                new DateOnly(2025, 1, 1),
                new EmployeeDetails(null, null, WorkMate.Records.Models.Gender.Unspecified, null));

            created.Succeeded.Should().BeTrue();
            employeeId = created.Value!.EmployeeId;
        });

        return employeeId;
    }

    /// <summary>How many placement rows are on record, closed ones included.</summary>
    private async Task<int> HistoryAsync(string employeeId, string structureId)
    {
        var count = 0;

        await InTenantAsync(async services =>
        {
            count = (await services.GetRequiredService<IEmployeeAssignmentService>()
                .GetHistoryAsync(employeeId, structureId)).Count;
        });

        return count;
    }

    private async Task<(string StructureId, string RecordId)> IdsAsync(string structureCode, string recordCode)
    {
        string structureId = null!, recordId = null!;

        await InTenantAsync(async services =>
        {
            structureId = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync(structureCode))!.StructureId;

            recordId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync(recordCode))!.RecordId;
        });

        return (structureId, recordId);
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("change placement test"))
            {
                await work(services);
            }
        });

    private async Task ApplyAsync(string fileName)
    {
        await InTenantAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                new RecipeDescriptor
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                    RequireNewScope = false,
                },
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
