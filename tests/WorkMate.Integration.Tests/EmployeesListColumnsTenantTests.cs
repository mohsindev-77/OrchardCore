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
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Slice F6c: the employees list shows what each person holds, where it sits and who answers for
/// it — derived, batched, filterable and sortable.
/// </summary>
/// <remarks>
/// <b>The columns are the easy half.</b> The half worth testing is that they are the SAME answers
/// the employee's own page gives — D30's organisation chain and D31 addendum 2's live line manager
/// — and that drawing fifty of them does not cost fifty reads of each. A list composed from its own
/// slightly different reads is how a person comes to have one manager on their page and another on
/// the list they appear in.
/// </remarks>
public sealed class EmployeesListColumnsTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly DateOnly LongAgo = Today.AddDays(-60);

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    /// <summary>
    /// Every column the brief names is on the list, filled from what the person actually holds.
    /// </summary>
    [Fact]
    public async Task TheListShowsPositionGradeReportsToAndTheOrganisationPath()
    {
        var boss = await GivenAnEmployeeAsync("f6c-boss", "Mona Saleh");

        await PlaceAsync(boss, "zenith-pos-mgr-tech", "zenith-grade-mgr-1");

        var engineer = await GivenAnEmployeeAsync("f6c-eng", "Dana Fahd");

        await PlaceAsync(engineer, "zenith-pos-se", "zenith-grade-se-1");

        var row = await RowAsync("f6c-eng");

        row.QuerySelector(".employee-position")!.TextContent.Trim()
            .Should().Be("Software Engineer", "Position stands where Job title used to — D22");

        row.QuerySelector(".employee-grade")!.TextContent
            .Should().Contain("SE-1", "the grade is on the placement row — ADR-0026");

        row.QuerySelector(".employee-line-manager")!.TextContent
            .Should().Contain("Mona Saleh", "the holder of the post above, live — D31 addendum 2");

        // The tenant's OWN names for its levels, as one column — D28 D. Zenith's engineer sits
        // under the Electrical department, which sits under a division and the company.
        row.QuerySelector(".employee-organisation")!.TextContent
            .Should().Contain("Electrical", "the path the position sits in, derived — D30");
    }

    /// <summary>
    /// Somebody placed at a unit rather than a position is named as such, not left blank.
    /// </summary>
    /// <remarks>
    /// ADR-0029's mid-migration state. D28 C requires it be made legible rather than hidden, and a
    /// blank Position cell on a list of two hundred people is exactly how it stays hidden.
    /// </remarks>
    [Fact]
    public async Task AnEmployeeWithNoPositionSaysSoWithTheUnitTheyAreIn()
    {
        // A TENANT AS IT WAS BEFORE F6, then upgraded — which is the only way to get this row,
        // because D30 refuses to write a new one and ADR-0029 refuses to move the old one. Its own
        // fixture, because the class's tenant has the Position level from the start.
        using var upgrading = new BaseTenantFixture();

        await upgrading.InitializeAsync();

        await ApplyToAsync(upgrading, "organisation-designer-zenith.recipe.json");

        await InTenantAsSystemAsync(upgrading, async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "f6c-legacy", new BilingualText("Old Record", "Old Record"), LongAgo, new EmployeeDetails());

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            // Straight at a department, which is what every pre-F6 placement is.
            (await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                created.Value!.EmployeeId,
                axis,
                await RecordIdAsync(services, "zenith-dept-electrical"),
                LongAgo))
                .Succeeded.Should().BeTrue("this is the row every pre-F6 employee has");
        });

        // The upgrade: the Position level arrives and the row above stays exactly as it was.
        await ApplyToAsync(upgrading, "organisation-designer-zenith-positions.recipe.json");

        var row = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                upgrading.Administrator, "/Admin/Employees/Index?search=f6c-legacy"))
            .QuerySelector(".employee-row[data-employee-code='f6c-legacy']");

        row.Should().NotBeNull("the employee is still listed — ADR-0029 moves nobody");

        var cell = row!.QuerySelector(".employee-position")!;

        cell.QuerySelector(".employee-no-position").Should().NotBeNull(
            "a blank cell would hide the backlog this label exists to make workable");

        cell.TextContent.Should().Contain("Electrical", "and names the unit they are actually in");

        await upgrading.DisposeAsync();
    }

    /// <summary>
    /// The list and the employee's own page give the same line manager, through one read path.
    /// </summary>
    /// <remarks>
    /// <b>The claim that matters most here.</b> The list resolves a page of answers at once and the
    /// page resolves one; if those were two implementations of D31 addendum 2's staleness rules
    /// they would drift, and the first anybody would know is a person whose manager differs
    /// depending on which screen you look at. They share one body, and this is what says so.
    /// </remarks>
    [Fact]
    public async Task TheBatchedReadAgreesWithThePerEmployeeOneOnEveryFooting()
    {
        var sole = await GivenAnEmployeeAsync("f6c-agree-sole", "Sole Holder");
        var outsider = await GivenAnEmployeeAsync("f6c-agree-out", "Off The Line");

        await PlaceAsync(sole, "zenith-pos-mgr-tech", "zenith-grade-mgr-1");

        // Derived: one holder of the post above, stored nowhere.
        var derived = await GivenAnEmployeeAsync("f6c-agree-derived", "Derived Case");

        await PlaceAsync(derived, "zenith-pos-se", "zenith-grade-se-1");

        // Override: a person off the reporting line, stored deliberately.
        var overridden = await GivenAnEmployeeAsync("f6c-agree-override", "Override Case");

        await PlaceAsync(overridden, "zenith-pos-se", "zenith-grade-se-1");

        await InTenantAsSystemAsync(async services =>
            (await services.GetRequiredService<IEmployeeService>().SetLineManagerAsync(
                overridden, outsider, WorkMate.Records.Models.StoredLineManagerKind.Override))
                .Succeeded.Should().BeTrue());

        // No position at all: nothing derived, and a stored value is the only answer there is.
        var unplaced = await GivenAnEmployeeAsync("f6c-agree-none", "No Position");

        var everyone = new[] { derived, overridden, unplaced, sole, outsider };

        await InTenantAsSystemAsync(async services =>
        {
            var organisation = services.GetRequiredService<IEmployeeOrganisationService>();

            var batched = await organisation.GetLinesAsync(everyone, Today);

            foreach (var employeeId in everyone)
            {
                var one = await organisation.GetLineManagerAsync(employeeId, Today);
                var many = batched.GetValueOrDefault(employeeId)?.LineManager
                    ?? LineManagerAnswer.Nobody();

                many.How.Should().Be(one.How, "the footing must match for {0}", employeeId);
                many.EmployeeId.Should().Be(one.EmployeeId, "and so must the person");
                many.Name.Should().Be(one.Name);
            }
        });
    }

    /// <summary>
    /// Filtering by a position lists its holders and nobody else, and says which post.
    /// </summary>
    [Fact]
    public async Task TheListFiltersByPosition()
    {
        var holder = await GivenAnEmployeeAsync("f6c-filter-in", "In The Post");
        var other = await GivenAnEmployeeAsync("f6c-filter-out", "Somewhere Else");

        await PlaceAsync(holder, "zenith-pos-se", "zenith-grade-se-1");
        await PlaceAsync(other, "zenith-pos-hr-manager", "zenith-grade-hrm-1");

        var page = await ListAsync($"positionRecordId={await RecordIdAsync("zenith-pos-se")}");

        Codes(page).Should().Contain("f6c-filter-in").And.NotContain(
            "f6c-filter-out", "filtering by a post is filtering to its holders");
    }

    /// <summary>
    /// Filtering by a unit includes everybody below it, through the position they hold.
    /// </summary>
    /// <remarks>
    /// The roll-up is free and that is the point: a position is a node on the organisation axis
    /// with a parent, so the closure index already puts its holder under the department. This is
    /// the test that says the Employees list reads it the same way the designer's headcount does.
    /// </remarks>
    [Fact]
    public async Task TheUnitFilterIncludesEverybodyBelowIt()
    {
        var engineer = await GivenAnEmployeeAsync("f6c-under", "Under The Division");

        await PlaceAsync(engineer, "zenith-pos-se", "zenith-grade-se-1");

        string axis = null!, division = null!;

        await InTenantAsSystemAsync(async services =>
        {
            axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            // Two levels above the Software Engineer's department, so nothing but the roll-up can
            // put them in this list.
            division = await RecordIdAsync(services, "zenith-div-engineering");
        });

        var withSubUnits = await ListAsync(
            $"structureId={axis}&recordId={division}&includeSubUnits=true");

        Codes(withSubUnits).Should().Contain(
            "f6c-under",
            "a position sits in a department which sits in the division, and the closure index "
            + "already rolls its holder all the way up");

        var atTheDivisionItself = await ListAsync($"structureId={axis}&recordId={division}");

        Codes(atTheDivisionItself).Should().NotContain(
            "f6c-under", "nobody is placed AT the division, which is a different question");
    }

    /// <summary>
    /// Sorting by position orders the page by the post's name, and by grade by the grade's.
    /// </summary>
    [Fact]
    public async Task TheListSortsByPositionAndByGrade()
    {
        // Named so that name order and position order are different orders — otherwise the test
        // would pass on a list that ignored the parameter entirely.
        var first = await GivenAnEmployeeAsync("f6c-sort-a", "Aaa Person");
        var second = await GivenAnEmployeeAsync("f6c-sort-b", "Bbb Person");

        // "Software Engineer" sorts after "HR Manager", so the two orders disagree.
        await PlaceAsync(first, "zenith-pos-se", "zenith-grade-se-1");
        await PlaceAsync(second, "zenith-pos-hr-manager", "zenith-grade-hrm-1");

        var byName = Codes(await ListAsync("search=f6c-sort"));

        byName.Should().Equal(["f6c-sort-a", "f6c-sort-b"], "by name, Aaa before Bbb");

        var byPosition = Codes(await ListAsync("search=f6c-sort&sort=Position"));

        byPosition.Should().Equal(
            ["f6c-sort-b", "f6c-sort-a"],
            "by position, HR Manager before Software Engineer — which is the opposite order, so "
            + "a list that ignored the parameter could not pass this");

        var byGrade = Codes(await ListAsync("search=f6c-sort&sort=Grade"));

        byGrade.Should().Equal(["f6c-sort-b", "f6c-sort-a"], "HRM-1 before SE-1");
    }

    /// <summary>
    /// A tenant with no positions keeps the list it had: no Position, Grade or Reports to columns.
    /// </summary>
    /// <remarks>
    /// D30's scope, restated as a test because this is the slice most likely to break it: the
    /// columns are the feature, and a feature that turned up uninvited on a tenant that has built
    /// no positions would be the product changing a screen nobody asked it to change.
    /// </remarks>
    [Fact]
    public async Task ATenantWithNoPositionsKeepsTodaysColumns()
    {
        using var plain = new BaseTenantFixture();

        await plain.InitializeAsync();

        await ApplyToAsync(plain, "organisation-designer-demo.recipe.json");

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(plain.Administrator, "/Admin/Employees/Index"));

        page.QuerySelectorAll("#employee-list thead th")
            .Select(cell => cell.TextContent.Trim())
            .Should().NotContain("Position", "D30 is scoped to a structure that has positions");

        page.QuerySelector("[data-position-picker]").Should().BeNull(
            "and there is no position to filter by either");

        await plain.DisposeAsync();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static List<string> Codes(AngleSharp.Html.Dom.IHtmlDocument page) =>
        [.. page.QuerySelectorAll(".employee-row")
            .Select(row => row.GetAttribute("data-employee-code") ?? string.Empty)];

    private async Task<AngleSharp.Html.Dom.IHtmlDocument> ListAsync(string query) =>
        new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator, $"/Admin/Employees/Index?{query}"));

    private async Task<AngleSharp.Dom.IElement> RowAsync(string code)
    {
        var row = (await ListAsync($"search={code}"))
            .QuerySelector($".employee-row[data-employee-code='{code}']");

        row.Should().NotBeNull("'{0}' is on the employees list", code);

        return row!;
    }

    private async Task PlaceAsync(string employeeId, string positionCode, string gradeCode) =>
        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            var placed = await services.GetRequiredService<IEmployeeAssignmentService>().PlaceAsync(
                employeeId,
                axis,
                await RecordIdAsync(services, positionCode),
                LongAgo,
                gradeRecordId: await RecordIdAsync(services, gradeCode));

            placed.Succeeded.Should().BeTrue(
                string.Join("; ", placed.Errors.Select(error => error.Message.Value)));
        });

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

    private async Task<string> RecordIdAsync(string code)
    {
        var recordId = string.Empty;

        await InTenantAsSystemAsync(async services => recordId = await RecordIdAsync(services, code));

        return recordId;
    }

    private static async Task<string> RecordIdAsync(IServiceProvider services, string code) =>
        (await services.GetRequiredService<IDimensionService>().GetByCodeAsync(code))!.RecordId;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        InTenantAsSystemAsync(_tenant, work);

    private static Task InTenantAsSystemAsync(
        BaseTenantFixture tenant,
        Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("F6c list test"))
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
