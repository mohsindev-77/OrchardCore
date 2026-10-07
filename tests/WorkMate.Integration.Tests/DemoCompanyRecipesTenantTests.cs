using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The two demo company recipes, applied to a real tenant: the tree each one builds, that each
/// re-runs clean, and that the shapes their containment forbids are actually refused.
/// </summary>
/// <remarks>
/// These are the worked examples ADR-0010 exists for, so the test asserts the two properties a
/// reader would otherwise have to take on trust. The tree, parent by parent — not a count, which
/// passes just as well when every unit is hanging off the wrong parent. And the refusals, because
/// a containment map that permits what it should and also permits everything else is the defect
/// level skipping had, and nothing but an attempted placement can tell the two apart.
///
/// Both recipes are shipped files read off disk rather than JSON written here. A test that carries
/// its own copy of a recipe proves that copy applies, which is not the question.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DemoCompanyRecipesTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DemoCompanyRecipesTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private static readonly DateOnly Today = new(2026, 10, 7);

    /// <summary>The three branches the recipe deliberately leaves without departments.</summary>
    private static readonly string[] BranchesWithNoDepartments =
    [
        "crescent-branch-gujranwala",
        "crescent-branch-sialkot",
        "crescent-branch-hyderabad",
    ];

    // ---- Zenith: two kinds of unit at one level -------------------------------------------

    [Fact]
    public async Task TheZenithRecipeBuildsItsTreeAndReRunsWithNoChange()
    {
        await ApplyAsync("organisation-designer-zenith.recipe.json");

        await InTenantAsync(async services =>
        {
            await AssertTreeAsync(services, "zenith-org", 14,
            [
                // Division            Child
                (null, "zenith-div-engineering"),
                (null, "zenith-div-projects"),
                (null, "zenith-div-corporate"),

                ("zenith-div-engineering", "zenith-dept-civil"),
                ("zenith-div-engineering", "zenith-dept-electrical"),
                ("zenith-div-engineering", "zenith-dept-mechanical"),

                ("zenith-div-projects", "zenith-proj-motorway"),
                ("zenith-div-projects", "zenith-proj-grid"),

                ("zenith-proj-motorway", "zenith-team-site-a"),
                ("zenith-proj-motorway", "zenith-team-site-b"),
                ("zenith-proj-grid", "zenith-team-electrical-works"),
                ("zenith-proj-grid", "zenith-team-civil-works"),

                ("zenith-div-corporate", "zenith-dept-finance"),
                ("zenith-div-corporate", "zenith-dept-hr"),
            ]);

            // The attribute values the Project schema declares, which ADR-0011's addendum made
            // carriable. A recipe that creates the project and loses its cost code has produced a
            // tree that looks right and describes nothing.
            var motorway = await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync("zenith-proj-motorway");

            var values = await services.GetRequiredService<IDimensionService>()
                .GetAttributeValuesAsync(motorway!.RecordId);

            values.Should().Contain(value => value.Name == "CostCode" && value.Value == "PRJ-2024-017");
            values.Should().Contain(value => value.Name == "StartDate" && value.Value == "2024-03-01");
            values.Should().Contain(value => value.Name == "SiteLocation" && value.Value == "M4 Interchange, Lahore");
        });

        // ADR-0008: an identical row is skipped, so the same file applies twice without error and
        // without writing anything. A recipe that is not re-runnable is a recipe nobody can fix
        // and re-apply after a failure partway through.
        await ApplyAsync("organisation-designer-zenith.recipe.json");

        await InTenantAsync(services => AssertTreeAsync(services, "zenith-org", 14, []));
    }

    [Fact]
    public async Task ZenithRefusesATeamDirectlyUnderADivision()
    {
        await ApplyAsync("organisation-designer-zenith.recipe.json");

        await InTenantAsync(async services =>
        {
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var structure = await StructureIdAsync(services, "zenith-org");
            var team = (await records.GetByCodeAsync("zenith-team-site-a"))!.RecordId;
            var division = (await records.GetByCodeAsync("zenith-div-projects"))!.RecordId;

            // The placement that turning on level skipping would have allowed, and the reason
            // ADR-0010 replaced it: a Team belongs to a Project, never straight to a Division.
            var refused = await graph.MoveAsync(structure, team, division, Today);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });
    }

    // ---- Crescent: a ragged tree -----------------------------------------------------------

    [Fact]
    public async Task TheCrescentRecipeBuildsItsTreeAndReRunsWithNoChange()
    {
        await ApplyAsync("organisation-designer-crescent.recipe.json");

        await InTenantAsync(async services =>
        {
            await AssertTreeAsync(services, "crescent-org", 16,
            [
                (null, "crescent-div-head-office"),
                (null, "crescent-div-branch-network"),

                // Departments directly under a Division, two levels deep.
                ("crescent-div-head-office", "crescent-dept-credit-risk"),
                ("crescent-div-head-office", "crescent-dept-treasury"),
                ("crescent-div-head-office", "crescent-dept-hr"),

                ("crescent-div-branch-network", "crescent-region-punjab"),
                ("crescent-div-branch-network", "crescent-region-sindh"),

                ("crescent-region-punjab", "crescent-branch-lahore-main"),
                ("crescent-region-punjab", "crescent-branch-gujranwala"),
                ("crescent-region-punjab", "crescent-branch-sialkot"),
                ("crescent-region-sindh", "crescent-branch-karachi-main"),
                ("crescent-region-sindh", "crescent-branch-hyderabad"),

                // ... and the same Department type again, four levels deep.
                ("crescent-branch-lahore-main", "crescent-dept-lhr-credit"),
                ("crescent-branch-lahore-main", "crescent-dept-lhr-operations"),
                ("crescent-branch-karachi-main", "crescent-dept-khi-credit"),
                ("crescent-branch-karachi-main", "crescent-dept-khi-operations"),
            ]);

            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();
            var structure = await StructureIdAsync(services, "crescent-org");

            // Ragged: the same type at two depths on one axis, which is the whole point of this
            // example and was inexpressible before ADR-0010.
            var atHeadOffice = (await records.GetByCodeAsync("crescent-dept-credit-risk"))!.RecordId;
            var atBranch = (await records.GetByCodeAsync("crescent-dept-lhr-credit"))!.RecordId;

            (await graph.GetAncestorsAsync(structure, atHeadOffice, Today)).Should().HaveCount(
                1, "Head Office > Credit Risk");

            (await graph.GetAncestorsAsync(structure, atBranch, Today)).Should().HaveCount(
                3, "Branch Network > Punjab Region > Lahore Main Branch > Credit");

            // Two branches with nothing under them, beside siblings that go one level deeper.
            foreach (var leaf in BranchesWithNoDepartments)
            {
                var record = (await records.GetByCodeAsync(leaf))!.RecordId;

                (await graph.GetChildrenAsync(structure, record, Today))
                    .Should().BeEmpty("{0} has no departments", leaf);
            }

            var lahore = (await records.GetByCodeAsync("crescent-branch-lahore-main"))!.RecordId;
            var values = await records.GetAttributeValuesAsync(lahore);

            values.Should().Contain(value => value.Name == "BranchCode" && value.Value == "LHR-001");
            values.Should().Contain(value => value.Name == "CostCentre" && value.Value == "CC-1100");
            values.Should().Contain(value => value.Name == "Location" && value.Value == "Gulberg III, Lahore");
        });

        await ApplyAsync("organisation-designer-crescent.recipe.json");

        await InTenantAsync(services => AssertTreeAsync(services, "crescent-org", 16, []));
    }

    [Fact]
    public async Task CrescentRefusesABranchDirectlyUnderHeadOffice()
    {
        await ApplyAsync("organisation-designer-crescent.recipe.json");

        await InTenantAsync(async services =>
        {
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var structure = await StructureIdAsync(services, "crescent-org");
            var branch = (await records.GetByCodeAsync("crescent-branch-sialkot"))!.RecordId;
            var headOffice = (await records.GetByCodeAsync("crescent-div-head-office"))!.RecordId;

            // Head Office holds Departments; its sibling holds Regions. A Division permitting one
            // does not permit the other's contents, which is the asymmetry a chain cannot express.
            var refused = await graph.MoveAsync(structure, branch, headOffice, Today);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });
    }

    /// <summary>
    /// Both recipes, and the original demo, on one tenant at once and in any order.
    /// </summary>
    /// <remarks>
    /// The property the prefixes exist for. Each recipe defines its own Division, Department and
    /// so on under its own code, so three organisations coexist without one's types becoming
    /// another's — which is what a shared code would have made them, permanently and with no way
    /// back once a customer had data under either.
    /// </remarks>
    [Fact]
    public async Task BothRecipesCoexistOnOneTenantInEitherOrder()
    {
        // Crescent first, then Zenith. The reverse order is covered by the two tests above, which
        // apply them independently and in whichever order the runner picks.
        await ApplyAsync("organisation-designer-crescent.recipe.json");
        await ApplyAsync("organisation-designer-zenith.recipe.json");

        await InTenantAsync(async services =>
        {
            var structures = (await services.GetRequiredService<IStructureService>().ListAsync())
                .Select(structure => structure.Code)
                .ToList();

            structures.Should().Contain(["crescent-org", "zenith-org"]);

            // Each keeps its own types rather than sharing one set. A shared "department" code
            // would have made one recipe's Department the other's, permanently, with no way back
            // once a customer had records under either.
            var types = (await services.GetRequiredService<IDimensionTypeService>()
                    .ListAsync(includeRetired: true))
                .Select(type => type.Code)
                .ToList();

            types.Should().Contain(["crescent-department", "zenith-department"]);

            // And neither has disturbed the demo organisation, wherever in this collection's run
            // order it was seeded. Asserted only when it is there: this test deliberately does not
            // apply the demo recipe itself — the tenant is shared, and applying it here would turn
            // another test's first run into a skip and break assertions that are rightly about a
            // first run. Its own re-run safety is DimensionsRecipeStepsTenantTests' subject.
            if (structures.Contains("demo-org"))
            {
                types.Should().Contain("demo-department");
            }
        });
    }

    // ---- scaffolding --------------------------------------------------------------------

    /// <summary>
    /// Asserts every parent-child pair, that nothing else is on the axis, and the unit count.
    /// </summary>
    /// <remarks>
    /// Both directions. The expected pairs must all be present, and the axis must hold no pair
    /// that is not expected — a tree with an extra unit hanging off the wrong parent satisfies
    /// "every expected pair exists" perfectly well. The count is a third check over the same data
    /// and is cheap; it is what catches a record created and never placed.
    ///
    /// An empty expectation list means "count only", which is what the re-run assertion needs:
    /// the tree was already checked on the first pass, and what the second pass is about is that
    /// nothing was added.
    /// </remarks>
    private static async Task AssertTreeAsync(
        IServiceProvider services,
        string structureCode,
        int expectedUnits,
        IReadOnlyList<(string? Parent, string Child)> expected)
    {
        var graph = services.GetRequiredService<IDimensionGraphService>();
        var records = services.GetRequiredService<IDimensionService>();
        var structureId = await StructureIdAsync(services, structureCode);

        var roots = await graph.GetRootsAsync(structureId, Today);
        var actual = new List<(string? Parent, string Child)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        async Task WalkAsync(string? parentCode, IReadOnlyList<DimensionNodeRef> nodes)
        {
            foreach (var node in nodes)
            {
                actual.Add((parentCode, node.Code));
                seen.Add(node.Code);

                await WalkAsync(node.Code, await graph.GetChildrenAsync(structureId, node.RecordId, Today));
            }
        }

        await WalkAsync(null, roots);

        seen.Should().HaveCount(expectedUnits, "the recipe builds exactly {0} units", expectedUnits);

        // Nothing stranded: a unit created but never placed never appears in the walk above.
        (await graph.GetUnplacedAsync(structureId, Today))
            .Should().BeEmpty("every unit the recipe creates is placed");

        if (expected.Count == 0)
        {
            return;
        }

        actual.Should().BeEquivalentTo(expected, "the tree is exactly the one the recipe describes");

        // And every code resolves, which is what proves the walk was not comparing nulls.
        foreach (var (_, child) in expected)
        {
            (await records.GetByCodeAsync(child)).Should().NotBeNull("'{0}' exists", child);
        }
    }

    /// <summary>
    /// Siblings come out in the order their recipe lists them, not in alphabetical order.
    /// </summary>
    /// <remarks>
    /// Every group asserted here is one the recipe deliberately lists out of alphabetical order,
    /// so a pass cannot be an accident of the names: Engineering, Projects, Corporate is the order
    /// the company puts its divisions in; Motorway Interchange precedes Grid Station because that
    /// is the order the projects started in; Lahore, Gujranwala, Sialkot is by size, which is how
    /// a bank lists its branches.
    ///
    /// Both recipes are applied, in that order, because that is also what proves the ordering is
    /// per sibling group rather than global: Crescent's records are created after all of Zenith's
    /// and carry higher sort orders, and its roots must still come out in its own order.
    /// </remarks>
    [Fact]
    public async Task SiblingsComeOutInTheOrderTheirRecipeListsThem()
    {
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-crescent.recipe.json");

        await InTenantAsync(async services =>
        {
            (await ChildCodesAsync(services, "zenith-org", null)).Should().Equal(
                "zenith-div-engineering", "zenith-div-projects", "zenith-div-corporate");

            (await ChildCodesAsync(services, "zenith-org", "zenith-div-projects")).Should().Equal(
                "zenith-proj-motorway", "zenith-proj-grid");

            (await ChildCodesAsync(services, "zenith-org", "zenith-proj-grid")).Should().Equal(
                "zenith-team-electrical-works", "zenith-team-civil-works");

            (await ChildCodesAsync(services, "crescent-org", null)).Should().Equal(
                "crescent-div-head-office", "crescent-div-branch-network");

            (await ChildCodesAsync(services, "crescent-org", "crescent-region-punjab")).Should().Equal(
                "crescent-branch-lahore-main", "crescent-branch-gujranwala", "crescent-branch-sialkot");
        });
    }

    /// <summary>The codes directly under one unit, in the order the graph hands them back.</summary>
    private static async Task<IReadOnlyList<string>> ChildCodesAsync(
        IServiceProvider services, string structureCode, string? parentCode)
    {
        var graph = services.GetRequiredService<IDimensionGraphService>();
        var records = services.GetRequiredService<IDimensionService>();
        var structureId = await StructureIdAsync(services, structureCode);

        if (parentCode is null)
        {
            return [.. (await graph.GetRootsAsync(structureId, Today)).Select(node => node.Code)];
        }

        var parent = await records.GetByCodeAsync(parentCode);

        parent.Should().NotBeNull("'{0}' exists", parentCode);

        return [.. (await graph.GetChildrenAsync(structureId, parent!.RecordId, Today)).Select(node => node.Code)];
    }

    private static async Task<string> StructureIdAsync(IServiceProvider services, string code)
    {
        var structure = await services.GetRequiredService<IStructureService>().GetByCodeAsync(code);

        structure.Should().NotBeNull("the recipe's structure '{0}' was created", code);

        return structure!.StructureId;
    }

    /// <summary>
    /// Applies a recipe shipped in the module, read from the module's own folder.
    /// </summary>
    /// <remarks>
    /// Off disk, from the source tree, so the test exercises the file that actually ships rather
    /// than a copy of it that cannot drift. The build copies these into the host's content root;
    /// reading the source avoids depending on that copy having happened.
    /// </remarks>
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

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("demo recipe test"))
            {
                await work(services);
            }
        });
}
