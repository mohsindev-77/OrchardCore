using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The two demo employee recipes applied to a real tenant: the headcounts they seed, the matrix
/// Zenith exists to demonstrate, and the three head shapes ADR-0012 was decided for.
/// </summary>
/// <remarks>
/// <b>Its own tenant, not the shared one.</b> These recipes seed 227 people between them, and the
/// base tenant is shared by most of this project's tests — adding that much data to it would slow
/// every one of them down and give several a different answer about what a query returns.
///
/// <b>And its own recipes, separate from the structure ones</b>, which is why they exist as two
/// files rather than four steps appended to the organisation recipes. A test about the shape of an
/// organisation should not have to load its people to run, and the browser suite builds a tenant
/// per fixture and would pay for them every time. Nothing loads a recipe it did not name.
///
/// The assertions are the figures from the prompt library's 7 October backlog note and the 10a
/// ruling that amended it, so this test is where those figures are actually enforced: a generator
/// that drifted from the table would otherwise produce a plausible organisation nobody checked.
/// </remarks>
public sealed class DemoEmployeeRecipesTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>The two demo axes, named once so the re-run comparison and the counts agree.</summary>
    private static readonly string[] AxisCodes = ["zenith-org", "crescent-org"];

    /// <summary>After every join date and every deployment in both recipes.</summary>
    private static readonly DateOnly Today = new(2026, 1, 1);

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

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- Zenith: the matrix ---------------------------------------------------------------

    [Fact]
    public async Task TheZenithEmployeeRecipeSeedsTheHeadcountsAndTheMatrix()
    {
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-employees.recipe.json");

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            (await employees.ListAsync(take: 1)).Total.Should().Be(
                158, "42 permanent staff and 116 project-hired, which is what the table adds up to");

            var counts = await CountsAsync(services, "zenith-org",
            [
                "zenith-dept-civil", "zenith-dept-electrical", "zenith-dept-mechanical",
                "zenith-dept-finance", "zenith-dept-hr",
                "zenith-team-site-a", "zenith-team-site-b",
                "zenith-team-electrical-works", "zenith-team-civil-works",
            ]);

            // The departments, exactly as the table states them. These include the engineers
            // deployed to a site team, per the 10a ruling: a department's figure is everyone whose
            // home it is, wherever they are working this month.
            counts["zenith-dept-civil"].Should().Be(14);
            counts["zenith-dept-electrical"].Should().Be(8);
            counts["zenith-dept-mechanical"].Should().Be(9);
            counts["zenith-dept-finance"].Should().Be(5);
            counts["zenith-dept-hr"].Should().Be(6);

            // The teams: the engineer deployed to each, plus the people hired for the project.
            counts["zenith-team-site-a"].Should().Be(44, "one site engineer, three foremen and forty labour");
            counts["zenith-team-site-b"].Should().Be(31, "one site engineer and thirty labour");
            counts["zenith-team-electrical-works"].Should().Be(20, "two electrical engineers and eighteen technicians");
            counts["zenith-team-civil-works"].Should().Be(26, "one civil engineer and twenty-five labour");

            // The matrix itself, on one person: a primary at the department they belong to and a
            // secondary at the team they are working on, both on the same axis on the same date.
            // This is the shape the whole organisation exists as a worked example of, and seeding
            // it as one assignment each would produce a chart that looks right and demonstrates
            // nothing.
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var zenith = (await structures.GetByCodeAsync("zenith-org"))!.StructureId;
            var engineer = (await employees.GetByCodeAsync("zenith-civil-01"))!.EmployeeId;

            var rows = await assignments.GetAllEffectiveAsync(engineer, zenith, Today);

            rows.Should().HaveCount(2, "a home department and a site team at once");

            var civil = (await records.GetByCodeAsync("zenith-dept-civil"))!.RecordId;
            var siteA = (await records.GetByCodeAsync("zenith-team-site-a"))!.RecordId;

            rows.Single(row => row.IsPrimary).RecordId.Should().Be(
                civil, "HR rolls up by department, so the department is the primary");

            rows.Single(row => !row.IsPrimary).RecordId.Should().Be(
                siteA, "cost rolls up by project, from the secondary on the same rows");

            rows.Sum(row => row.AllocationPercent).Should().Be(100m);

            // A labourer, by contrast, is project-hired: one assignment, at the team, and no home
            // department at all. Counting them in one would overstate every permanent headcount.
            var labourer = (await employees.GetByCodeAsync("zenith-labour-a-01"))!.EmployeeId;

            var labour = await assignments.GetAllEffectiveAsync(labourer, zenith, Today);

            labour.Should().ContainSingle().Which.RecordId.Should().Be(siteA);
        });
    }

    [Fact]
    public async Task TheZenithEmployeeRecipeSeedsTheThreeHeadShapes()
    {
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-employees.recipe.json");

        await InTenantAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var zenith = (await structures.GetByCodeAsync("zenith-org"))!.StructureId;

            async Task<string> UnitAsync(string code) =>
                (await records.GetByCodeAsync(code))!.RecordId;

            async Task<string> PersonAsync(string code) =>
                (await employees.GetByCodeAsync(code))!.EmployeeId;

            // One person heading two units — the case an allocation-based model could not express
            // without charging them to both departments.
            var manager = await PersonAsync("zenith-civil-01");

            (await assignments.GetHeadAsync(zenith, await UnitAsync("zenith-dept-civil"), Today))!
                .EmployeeId.Should().Be(manager);

            (await assignments.GetHeadAsync(zenith, await UnitAsync("zenith-dept-mechanical"), Today))!
                .EmployeeId.Should().Be(manager);

            // An acting head who is not a member of the unit: Finance's manager covering HR. They
            // are counted by Finance and by nobody else, which is the half ADR-0012 turns on.
            var acting = await PersonAsync("zenith-fin-01");
            var hr = await UnitAsync("zenith-dept-hr");

            (await assignments.GetHeadAsync(zenith, hr, Today))!.EmployeeId.Should().Be(acting);

            var hrMembers = await assignments.GetAllEffectiveAsync(acting, zenith, Today);

            hrMembers.Should().NotContain(row => row.RecordId == hr, "they lead HR and work in Finance");

            // A vacant post: a term that ended with nobody taking over. Vacancy is a fact about the
            // organisation, so the recipe states the ending rather than omitting the unit.
            var electrical = await UnitAsync("zenith-dept-electrical");

            (await assignments.GetHeadAsync(zenith, electrical, new DateOnly(2025, 10, 31)))
                .Should().NotBeNull("somebody led it until the end of October");

            (await assignments.GetHeadAsync(zenith, electrical, Today))
                .Should().BeNull("and nobody has since");

            (await assignments.GetHeadHistoryAsync(zenith, electrical))
                .Should().ContainSingle("the vacancy is the end of a term on record, not an absence of one");
        });
    }

    // ---- Crescent: a branch network -------------------------------------------------------

    [Fact]
    public async Task TheCrescentEmployeeRecipeSeedsItsHeadcountsAndItsLeafBranches()
    {
        await ApplyAsync("organisation-designer-crescent.recipe.json");
        await ApplyAsync("organisation-designer-crescent-employees.recipe.json");

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            (await employees.ListAsync(take: 1)).Total.Should().Be(69);

            var counts = await CountsAsync(services, "crescent-org",
            [
                "crescent-dept-credit-risk", "crescent-dept-treasury", "crescent-dept-hr",
                "crescent-dept-lhr-credit", "crescent-dept-lhr-operations",
                "crescent-dept-khi-credit", "crescent-dept-khi-operations",
                "crescent-branch-gujranwala", "crescent-branch-sialkot", "crescent-branch-hyderabad",
            ]);

            counts["crescent-dept-credit-risk"].Should().Be(6);
            counts["crescent-dept-treasury"].Should().Be(3);
            counts["crescent-dept-hr"].Should().Be(4);
            counts["crescent-dept-lhr-credit"].Should().Be(12);
            counts["crescent-dept-lhr-operations"].Should().Be(9);
            counts["crescent-dept-khi-credit"].Should().Be(10);
            counts["crescent-dept-khi-operations"].Should().Be(7);

            // The branches with no departments are leaves, so their staff attach to the branch
            // itself — manager and all. A branch that had departments could not hold anybody
            // directly, and that difference is the reason these three are in the demo.
            counts["crescent-branch-gujranwala"].Should().Be(7, "a manager and six");
            counts["crescent-branch-sialkot"].Should().Be(5, "a manager and four");
            counts["crescent-branch-hyderabad"].Should().Be(6, "a manager and five");

            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var crescent = (await structures.GetByCodeAsync("crescent-org"))!.StructureId;

            // A branch manager who is both a member and the head, which is the ordinary case —
            // asserted so that the acting head below reads as the exception it is.
            var gujranwala = (await records.GetByCodeAsync("crescent-branch-gujranwala"))!.RecordId;
            var branchManager = (await employees.GetByCodeAsync("crescent-guj-01"))!.EmployeeId;

            (await assignments.GetHeadAsync(crescent, gujranwala, Today))!
                .EmployeeId.Should().Be(branchManager);

            (await assignments.GetAllEffectiveAsync(branchManager, crescent, Today))
                .Should().ContainSingle().Which.RecordId.Should().Be(gujranwala);

            // Covering a branch department from head office, which is what covering looks like.
            var karachiOperations = (await records.GetByCodeAsync("crescent-dept-khi-operations"))!.RecordId;
            var cover = (await employees.GetByCodeAsync("crescent-cr-02"))!.EmployeeId;

            (await assignments.GetHeadAsync(crescent, karachiOperations, Today))!
                .EmployeeId.Should().Be(cover);

            (await assignments.GetAllEffectiveAsync(cover, crescent, Today))
                .Should().NotContain(row => row.RecordId == karachiOperations);

            // And Treasury, empty since June.
            var treasury = (await records.GetByCodeAsync("crescent-dept-treasury"))!.RecordId;

            (await assignments.GetHeadAsync(crescent, treasury, new DateOnly(2025, 6, 30)))
                .Should().NotBeNull();

            (await assignments.GetHeadAsync(crescent, treasury, Today)).Should().BeNull();
        });
    }

    // ---- ADR-0008 --------------------------------------------------------------------------

    /// <summary>
    /// Both recipes re-run clean, writing nothing the second time.
    /// </summary>
    /// <remarks>
    /// The rule matters most for this data. A second run that re-applied the placements would write
    /// a second identical transfer and a second head term, and nothing downstream would complain —
    /// the chart would draw, the headcount would still be right, and the history screen would show
    /// a handover from somebody to themselves.
    /// </remarks>
    [Fact]
    public async Task BothEmployeeRecipesReRunWithNoChange()
    {
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-crescent.recipe.json");
        await ApplyAsync("organisation-designer-zenith-employees.recipe.json");
        await ApplyAsync("organisation-designer-crescent-employees.recipe.json");

        var before = await DescribeAsync();

        await ApplyAsync("organisation-designer-zenith-employees.recipe.json");
        await ApplyAsync("organisation-designer-crescent-employees.recipe.json");

        (await DescribeAsync()).Should().BeEquivalentTo(before, "a second run of the same recipe is a no-op");
    }

    /// <summary>
    /// Everybody, where they sit and what they lead — flat, so a difference names itself.
    /// </summary>
    private async Task<List<string>> DescribeAsync()
    {
        var lines = new List<string>();

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var structures = services.GetRequiredService<IStructureService>();

            var axes = new List<(string Code, string Id)>();

            foreach (var code in AxisCodes)
            {
                var structure = await structures.GetByCodeAsync(code);

                if (structure is not null)
                {
                    axes.Add((code, structure.StructureId));
                }
            }

            var page = await employees.ListAsync(take: 500);

            foreach (var employee in page.Items.OrderBy(person => person.Code, StringComparer.Ordinal))
            {
                foreach (var (axisCode, structureId) in axes)
                {
                    var rows = await assignments.GetAllEffectiveAsync(employee.EmployeeId, structureId, Today);

                    foreach (var row in rows.OrderBy(row => row.RecordId, StringComparer.Ordinal))
                    {
                        lines.Add($"{employee.Code} | {axisCode} | {row.RecordId} | {row.AllocationPercent} | {row.IsPrimary}");
                    }

                    foreach (var headship in (await assignments.GetHeadshipsOfAsync(employee.EmployeeId, Today))
                        .Where(headship => headship.StructureId == structureId)
                        .OrderBy(headship => headship.RecordId, StringComparer.Ordinal))
                    {
                        lines.Add($"{employee.Code} | heads | {axisCode} | {headship.RecordId} | {headship.Range}");
                    }
                }
            }
        });

        return lines;
    }

    // ---- harness ---------------------------------------------------------------------------

    private static async Task<IReadOnlyDictionary<string, int>> CountsAsync(
        IServiceProvider services, string structureCode, string[] unitCodes)
    {
        var structures = services.GetRequiredService<IStructureService>();
        var records = services.GetRequiredService<IDimensionService>();
        var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

        var structureId = (await structures.GetByCodeAsync(structureCode))!.StructureId;
        var idByCode = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var code in unitCodes)
        {
            idByCode[code] = (await records.GetByCodeAsync(code))!.RecordId;
        }

        var counts = await assignments.CountEmployeesAtAsync(structureId, [.. idByCode.Values], Today);

        // Zero rather than absent, so an assertion on a unit nobody is at fails with the number it
        // expected instead of a missing-key exception.
        return idByCode.ToDictionary(
            entry => entry.Key, entry => counts.GetValueOrDefault(entry.Value), StringComparer.Ordinal);
    }

    private async Task ApplyAsync(string fileName)
    {
        var path = Path.Combine(RecipesFolder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' ships with the module", fileName);

        await InTenantAsync(async services =>
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

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("demo employee recipes test"))
            {
                await work(services);
            }
        });
}
