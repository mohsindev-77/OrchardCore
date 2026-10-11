using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The four outcomes a <c>structures</c> row can have for <c>employeeAttachableTypeCodes</c>.
/// </summary>
/// <remarks>
/// <b>This is ADR-0008's 11 October addendum, tested.</b> The original decision gives a re-run two
/// outcomes — identical means skip, different means fail — and the addendum adds a third for this
/// field alone: a tenant holding <em>nothing</em> is not holding a different answer, because the
/// model documents empty as "this axis is not expressing the constraint". So a row that states a
/// set against a structure that declares none fills it in.
///
/// The one that matters most is the last: a tenant that has ticked a real, different set is still
/// refused, writing nothing. The addendum is narrow, and a test that only proved the convenient
/// half would be the wrong half.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class StructureEmployeeAttachmentRecipeTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public StructureEmployeeAttachmentRecipeTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A fresh structure takes the constraint from the row that created it.
    /// </summary>
    [Fact]
    public async Task AFreshStructureTakesTheConstraintFromTheRecipe()
    {
        var types = await GivenTwoTypesAsync("ea-fresh");

        await ApplyAsync(StepFor("ea-fresh-org", types, attachable: ["ea-fresh-department"]));

        (await AttachableCodesAsync("ea-fresh-org", types))
            .Should().BeEquivalentTo(["ea-fresh-department"]);
    }

    /// <summary>
    /// Applying the same row again changes nothing and fails nothing.
    /// </summary>
    /// <remarks>
    /// The original decision's first outcome, which the new field must not break: a recipe is a
    /// declared desired state, and applying it twice has to converge rather than error.
    /// </remarks>
    [Fact]
    public async Task ReapplyingTheSameRowIsANoOp()
    {
        var types = await GivenTwoTypesAsync("ea-again");
        var step = StepFor("ea-again-org", types, attachable: ["ea-again-department"]);

        await ApplyAsync(step);
        await ApplyAsync(step);

        (await AttachableCodesAsync("ea-again-org", types))
            .Should().BeEquivalentTo(["ea-again-department"]);
    }

    /// <summary>
    /// A structure that declares nothing has the constraint filled in rather than being refused.
    /// </summary>
    /// <remarks>
    /// The case the addendum exists for, and the one every demo tenant built before this change is
    /// in: the structure was created by a row that said nothing about employee attachment, and the
    /// row now says something. Empty is the absence of an answer, so there is no decision to
    /// overwrite — and the alternative, failing, would mean no existing demo tenant could re-run
    /// the recipe that built it.
    /// </remarks>
    [Fact]
    public async Task AStructureDeclaringNothingHasTheConstraintFilledIn()
    {
        var types = await GivenTwoTypesAsync("ea-gap");

        // Built first by a row that says nothing, which is every recipe written before the field.
        await ApplyAsync(StepFor("ea-gap-org", types, attachable: null));

        (await AttachableCodesAsync("ea-gap-org", types)).Should().BeEmpty(
            "a row that states nothing leaves the structure unconstrained");

        await ApplyAsync(StepFor("ea-gap-org", types, attachable: ["ea-gap-department"]));

        (await AttachableCodesAsync("ea-gap-org", types)).Should().BeEquivalentTo(
            ["ea-gap-department"],
            "stating it against a structure that declares none fills the gap rather than failing");
    }

    /// <summary>
    /// A structure holding a different, real set is refused, and nothing is written.
    /// </summary>
    /// <remarks>
    /// The half of the addendum that is not a convenience. A tenant that has ticked Division and
    /// Department does not silently become Department because a recipe said so — that is the
    /// overwrite ADR-0008 refuses, and the narrowness of "empty is an absence" is what keeps this
    /// case where it was.
    /// </remarks>
    [Fact]
    public async Task AStructureHoldingADifferentSetIsRefusedAndNothingIsWritten()
    {
        var types = await GivenTwoTypesAsync("ea-clash");

        await ApplyAsync(StepFor("ea-clash-org", types, attachable: ["ea-clash-division"]));

        var failure = await Record.ExceptionAsync(() =>
            ApplyAsync(StepFor("ea-clash-org", types, attachable: ["ea-clash-department"])));

        failure.Should().NotBeNull("a real, different answer is a difference and the step fails");

        (await AttachableCodesAsync("ea-clash-org", types)).Should().BeEquivalentTo(
            ["ea-clash-division"],
            "the tenant's own answer stands, which is the whole of ADR-0008's refusal to overwrite");
    }

    /// <summary>
    /// A type code naming nothing fails the row rather than resolving to nothing.
    /// </summary>
    /// <remarks>
    /// Everything in a recipe is named by code — that is what makes a recipe portable — so a code
    /// that resolves to nothing is a mistake in the file, not an empty set to pass on.
    /// </remarks>
    [Fact]
    public async Task AnUnknownTypeCodeFailsTheRow()
    {
        var types = await GivenTwoTypesAsync("ea-unknown");

        var failure = await Record.ExceptionAsync(() =>
            ApplyAsync(StepFor("ea-unknown-org", types, attachable: ["ea-unknown-nonexistent"])));

        failure.Should().NotBeNull();

        await InTenantAsSystemAsync(async services =>
        {
            (await services.GetRequiredService<IStructureService>().GetByCodeAsync("ea-unknown-org"))
                .Should().BeNull("the row failed, so nothing from it was written");
        });
    }

    /// <summary>
    /// The demo recipes now say which types hold employees, which is what takes the action off a
    /// Division's card.
    /// </summary>
    /// <remarks>
    /// Asserted on the files rather than on a tenant, because that is the claim: the recipes
    /// <em>declare</em> it. A tenant test for the behaviour it produces is
    /// <c>DesignerPlaceEmployeeActionTenantTests</c>.
    /// </remarks>
    [Theory]
    [InlineData("organisation-designer-zenith.recipe.json", "zenith-department", "zenith-team")]
    [InlineData("organisation-designer-crescent.recipe.json", "crescent-branch", "crescent-department")]
    public void TheDemoRecipesDeclareWhichTypesHoldEmployees(string fileName, string first, string second)
    {
        var json = File.ReadAllText(Path.Combine(RecipesFolder, fileName));
        var recipe = JsonNode.Parse(json);

        var structures = recipe!["steps"]!.AsArray()
            .Single(step => step!["name"]!.GetValue<string>() == "structures")!["structures"]!.AsArray();

        var codes = structures[0]!["employeeAttachableTypeCodes"]!.AsArray()
            .Select(code => code!.GetValue<string>())
            .ToList();

        codes.Should().BeEquivalentTo([first, second]);
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>The two suffixes every world here is built from.</summary>
    private static readonly string[] TypeSuffixes = ["division", "department"];

    /// <summary>A division type and a department type, created once per prefix.</summary>
    private async Task<(string Division, string Department)> GivenTwoTypesAsync(string prefix)
    {
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            foreach (var suffix in TypeSuffixes)
            {
                var code = $"{prefix}-{suffix}";

                if (await types.GetByCodeAsync(code) is null)
                {
                    (await types.CreateAsync(code, new BilingualText(code, code), [], allowsSelfNesting: false))
                        .Succeeded.Should().BeTrue();
                }
            }
        });

        return ($"{prefix}-division", $"{prefix}-department");
    }

    /// <summary>
    /// One <c>structures</c> step, optionally stating employee attachment.
    /// </summary>
    /// <param name="attachable">
    /// Null leaves the key out entirely, which is how a row says nothing — the distinction the
    /// whole addendum turns on, so the harness has to be able to express it.
    /// </param>
    private static string StepFor(
        string structureCode,
        (string Division, string Department) types,
        IReadOnlyList<string>? attachable)
    {
        var row = new JsonObject
        {
            ["code"] = structureCode,
            ["nameEn"] = structureCode,
            ["nameAr"] = structureCode,
            ["levelTypeCodes"] = new JsonArray(types.Division, types.Department),
            ["rootTypeCodes"] = new JsonArray(types.Division),
            ["containment"] = new JsonObject
            {
                [types.Division] = new JsonArray(types.Department),
            },
            ["isStrict"] = true,
            ["isPrimaryOrganisation"] = false,
        };

        if (attachable is not null)
        {
            row["employeeAttachableTypeCodes"] = new JsonArray([.. attachable.Select(code => (JsonNode)code!)]);
        }

        return new JsonObject
        {
            ["name"] = "structures",
            ["structures"] = new JsonArray(row),
        }.ToJsonString();
    }

    /// <summary>The codes of the types a structure says may hold employees.</summary>
    private async Task<IReadOnlyList<string>> AttachableCodesAsync(
        string structureCode, (string Division, string Department) types)
    {
        var codes = new List<string>();

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();
            var dimensionTypes = services.GetRequiredService<IDimensionTypeService>();

            var structure = await structures.GetByCodeAsync(structureCode);

            structure.Should().NotBeNull("'{0}' was created by the recipe", structureCode);

            foreach (var id in structure!.EmployeeAttachableDimensionTypeIds)
            {
                var type = await dimensionTypes.GetAsync(id);

                codes.Add(type?.Code ?? id);
            }
        });

        return codes;
    }

    /// <summary>
    /// Runs one step as a recipe, so the step's own failure path is the one under test.
    /// </summary>
    /// <remarks>
    /// <c>RecipeExecutor</c> replaces whatever a step reports with a generic message, so these
    /// tests assert that an exception was thrown and then read the tenant to see what was written —
    /// which is the behaviour that matters anyway. ADR-0008's context note records the same about
    /// the admin screen's error.
    /// </remarks>
    private async Task ApplyAsync(string stepJson)
    {
        var folder = Path.Combine(Path.GetTempPath(), "workmate-recipe", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(folder);

        var fileName = "step.recipe.json";

        await File.WriteAllTextAsync(
            Path.Combine(folder, fileName),
            $$"""
            {
              "name": "WorkMate.Test.Structures",
              "steps": [ {{stepJson}} ]
            }
            """);

        try
        {
            await InTenantAsSystemAsync(async services =>
            {
                var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(folder);

                await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                    Guid.NewGuid().ToString("n"),
                    new RecipeDescriptor
                    {
                        Name = "structures-test",
                        BasePath = string.Empty,
                        FileProvider = fileProvider,
                        RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                        RequireNewScope = false,
                    },
                    new Dictionary<string, object>(),
                    CancellationToken.None);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("structures recipe test"))
            {
                await work(services);
            }
        });

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
