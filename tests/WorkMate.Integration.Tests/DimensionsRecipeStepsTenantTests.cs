using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The <c>dimension-types</c>, <c>structures</c> and <c>dimension-records</c> recipe steps,
/// exercised the same way an operator applying a recipe would: through
/// <see cref="IRecipeExecutor"/> against a real recipe file, not by calling the step classes
/// directly.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionsRecipeStepsTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DimensionsRecipeStepsTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    /// <summary>
    /// Runs a recipe through <see cref="IRecipeExecutor"/> the way an operator would, and reports
    /// what happened without throwing: <see cref="IRecipeExecutor.ExecuteAsync"/> propagates a
    /// failing step's <see cref="RecipeExecutionException"/> rather than returning a result object,
    /// so this is the one place that exception is caught and turned into something a test can
    /// assert against either way.
    /// </summary>
    /// <remarks>
    /// The errors returned come from the caught exception's own <c>Message</c>, not
    /// <c>StepResult.Errors</c>: Orchard's <c>RecipeExecutor</c> replaces
    /// <c>RecipeStepResult.Errors</c> with the generic "Unexpected error occurred while executing
    /// the '{0}' step." for every step failure, regardless of cause — see
    /// <c>RecipeStepFailures</c>'s remarks — but preserves the original exception's own message,
    /// which is where this module's specific, useful detail actually survives.
    /// </remarks>
    private static async Task<(bool Succeeded, IReadOnlyList<string> Errors)> ExecuteRecipeAsync(
        IServiceProvider services, string json)
    {
        var directory = Directory.CreateTempSubdirectory("workmate-recipe-test-");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "recipe.json"), json);

            var fileProvider = new PhysicalFileProvider(directory.FullName);
            var descriptor = new RecipeDescriptor
            {
                Name = "test-recipe",
                BasePath = string.Empty,
                FileProvider = fileProvider,
                RecipeFileInfo = fileProvider.GetFileInfo("recipe.json"),
                RequireNewScope = false,
            };

            var executor = services.GetRequiredService<IRecipeExecutor>();

            try
            {
                await executor.ExecuteAsync(
                    Guid.NewGuid().ToString("n"), descriptor, new Dictionary<string, object>(), CancellationToken.None);

                return (true, []);
            }
            catch (RecipeExecutionException exception)
            {
                return (false, [exception.Message]);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ARecipeCreatesTypesStructureAndPlacedAndUnplacedRecords()
    {
        const string json = """
        {
            "steps": [
                {
                    "name": "dimension-types",
                    "types": [
                        { "code": "recipe-division", "nameEn": "Recipe Division", "nameAr": "قسم", "allowsSelfNesting": false },
                        { "code": "recipe-department", "nameEn": "Recipe Department", "nameAr": "إدارة", "allowsSelfNesting": false }
                    ]
                },
                {
                    "name": "structures",
                    "structures": [
                        {
                            "code": "recipe-structure",
                            "nameEn": "Recipe Structure",
                            "nameAr": "هيكل",
                            "levelTypeCodes": [ "recipe-division", "recipe-department" ],
                            "allowSkipLevel": false,
                            "isStrict": true,
                            "isPrimaryOrganisation": false
                        }
                    ]
                },
                {
                    "name": "dimension-records",
                    "records": [
                        {
                            "code": "recipe-div-1",
                            "typeCode": "recipe-division",
                            "nameEn": "Recipe Division One",
                            "nameAr": "واحد",
                            "effectiveFrom": "2024-01-01",
                            "placements": [
                                { "structureCode": "recipe-structure", "parentCode": null, "effectiveFrom": "2024-01-01" }
                            ]
                        },
                        {
                            "code": "recipe-dept-1",
                            "typeCode": "recipe-department",
                            "nameEn": "Recipe Department One",
                            "nameAr": "واحد",
                            "effectiveFrom": "2024-01-01",
                            "placements": [
                                { "structureCode": "recipe-structure", "parentCode": "recipe-div-1", "effectiveFrom": "2024-01-01" }
                            ]
                        },
                        {
                            "code": "recipe-dept-orphan",
                            "typeCode": "recipe-department",
                            "nameEn": "Recipe Orphan Department",
                            "nameAr": "يتيم",
                            "effectiveFrom": "2024-01-01",
                            "placements": []
                        }
                    ]
                }
            ]
        }
        """;

        await InTenantAsSystemAsync(async services =>
        {
            var result = await ExecuteRecipeAsync(services, json);

            result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));

            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var structure = await structures.GetByCodeAsync("recipe-structure");
            structure.Should().NotBeNull();
            structure!.Levels.Should().HaveCount(2);

            var division = await records.GetByCodeAsync("recipe-div-1");
            var department = await records.GetByCodeAsync("recipe-dept-1");
            var orphan = await records.GetByCodeAsync("recipe-dept-orphan");

            division.Should().NotBeNull();
            department.Should().NotBeNull();
            orphan.Should().NotBeNull();

            var roots = await graph.GetRootsAsync(structure.StructureId);
            roots.Should().ContainSingle(node => node.RecordId == division!.RecordId);

            var children = await graph.GetChildrenAsync(structure.StructureId, division!.RecordId);
            children.Should().ContainSingle(node => node.RecordId == department!.RecordId);

            var unplaced = await graph.GetUnplacedAsync(structure.StructureId);
            unplaced.Should().ContainSingle(node => node.RecordId == orphan!.RecordId);
        });
    }

    /// <summary>
    /// Each recipe step is individually all-or-nothing (validated as one batch, nothing written on
    /// a failure within it — see
    /// <see cref="TwoTypesSharingACodeInOneStepFailTheWholeStepAndNothingIsWritten"/>), but the
    /// recipe as a whole is not: when <c>dimension-records</c> fails after <c>dimension-types</c>
    /// and <c>structures</c> already committed, their writes stay. Cross-step atomicity — wrapping
    /// the whole recipe in one transaction so nothing commits until every step succeeds — was
    /// considered for this and rejected; see the README's note on recipe re-run behaviour for why.
    /// The decision taken instead is tested here end to end: the leftover type and structure from
    /// the first, failed run are recognised as already matching once the records step's own
    /// reference is fixed, so the second run completes successfully without recreating or erroring
    /// on them.
    /// </summary>
    [Fact]
    public async Task LeftoversFromAFailedRunDoNotBlockAFixedReRun()
    {
        const string brokenRecordsStep = """
            {
                "name": "dimension-records",
                "records": [
                    {
                        "code": "cross-div-1",
                        "typeCode": "cross-division",
                        "nameEn": "Cross Division One",
                        "nameAr": "واحد",
                        "effectiveFrom": "2024-01-01",
                        "placements": [
                            { "structureCode": "does-not-exist", "parentCode": null, "effectiveFrom": "2024-01-01" }
                        ]
                    }
                ]
            }
            """;

        const string fixedRecordsStep = """
            {
                "name": "dimension-records",
                "records": [
                    {
                        "code": "cross-div-1",
                        "typeCode": "cross-division",
                        "nameEn": "Cross Division One",
                        "nameAr": "واحد",
                        "effectiveFrom": "2024-01-01",
                        "placements": [
                            { "structureCode": "cross-structure", "parentCode": null, "effectiveFrom": "2024-01-01" }
                        ]
                    }
                ]
            }
            """;

        string TypesAndStructureSteps() => """
            {
                "name": "dimension-types",
                "types": [
                    { "code": "cross-division", "nameEn": "Cross Division", "nameAr": "قسم", "allowsSelfNesting": false }
                ]
            },
            {
                "name": "structures",
                "structures": [
                    {
                        "code": "cross-structure",
                        "nameEn": "Cross Structure",
                        "nameAr": "هيكل",
                        "levelTypeCodes": [ "cross-division" ],
                        "allowSkipLevel": false,
                        "isStrict": true,
                        "isPrimaryOrganisation": false
                    }
                ]
            },
            """;

        var brokenRecipe = $$"""{ "steps": [ {{TypesAndStructureSteps()}} {{brokenRecordsStep}} ] }""";
        var fixedRecipe = $$"""{ "steps": [ {{TypesAndStructureSteps()}} {{fixedRecordsStep}} ] }""";

        await InTenantAsSystemAsync(async services =>
        {
            var firstRun = await ExecuteRecipeAsync(services, brokenRecipe);
            firstRun.Succeeded.Should().BeFalse("the record names a structure that does not exist");

            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            (await types.GetByCodeAsync("cross-division")).Should().NotBeNull(
                "dimension-types committed before dimension-records failed, and that is not undone");
            (await structures.GetByCodeAsync("cross-structure")).Should().NotBeNull(
                "structures committed before dimension-records failed, and that is not undone");
            (await records.GetByCodeAsync("cross-div-1")).Should().BeNull(
                "dimension-records validated its own batch first and wrote nothing from itself");

            var secondRun = await ExecuteRecipeAsync(services, fixedRecipe);
            secondRun.Succeeded.Should().BeTrue(
                "dimension-types and structures recognise the leftovers as already matching and skip them; " +
                string.Join("; ", secondRun.Errors));

            (await records.GetByCodeAsync("cross-div-1")).Should().NotBeNull(
                "the fixed records step now creates and places the record cleanly");
        });
    }

    /// <summary>
    /// The one test that runs the real, shipped demo recipe file end to end, through every layer
    /// at once: it is the only test in this class allowed to touch the demo's fixed codes, because
    /// two tests each applying the same fixed-code recipe to this class's shared tenant would
    /// collide with each other.
    ///
    /// Covers, in sequence: the real path <c>/Admin/Recipes</c> uses (a POST to
    /// <c>/Admin/Recipes/Execute</c>, with the antiforgery token the page itself issues), on a
    /// tenant that already has a dimension type and a structure created through the real admin
    /// HTTP screens beforehand — in their own separate requests, the way a person testing in a
    /// browser actually produces that state, which is the reported bug's precondition; the demo's
    /// own content (roots, levels, the deliberately unplaced record); and running it a second time
    /// without cleaning up first, which must now succeed and change nothing — every row already
    /// matches what is in the tenant, so every step skips rather than errors or duplicates.
    ///
    /// This combination applies cleanly on the first run: "other data already exists" alone does
    /// not reproduce the originally reported failure, which is why
    /// <see cref="LeftoversFromAFailedRunDoNotBlockAFixedReRun"/> and file logging
    /// (<c>UseNLogHost</c> in WorkMate.Web's Program.cs) are what is left to catch the real cause
    /// if and when it recurs.
    /// </summary>
    [Fact]
    public async Task TheDemoRecipeAppliesThroughTheRealAdminPathAndRunningItAgainSucceedsAndChangesNothing()
    {
        var typeCreatePage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Dimensions/Types/Create");

        var typeFields = RenderedForm.FieldsOf(typeCreatePage)
            .With("Code", "manual-division")
            .With("NameEn", "Manual Division")
            .With("NameAr", "قسم يدوي");

        (await _fixture.Administrator.PostAsync("/Admin/Dimensions/Types/Create", new FormUrlEncodedContent(typeFields)))
            .EnsureSuccessStatusCode();

        var structureCreatePage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Dimensions/Structures/Create");

        var manualTypeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var type = await services.GetRequiredService<IDimensionTypeService>().GetByCodeAsync("manual-division");
            type.Should().NotBeNull();
            manualTypeId = type!.DimensionTypeId;
        });

        var structureFields = RenderedForm.FieldsOf(structureCreatePage)
            .With("Code", "manual-structure")
            .With("NameEn", "Manual Structure")
            .With("NameAr", "هيكل يدوي")
            .With("IsStrict", "true")
            .With("LevelDimensionTypeIds[0]", manualTypeId);

        (await _fixture.Administrator.PostAsync("/Admin/Dimensions/Structures/Create", new FormUrlEncodedContent(structureFields)))
            .EnsureSuccessStatusCode();

        const string executeUrl =
            "/Admin/Recipes/Execute?basePath=Areas%2FWorkMate.Dimensions%2FRecipes&fileName=organisation-designer-demo.recipe.json";

        var recipesPage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Recipes");
        var token = BaseTenantFixture.AntiforgeryTokenIn(recipesPage);

        var firstRun = await _fixture.Administrator.PostAsync(
            executeUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        firstRun.EnsureSuccessStatusCode();

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var structure = await structures.GetByCodeAsync("demo-org");
            structure.Should().NotBeNull(
                "the demo recipe must apply via the real admin path even with unrelated hand-created data already in the tenant");
            structure!.Levels.Should().HaveCount(3);

            var roots = await graph.GetRootsAsync(structure.StructureId);
            roots.Should().HaveCount(2, "the demo seeds two divisions at the root");

            var unplaced = await graph.GetUnplacedAsync(structure.StructureId);
            unplaced.Should().ContainSingle(node => node.Code == "demo-dept-unassigned");
        });

        // Re-running it now, without cleaning up first, must succeed and change nothing. The HTTP
        // response alone cannot tell a clean success from a caught failure — both redirect to
        // /Admin/Recipes with 200 OK, which is exactly why "Unexpected error" was uninformative in
        // the first place — so this second run goes through the in-process executor instead, to
        // assert the real outcome directly, against the same real file the first run just applied
        // through the admin screen.
        var recipePath = Path.Combine(
            RepositoryRoot, "src", "WorkMate.Dimensions", "Recipes", "organisation-designer-demo.recipe.json");
        var json = await File.ReadAllTextAsync(recipePath);

        await InTenantAsSystemAsync(async services =>
        {
            var secondRun = await ExecuteRecipeAsync(services, json);
            secondRun.Succeeded.Should().BeTrue(
                "every row already matches the tenant, so every step must skip rather than error; " +
                string.Join("; ", secondRun.Errors));

            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var structure = await structures.GetByCodeAsync("demo-org");

            (await graph.GetRootsAsync(structure!.StructureId)).Should().HaveCount(
                2, "a clean second run recognises every row as already matching and must not duplicate or change anything");

            var unplaced = await graph.GetUnplacedAsync(structure.StructureId);
            unplaced.Should().ContainSingle(node => node.Code == "demo-dept-unassigned");
        });
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
        }
    }

    [Fact]
    public async Task TheRecipeHarvesterListsTheDemoRecipeWhenDimensionsIsEnabled()
    {
        // The recipe lives in src/WorkMate.Dimensions/Recipes/, not under /recipes at the
        // repository root: OrchardCore.Recipes.Services.RecipeHarvester — the harvester behind
        // the /Admin/Recipes screen — is scoped to each enabled extension's own Recipes folder
        // via IExtensionManager. ApplicationRecipeHarvester, which does scan the application
        // content root /recipes is copied into, feeds the tenant *setup* screen's recipe picker
        // only, and that is a different list.
        await InTenantAsSystemAsync(async services =>
        {
            var harvesters = services.GetServices<IRecipeHarvester>();
            var harvested = new List<RecipeDescriptor>();

            foreach (var harvester in harvesters)
            {
                harvested.AddRange(await harvester.HarvestRecipesAsync());
            }

            harvested.Should().Contain(
                recipe => recipe.Name == "WorkMate.Demo.OrganisationDesigner",
                "the demo recipe must be discoverable the same way the admin screen discovers every other module recipe");

            var demo = harvested.Single(recipe => recipe.Name == "WorkMate.Demo.OrganisationDesigner");

            demo.DisplayName.Should().Be("WorkMate demo: organisation designer sample data");
            demo.IsSetupRecipe.Should().BeFalse("it must not offer itself on the tenant setup screen");
        });
    }

    [Fact]
    public async Task TwoTypesSharingACodeInOneStepFailTheWholeStepAndNothingIsWritten()
    {
        const string json = """
        {
            "steps": [
                {
                    "name": "dimension-types",
                    "types": [
                        { "code": "dup-recipe-type", "nameEn": "First", "nameAr": "الأول", "allowsSelfNesting": false },
                        { "code": "dup-recipe-type", "nameEn": "Second", "nameAr": "الثاني", "allowsSelfNesting": false }
                    ]
                }
            ]
        }
        """;

        await InTenantAsSystemAsync(async services =>
        {
            var result = await ExecuteRecipeAsync(services, json);

            result.Succeeded.Should().BeFalse("two rows of the same step share a code");

            var types = services.GetRequiredService<IDimensionTypeService>();
            var type = await types.GetByCodeAsync("dup-recipe-type");

            type.Should().BeNull("a batch failure must write nothing, not even the first, valid-looking row");
        });
    }

    [Fact]
    public async Task ATypeWithTheSameCodeButDifferentContentFailsNamingTheDifference()
    {
        const string original = """
            { "steps": [ { "name": "dimension-types", "types": [
                { "code": "mismatch-division", "nameEn": "Original Name", "nameAr": "الاسم الأصلي", "allowsSelfNesting": false }
            ] } ] }
            """;

        const string changed = """
            { "steps": [ { "name": "dimension-types", "types": [
                { "code": "mismatch-division", "nameEn": "Changed Name", "nameAr": "الاسم الأصلي", "allowsSelfNesting": false }
            ] } ] }
            """;

        await InTenantAsSystemAsync(async services =>
        {
            (await ExecuteRecipeAsync(services, original)).Succeeded.Should().BeTrue();

            var second = await ExecuteRecipeAsync(services, changed);
            second.Succeeded.Should().BeFalse("the existing type's English name does not match the recipe's");
            second.Errors.Should().ContainSingle(error =>
                error.Contains("mismatch-division", StringComparison.Ordinal) &&
                error.Contains("English name", StringComparison.Ordinal));

            var types = services.GetRequiredService<IDimensionTypeService>();
            var type = await types.GetByCodeAsync("mismatch-division");
            type!.Name.En.Should().Be("Original Name", "a mismatch must never silently overwrite the tenant's data");
        });
    }

    [Fact]
    public async Task AStructureWithTheSameCodeButDifferentLevelsFailsNamingTheDifference()
    {
        const string setup = """
            {
                "steps": [
                    {
                        "name": "dimension-types",
                        "types": [
                            { "code": "mismatch-struct-division", "nameEn": "Division", "nameAr": "قسم", "allowsSelfNesting": false },
                            { "code": "mismatch-struct-department", "nameEn": "Department", "nameAr": "إدارة", "allowsSelfNesting": false }
                        ]
                    },
                    {
                        "name": "structures",
                        "structures": [
                            {
                                "code": "mismatch-structure",
                                "nameEn": "Mismatch Structure",
                                "nameAr": "هيكل",
                                "levelTypeCodes": [ "mismatch-struct-division" ],
                                "allowSkipLevel": false,
                                "isStrict": true,
                                "isPrimaryOrganisation": false
                            }
                        ]
                    }
                ]
            }
            """;

        const string changedLevels = """
            {
                "steps": [
                    {
                        "name": "structures",
                        "structures": [
                            {
                                "code": "mismatch-structure",
                                "nameEn": "Mismatch Structure",
                                "nameAr": "هيكل",
                                "levelTypeCodes": [ "mismatch-struct-division", "mismatch-struct-department" ],
                                "allowSkipLevel": false,
                                "isStrict": true,
                                "isPrimaryOrganisation": false
                            }
                        ]
                    }
                ]
            }
            """;

        await InTenantAsSystemAsync(async services =>
        {
            (await ExecuteRecipeAsync(services, setup)).Succeeded.Should().BeTrue();

            var second = await ExecuteRecipeAsync(services, changedLevels);
            second.Succeeded.Should().BeFalse("the existing structure's levels do not match the recipe's");
            second.Errors.Should().ContainSingle(error =>
                error.Contains("mismatch-structure", StringComparison.Ordinal) &&
                error.Contains("levels", StringComparison.Ordinal));

            var structures = services.GetRequiredService<IStructureService>();
            var structure = await structures.GetByCodeAsync("mismatch-structure");
            structure!.Levels.Should().ContainSingle(
                "a mismatch must never silently overwrite the tenant's data");
        });
    }

    [Fact]
    public async Task ARecordWithTheSameCodeButDifferentNameFailsNamingTheDifference()
    {
        const string setup = """
            {
                "steps": [
                    {
                        "name": "dimension-types",
                        "types": [
                            { "code": "mismatch-rec-division", "nameEn": "Division", "nameAr": "قسم", "allowsSelfNesting": false }
                        ]
                    },
                    {
                        "name": "dimension-records",
                        "records": [
                            {
                                "code": "mismatch-rec-1",
                                "typeCode": "mismatch-rec-division",
                                "nameEn": "Original Division",
                                "nameAr": "القسم الأصلي",
                                "effectiveFrom": "2024-01-01",
                                "placements": []
                            }
                        ]
                    }
                ]
            }
            """;

        const string changedName = """
            {
                "steps": [
                    {
                        "name": "dimension-records",
                        "records": [
                            {
                                "code": "mismatch-rec-1",
                                "typeCode": "mismatch-rec-division",
                                "nameEn": "Changed Division",
                                "nameAr": "القسم الأصلي",
                                "effectiveFrom": "2024-01-01",
                                "placements": []
                            }
                        ]
                    }
                ]
            }
            """;

        await InTenantAsSystemAsync(async services =>
        {
            (await ExecuteRecipeAsync(services, setup)).Succeeded.Should().BeTrue();

            var second = await ExecuteRecipeAsync(services, changedName);
            second.Succeeded.Should().BeFalse("the existing record's English name does not match the recipe's");
            second.Errors.Should().ContainSingle(error =>
                error.Contains("mismatch-rec-1", StringComparison.Ordinal) &&
                error.Contains("English name", StringComparison.Ordinal));

            var records = services.GetRequiredService<IDimensionService>();
            var record = await records.GetByCodeAsync("mismatch-rec-1");
            record!.NameEn.Should().Be("Original Division", "a mismatch must never silently overwrite the tenant's data");
        });
    }
}
