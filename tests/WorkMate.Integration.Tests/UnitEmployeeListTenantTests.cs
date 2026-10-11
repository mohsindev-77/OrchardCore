using System.Net;
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
/// The employee list filtered to a unit: the figures, the roles, the head, and who may open it.
/// </summary>
/// <remarks>
/// <b>The number has to be the card's number.</b> The designer card says "14 employees" and this
/// screen is what that number opens; if the two were computed separately they would disagree the
/// first time anybody split an allocation, and the chart and the list disagreeing about the size of
/// a department is the kind of defect that is noticed in a board meeting rather than in a test. So
/// the total comes from the same <c>IEmployeeAssignmentService</c> call, and these assert they match
/// rather than asserting a literal on both sides.
///
/// Run on their own tenant with the Zenith recipes, because the figures being asserted are Zenith's
/// and the matrix only exists there.
/// </remarks>
public sealed class UnitEmployeeListTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>After every join date and every deployment in the Zenith employee recipe.</summary>
    private const string AsAt = "2026-01-01";

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-employees.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    /// <summary>
    /// Civil shows its fourteen, matrix rows included, and the total is the card's own number.
    /// </summary>
    /// <remarks>
    /// The engineers deployed to a site team are the point: they hold a primary at Civil and a
    /// secondary at the team, and a screen that listed only people whose <em>whole</em> allocation
    /// was here would show twelve and be wrong about the department in the way the matrix exists to
    /// prevent.
    /// </remarks>
    [Fact]
    public async Task CivilListsItsFourteenIncludingTheOnesDeployedElsewhere()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var page = await GetAsync($"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&take=100");

        page.Should().Contain("Employees in Civil", "the header says which unit, on which axis, on which date");

        var listed = RowCodesIn(page);

        listed.Should().HaveCount(14, "the department's figure in the headcount table");

        listed.Should().Contain(
            ["zenith-civil-01", "zenith-civil-02", "zenith-civil-03"],
            "the engineers deployed to site teams are still Civil's people — their home department "
            + "is what the department's headcount counts");

        // And the figure in the header is the card's figure, from the same service call rather than
        // from the length of this list.
        await InTenantAsync(async services =>
        {
            var counts = await services.GetRequiredService<IEmployeeAssignmentService>()
                .CountEmployeesAtAsync(structureId, [recordId], new DateOnly(2026, 1, 1));

            page.Should().Contain(
                $"{counts[recordId]} at this unit",
                "the header's number is the card's number, not a count of the rows on this page");
        });
    }

    /// <summary>
    /// Site Team A shows forty-four, and the site engineer reads as additional with their home named.
    /// </summary>
    [Fact]
    public async Task SiteTeamAShowsItsFortyFourAndNamesTheEngineersHomeDepartment()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-team-site-a");

        var page = await GetAsync($"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&take=100");

        RowCodesIn(page).Should().HaveCount(
            44, "one site engineer, three foremen and forty labour");

        // The engineer's row says what they are here and where they belong, which is the whole
        // matrix in one line.
        var engineerRow = RowFor(page, "zenith-civil-01");

        engineerRow.Should().Contain("Additional", "they are lent to this team, not based on it");
        engineerRow.Should().Contain("Civil", "and their home department is named beside it");
        engineerRow.Should().Contain("40", "the allocation this team accounts for");

        // A labourer, by contrast, is wholly here.
        RowFor(page, "zenith-labour-a-01").Should().Contain("Main");
    }

    /// <summary>
    /// The column is "Placement", and its values are "Main" and "Additional".
    /// </summary>
    /// <remarks>
    /// "Role here" described a job title, which this column has never held: it says whether the
    /// unit is where somebody mainly works or somewhere they are also counted. "Secondary" had the
    /// same problem from the other end — it reads as a judgement about the person rather than a
    /// statement about the unit's share of them.
    /// </remarks>
    [Fact]
    public async Task ThePlacementColumnIsCalledPlacementAndReadsMainOrAdditional()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-team-site-a");

        var page = await GetAsync($"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&take=100");

        page.Should().Contain(">Placement<");
        page.Should().NotContain(">Role here<");
        page.Should().NotContain(">Secondary<");
        page.Should().NotContain(">Primary<");
    }

    /// <summary>
    /// Ahmed Khan is split 60/40 between Civil and Site Team A, and no row ever says "home:" twice.
    /// </summary>
    /// <remarks>
    /// <b>The defect.</b> His row on Site Team A read "Civil · Secondary · 40% · home: Civil". The
    /// placement column beside the role is resolved on <em>the structure the list is filtered to</em>
    /// — which is where his main placement is — so "home: {0}" was, by construction, always the
    /// unit already named one cell to the left. It could not have been anything else: the only way
    /// the two would have differed is if the main placement had been read off a different
    /// structure, which would have made the pair meaningless rather than redundant.
    ///
    /// <b>The replacement.</b> Where else somebody sits is one question answered in one place: the
    /// "+N more" disclosure in the placement column, which names the other units by their full path
    /// and says what share each accounts for. Both ends of the 60/40 are asserted here, because the
    /// disclosure has to be right from whichever end the reader opened the list.
    /// </remarks>
    [Fact]
    public async Task AnEmployeeSplitSixtyFortyNamesBothUnitsAndNeverRepeatsTheOneAlreadyShown()
    {
        var (structureId, siteTeam) = await IdsAsync("zenith-org", "zenith-team-site-a");
        var (_, civil) = await IdsAsync("zenith-org", "zenith-dept-civil");

        // From the team he is lent to: the main placement is Civil, and the 40% he is here for is
        // behind the disclosure.
        var fromTheTeam = await GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={siteTeam}&asAt={AsAt}&take=100");

        var onTheTeam = RowFor(fromTheTeam, "zenith-civil-01");

        onTheTeam.Should().NotContain(
            "home:", "the unit already named in the placement column is not named again beside it");

        onTheTeam.Should().Contain("Civil", "the main placement leads");
        onTheTeam.Should().Contain("+1 more", "and the other placement is one click away");
        onTheTeam.Should().Contain("Site Team A", "which is this team");
        onTheTeam.Should().Contain("40%", "for the share it accounts for");

        // And from his home department, the same two facts the other way round.
        var fromCivil = await GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={civil}&asAt={AsAt}&take=100");

        var inCivil = RowFor(fromCivil, "zenith-civil-01");

        inCivil.Should().NotContain("home:");
        inCivil.Should().Contain("Main", "Civil is where he mainly works");
        inCivil.Should().Contain("60%", "the share Civil accounts for");
        inCivil.Should().Contain("+1 more");
        inCivil.Should().Contain("Site Team A");

        // Somebody with one placement has nothing to disclose, so the control is not drawn at all.
        RowFor(fromCivil, "zenith-civil-04").Should().NotContain("more");
    }

    /// <summary>
    /// The star names every unit this person heads, not "this unit".
    /// </summary>
    /// <remarks>
    /// ADR-0012 lets one person head several, and the Zenith demo deliberately has one over two
    /// departments and a site team. "Heads this unit" is the least useful true thing available to
    /// say to the reader hovering that star.
    /// </remarks>
    [Fact]
    public async Task TheStarsTooltipNamesTheUnitsThisPersonHeads()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var page = await GetAsync($"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&take=100");

        var row = RowFor(page, "zenith-civil-01");

        row.Should().Contain("Heads ", "the tooltip names them rather than saying \"this unit\"");
        row.Should().Contain("Mechanical", "he heads Mechanical as well as Civil");
        row.Should().Contain("Site Team A", "and the site team");
    }

    /// <summary>
    /// A Division with sub-units included lists everybody beneath it, and the total agrees.
    /// </summary>
    [Fact]
    public async Task ADivisionWithSubUnitsShowsEverybodyBeneathIt()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-div-engineering");

        var itself = await GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&take=200");

        RowCodesIn(itself).Should().BeEmpty(
            "nobody is placed on a Division — it is a container, and employees attach to leaves");

        var beneath = await GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&includeSubUnits=true&take=200");

        var listed = RowCodesIn(beneath);

        listed.Should().HaveCount(
            31, "Civil's fourteen, Electrical's eight and Mechanical's nine");

        listed.Should().Contain("zenith-mech-01");

        await InTenantAsync(async services =>
        {
            var under = await services.GetRequiredService<IEmployeeAssignmentService>()
                .GetEmployeesUnderAsync(
                    structureId, recordId, new DateOnly(2026, 1, 1), includeDescendants: true, skip: 0, take: 1);

            beneath.Should().Contain(
                $"{under.Total} at this unit and below it",
                "the header's number is the engine's number with sub-units too");
        });
    }

    /// <summary>
    /// The head who is not a member is listed, marked, and not counted.
    /// </summary>
    /// <remarks>
    /// The Zenith recipe makes Finance's manager the acting head of HR, which is ADR-0012's case.
    /// All three halves matter: listed, because "who runs this" is the first question; marked,
    /// because otherwise they look like staff; and not counted, because they are counted by Finance
    /// and a person in two headcounts makes the company bigger than it is.
    /// </remarks>
    [Fact]
    public async Task TheHeadWhoIsNotAMemberIsListedMarkedAndNotCounted()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-dept-hr");

        var page = await GetAsync($"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&take=100");

        var listed = RowCodesIn(page);

        listed.Should().Contain("zenith-fin-01", "the acting head is on the list of the unit they head");
        listed.Should().HaveCount(7, "HR's six, plus the head who works in Finance");

        page.Should().Contain("6 at this unit", "and the count is six: the head is not one of them");

        RowFor(page, "zenith-fin-01").Should().Contain(
            "Head (not a member)", "marked, or they read as somebody who works here");

        // The head of a unit somebody does work in is counted normally — the same screen, the other
        // case, so that "not counted" is a statement about membership and not about headship.
        var (_, civil) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var civilPage = await GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={civil}&asAt={AsAt}&take=100");

        civilPage.Should().Contain("14 at this unit");
        RowFor(civilPage, "zenith-civil-01").Should().Contain("Primary", "Civil's head works in Civil");
    }

    /// <summary>
    /// Searching within a unit narrows the list and leaves the unit's own figure alone.
    /// </summary>
    /// <remarks>
    /// Two numbers that mean different things: how many are in the unit, and how many of them match
    /// what was typed. A screen that let the search change the first would be telling a reader the
    /// department had shrunk.
    /// </remarks>
    [Fact]
    public async Task SearchingWithinAUnitNarrowsTheListAndNotTheUnitsFigure()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-dept-civil");

        var page = await GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}&search=zenith-civil-01");

        RowCodesIn(page).Should().BeEquivalentTo(["zenith-civil-01"]);

        page.Should().Contain("14 at this unit", "the unit still has fourteen people in it");
    }

    /// <summary>
    /// Without ViewEmployees the screen is refused and the designer offers no way to it.
    /// </summary>
    [Fact]
    public async Task WithoutThePermissionTheScreenIsRefusedAndTheCardOffersNoLink()
    {
        var (structureId, recordId) = await IdsAsync("zenith-org", "zenith-dept-civil");

        // The Employee role, which is the one that reaches the admin panel and is granted no
        // employee-record permission at all. Not the Auditor: an auditor is *given* ViewEmployees
        // on purpose — reading the staff list is most of the job — so using them here would have
        // asserted that the permission works by picking somebody who has it.
        using var reader = await _tenant.CreateSignedInClientAsync(
            "unitlist-reader", "Workmate!Integration1", PlatformRoles.Employee, allowAutoRedirect: false);

        var refused = await reader.GetAsync(
            $"/Admin/Employees/Index?structureId={structureId}&recordId={recordId}&asAt={AsAt}");

        refused.StatusCode.Should().NotBe(HttpStatusCode.OK, "ViewEmployees is what opens this screen");

        // And the designer does not offer a link somebody would be refused at.
        var designer = await reader.GetAsync($"/Admin/Dimensions/Designer/Index?structureId={structureId}");
        var chart = await designer.Content.ReadAsStringAsync();

        chart.Should().NotContain(
            "data-designer-action=\"viewemployees\"",
            "a link that lands on a refusal is a worse answer than no link");

        chart.Should().NotContain("data-designer-employees-link");
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>The employee codes on the rows of a rendered list.</summary>
    private static IReadOnlyList<string> RowCodesIn(string html) =>
        [.. System.Text.RegularExpressions.Regex
            .Matches(html, "<tr class=\"employee-row\" data-employee-code=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)];

    /// <summary>One row's markup, so an assertion about a person is about that person.</summary>
    private static string RowFor(string html, string code)
    {
        var start = html.IndexOf($"data-employee-code=\"{code}\"", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1, "'{0}' is on this page", code);

        var end = html.IndexOf("</tr>", start, StringComparison.Ordinal);

        return html[start..(end < 0 ? html.Length : end)];
    }

    private async Task<string> GetAsync(string path)
    {
        var response = await _tenant.Administrator.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0}", path);

        return await response.Content.ReadAsStringAsync();
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
            using (services.GetRequiredService<ISystemOperation>().Begin("unit employee list test"))
            {
                await work(services);
            }
        });

    private async Task ApplyAsync(string fileName)
    {
        await InTenantAsync(async services =>
        {
            var folder = RecipesFolder;
            var fileProvider = new PhysicalFileProvider(folder);

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
