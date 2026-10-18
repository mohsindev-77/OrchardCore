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
/// Slice F6c's small follow-ups: the dated "Change reports to…", two wordings that were true and
/// unactionable, the picker's ordering, and the grouped list's Add.
/// </summary>
public sealed class F6cFollowUpsTenantTests : IAsyncLifetime, IDisposable
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

    // ---- 3a: the dated change of a post's reporting line ---------------------------------------

    /// <summary>
    /// A position's page offers "Change reports to…", and it opens the designer's DATED move on
    /// the reporting axis with the post already chosen.
    /// </summary>
    /// <remarks>
    /// <b>Dated is the whole requirement.</b> ADR-0005: every move in this engine is a dated pair
    /// of rows, and the designer's Move screen is the one place that takes the date and writes
    /// them. A control on this page that simply set a new parent would be the one place in the
    /// product where a reporting line changed without a date — and "who did this post report to
    /// last March" would quietly start returning today's answer.
    /// </remarks>
    [Fact]
    public async Task ThePositionPageChangesItsReportingLineThroughTheDatedMove()
    {
        var engineer = await RecordIdAsync("zenith-pos-se");

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator, $"/Admin/Dimensions/Records/Details/{engineer}"));

        var action = page.QuerySelector("#value-change-reports-to");

        action.Should().NotBeNull("a post's page is where somebody asks what it answers to");

        var href = action!.GetAttribute("href") ?? string.Empty;

        href.Should().Contain("/Designer/Move", "the designer's dated move, never an undated reparent")
            .And.Contain(engineer, "with this post already chosen");

        // ON THE REPORTING AXIS, not the organisation one. Moving a post between departments and
        // moving it under a different manager are two different changes on two different charts —
        // ADR-0028 — and a button that opened the wrong one would silently transfer the post.
        var reporting = string.Empty;

        await InTenantAsSystemAsync(async services =>
            reporting = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-reporting"))!.StructureId);

        href.Should().Contain(reporting, "the reporting line is the axis this question is about");
    }

    // ---- 3b: naming the empty chairs ------------------------------------------------------------

    /// <summary>
    /// With nothing held above, the row names the vacant posts rather than counting them.
    /// </summary>
    /// <remarks>
    /// "No one above holds a post yet" was true and unactionable: a reader could not tell whether
    /// one chair was empty or three, or which. Naming them turns the sentence into a list of posts
    /// somebody can go and fill.
    /// </remarks>
    [Fact]
    public async Task NothingHeldAboveNamesTheVacantPostsRatherThanCountingThem()
    {
        // Nobody is placed anywhere on this tenant, so a Software Engineer has Manager Technology,
        // Director Technology and the CEO empty above them — three chairs, all vacant.
        var page = new HtmlParser().ParseDocument(
            await PostToCreateAsync(new Dictionary<string, string>
            {
                ["JoinDate"] = Today.ToIso(),
                ["PositionRecordId"] = await RecordIdAsync("zenith-pos-se"),
                ["IsRefresh"] = "true",
            }));

        var said = page.QuerySelector("#line-manager-nobody-above")!.TextContent;

        said.Should().Contain("No acting manager", "which is what is actually missing");

        said.Should().Contain("Manager Technology", "the chair directly above, named first")
            .And.Contain("Director Technology")
            .And.Contain("CEO", "and all the way to the top, because all of them are empty");
    }

    // ---- 3c: the row does not claim a line manager the record is asking about -------------------

    /// <summary>
    /// With one holder above and a stale stored choice, the row names the post and the holder but
    /// does not call them the line manager.
    /// </summary>
    /// <remarks>
    /// Both facts stay — the post has a holder, and that is worth showing — but the
    /// <em>"(line manager)"</em> claim contradicted the sentence directly below it, which says the
    /// record needs a new one. Two statements one line apart, disagreeing.
    /// </remarks>
    [Fact]
    public async Task AStaleChoiceStopsTheRowClaimingTheHolderIsTheLineManager()
    {
        var first = await GivenAnEmployeeAsync("f6c-row-a", "First Director");
        var second = await GivenAnEmployeeAsync("f6c-row-b", "Second Director");

        // Two holders of Director Technology, so the choice below is a real one.
        await PlaceAsync(first, "zenith-pos-dir-tech", "zenith-grade-dir-1");
        await PlaceAsync(second, "zenith-pos-dir-tech", "zenith-grade-dir-1");

        var manager = await GivenAnEmployeeAsync("f6c-row-hr", "Reem Adel");

        await PlaceAsync(manager, "zenith-pos-hr-manager", "zenith-grade-hrm-1");

        await InTenantAsSystemAsync(async services =>
            (await services.GetRequiredService<IEmployeeService>().SetLineManagerAsync(
                manager, first, StoredLineManagerKind.Chosen))
                .Succeeded.Should().BeTrue());

        // The chosen one leaves the post, so one holder is left and the stored choice is stale.
        await EndOnTheOrgAxisAsync(first, Today.AddDays(-3));

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator, $"/Admin/Employees/Edit/{manager}"));

        page.QuerySelector("#line-manager-needs-choice").Should().NotBeNull(
            "the stored choice has stopped being true");

        var row = page.QuerySelector("#job-reports-to-one")!.TextContent;

        row.Should().Contain("Director Technology", "the post is still a fact")
            .And.Contain("Second Director", "and so is who holds it");

        row.Should().NotContain(
            "line manager",
            "claiming the holder IS the line manager contradicts the sentence directly below, "
            + "which says the record needs a new one");
    }

    // ---- 3d: the picker offers the same department first ----------------------------------------

    /// <summary>
    /// The Reports-to picker offers the posts in the same department before the rest.
    /// </summary>
    /// <remarks>
    /// <b>Fetched first, not re-sorted.</b> Re-ordering the page would only move the
    /// same-department posts that happened to land in the first twenty, which is the half of the
    /// problem that did not need solving — so the assertion is on the order of the first page,
    /// which is what a reader actually sees before typing.
    /// </remarks>
    [Fact]
    public async Task TheReportsToPickerOffersTheSameDepartmentFirst()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var positions = services.GetRequiredService<IPositionService>();

            // Electrical holds Director Technology, Manager Technology, the engineers; HR holds
            // the CEO and HR Manager. Asking near one must put its own posts at the top.
            var electrical = await RecordIdAsync(services, "zenith-dept-electrical");

            var near = await positions.SearchSelectableAsync(
                searchText: null, Today, take: 4, nearUnitRecordId: electrical);

            near.Items.Should().NotBeEmpty();

            var hr = await RecordIdAsync(services, "zenith-dept-hr");

            // Every one of the first page sits in the department asked about, which an alphabetical
            // page across the company does not do: "CEO" sorts first and is in HR.
            near.Items.Select(option => option.UnitPath)
                .Should().OnlyContain(path => path.Contains("Electrical", StringComparison.Ordinal),
                    "the department's own posts come first");

            var nearHr = await positions.SearchSelectableAsync(
                searchText: null, Today, take: 2, nearUnitRecordId: hr);

            nearHr.Items.Select(option => option.UnitPath)
                .Should().OnlyContain(path => path.Contains("HR", StringComparison.Ordinal),
                    "and asking near a different department gives a different first page");
        });
    }

    // ---- 3e: Add on a grouped list asks which organisation --------------------------------------

    /// <summary>
    /// Add on a grouped "All &lt;plural&gt;" list asks which organisation before opening the form.
    /// </summary>
    /// <remarks>
    /// The group holds one entry per dimension type sharing a name — the base recipe's Position and
    /// Zenith's — so the list is about no single type and Add used to post an empty id and answer
    /// 404 from a button the screen had just offered.
    /// </remarks>
    [Fact]
    public async Task AddOnAGroupedListAsksWhichOrganisationFirst()
    {
        var groupKey = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = await services.GetRequiredService<IDimensionTypeService>()
                .ListAsync();

            var positions = types
                .Where(type => type.StandardRole == WorkMate.Dimensions.Models.DimensionTypeRole.Position)
                .ToList();

            positions.Count.Should().BeGreaterThan(
                1, "this tenant has base's Position type and Zenith's, which is what makes a group");

            groupKey = DimensionMenuComposition.KeyOf(positions[0]);
        });

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator,
                $"/Admin/Dimensions/Records/Create?group={Uri.EscapeDataString(groupKey)}"));

        page.QuerySelector("#choose-organisation-choices").Should().NotBeNull(
            "a 404 from a button the screen just offered is the defect this closes");

        var choices = page.QuerySelectorAll(".choose-organisation-choice")
            .Select(choice => choice.GetAttribute("data-dimension-type-id"))
            .ToList();

        choices.Should().HaveCountGreaterThan(1, "one per type in the group");

        choices.Should().OnlyContain(
            id => !string.IsNullOrEmpty(id),
            "and each goes to the form knowing what it is creating, which is the whole point");
    }

    /// <summary>
    /// With one type in the group, Add still goes straight to the form.
    /// </summary>
    [Fact]
    public async Task AddGoesStraightToTheFormWhenTheGroupHoldsOneType()
    {
        var groupKey = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            // ITS OWN TYPE, with a name nothing else in the tenant shares. Every shipped dimension
            // is grouped with a namesake somewhere — base's Grade and Zenith's are one entry, which
            // is D23 B working as intended — so a test for the one-type case has to make one.
            var types = services.GetRequiredService<IDimensionTypeService>();

            var made = await types.CreateAsync(
                "f6c-sole-type",
                new BilingualText("F6c Sole Dimension", "F6c Sole Dimension"),
                [],
                allowsSelfNesting: false);

            made.Succeeded.Should().BeTrue(
                string.Join("; ", made.Errors.Select(error => error.Message.Value)));

            var all = await types.ListAsync();

            var sole = all.Single(type =>
                string.Equals(type.Code, "f6c-sole-type", StringComparison.Ordinal));

            all.Count(type => string.Equals(
                    DimensionMenuComposition.KeyOf(type),
                    DimensionMenuComposition.KeyOf(sole),
                    StringComparison.Ordinal))
                .Should().Be(1, "nothing else in this tenant is called that");

            groupKey = DimensionMenuComposition.KeyOf(sole);
        });

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator,
                $"/Admin/Dimensions/Records/Create?group={Uri.EscapeDataString(groupKey)}"));

        page.QuerySelector("#choose-organisation-choices").Should().BeNull(
            "there is nothing to choose between, so asking would be a step for its own sake");

        page.QuerySelector("form").Should().NotBeNull("it is the Create form, unchanged");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private Task<string> PostToCreateAsync(IDictionary<string, string> fields) =>
        BaseTenantFixture.PostFormAsync(_tenant.Administrator, "/Admin/Employees/Create", fields);

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

    private async Task EndOnTheOrgAxisAsync(string employeeId, DateOnly lastDay) =>
        await InTenantAsSystemAsync(async services =>
        {
            var axis = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            (await services.GetRequiredService<IEmployeeAssignmentService>()
                .EndAsync(employeeId, axis, lastDay))
                .Succeeded.Should().BeTrue();
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
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("F6c follow-ups test"))
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
