using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The <c>lookup-lists</c> recipe step, and ADR-0008's re-run rules applied to it.
/// </summary>
/// <remarks>
/// Four outcomes are the contract: create what is new, skip what matches, fail naming what differs,
/// and write nothing at all when anything in the step fails. The third is the one worth testing
/// hardest, because the alternative — overwriting quietly — is the failure ADR-0008 exists to
/// prevent and the only one nobody would notice.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class LookupListsRecipeStepTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public LookupListsRecipeStepTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    [Fact]
    public async Task AListAndItsEntriesAreCreated()
    {
        await ApplyAsync(Recipe(List(
            "step-grade",
            "Grade",
            "الدرجة",
            Entry("G1", "Grade 1", "الدرجة ١", order: 10),
            Entry("G2", "Grade 2", "الدرجة ٢", order: 20))));

        await InTenantAsync(async lists =>
        {
            var grade = await lists.GetByCodeAsync("step-grade");

            grade.Should().NotBeNull();
            grade!.Name.Ar.Should().Be("الدرجة");
            grade.Items.Select(item => item.Code).Should().Equal(["G1", "G2"]);
            grade.Items.Should().AllSatisfy(item => item.IsActive.Should().BeTrue(
                "absent means offered; a pack listing a choice nobody may pick would say so"));

            grade.IsSystemDefined.Should().BeTrue(
                "arriving in a recipe is what makes a list shipped rather than invented");
        });
    }

    /// <summary>
    /// An entry with no stated order gets the order the file lists it in.
    /// </summary>
    /// <remarks>
    /// ADR-0008's sort-order addendum, applied to this step: a hand-written file states nothing and
    /// gets its own listing order, which is the thing its author can actually see. An export states
    /// every one, because an export reproduces a tenant rather than expressing an intent.
    /// </remarks>
    [Fact]
    public async Task AnEntryWithNoStatedOrderTakesTheFilesOrder()
    {
        await ApplyAsync(Recipe(List(
            "step-unordered",
            "Unordered",
            "بلا ترتيب",
            Entry("THIRD", "Third"),
            Entry("FIRST", "First"),
            Entry("SECOND", "Second"))));

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-unordered");

            list!.Items.Select(item => item.Code).Should().Equal(["THIRD", "FIRST", "SECOND"],
                "the order the file lists them in, not alphabetical");
        });
    }

    [Fact]
    public async Task AnIdenticalFileIsAppliedTwiceAndChangesNothing()
    {
        var recipe = Recipe(List(
            "step-rerun",
            "Re-run",
            "إعادة",
            Entry("A", "Alpha", "ألفا", order: 10)));

        await ApplyAsync(recipe);
        await ApplyAsync(recipe);

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-rerun");

            list!.Items.Should().ContainSingle("the second run wrote nothing at all");
            list.Name.En.Should().Be("Re-run");
        });
    }

    /// <summary>
    /// A list the tenant has added its own entries to still re-runs clean.
    /// </summary>
    /// <remarks>
    /// The case ADR-0008's "comparison is scoped to what the row states" rule is for, and the one a
    /// customer actually hits: they extend a shipped list, then the pack is re-applied. A step that
    /// read the file's four entries as a claim that the tenant's fifth should not exist would refuse
    /// to re-run on every tenant that had ever used the feature.
    /// </remarks>
    [Fact]
    public async Task AnEntryTheTenantAddedIsNotADifference()
    {
        var recipe = Recipe(List(
            "step-extended",
            "Extended",
            "موسّعة",
            Entry("SHIPPED", "Shipped", "مُرفق", order: 10)));

        await ApplyAsync(recipe);

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-extended");

            var extended = await lists.UpdateAsync(
                list!.LookupListId,
                list.Name,
                [
                    .. list.Items,
                    new WorkMate.Records.Models.LookupListItem(
                        "CUSTOMERS",
                        new WorkMate.Core.BilingualText("The customer's own", "خاص بالعميل"),
                        SortOrder: 20),
                ]);

            extended.Succeeded.Should().BeTrue(extended.Describe());
        });

        await ApplyAsync(recipe);

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-extended");

            list!.Items.Select(item => item.Code).Should().Equal(["SHIPPED", "CUSTOMERS"],
                "the file says nothing about the entry it does not name");
        });
    }

    /// <summary>
    /// A stated entry the tenant does not hold fails the step, naming it.
    /// </summary>
    /// <remarks>
    /// ADR-0008 applied literally — the row is making a claim the tenant contradicts — and the
    /// consequence worth stating: a second version of a pack that adds an entry to an existing list
    /// cannot be applied over it, and the operator adds the entry through the screen. The
    /// alternative is filling a gap in silently, which is the behaviour the ADR exists to refuse.
    /// </remarks>
    [Fact]
    public async Task AnEntryTheFileAddsToAnExistingListFailsTheStep()
    {
        await ApplyAsync(Recipe(List(
            "step-added",
            "Added",
            "مضافة",
            Entry("ONE", "One", "واحد", order: 10))));

        var withAnother = Recipe(List(
            "step-added",
            "Added",
            "مضافة",
            Entry("ONE", "One", "واحد", order: 10),
            Entry("TWO", "Two", "اثنان", order: 20)));

        var failure = await Record.ExceptionAsync(() => ApplyAsync(withAnother));

        failure.Should().NotBeNull();

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-added");

            list!.Items.Should().ContainSingle("nothing was written on the way to failing");
        });
    }

    [Theory]
    [InlineData("nameEn", "Renamed")]
    [InlineData("entryNameEn", "Renamed entry")]
    [InlineData("isActive", "false")]
    [InlineData("sortOrder", "99")]
    public async Task AStatedFieldThatDiffersFailsTheStepAndWritesNothing(string field, string value)
    {
        var code = $"step-differs-{field.ToLowerInvariant()}";

        await ApplyAsync(Recipe(List(code, "Original", "أصلية", Entry("A", "Alpha", "ألفا", order: 10))));

        var changed = List(code, "Original", "أصلية", Entry("A", "Alpha", "ألفا", order: 10));

        switch (field)
        {
            case "nameEn":
                changed["nameEn"] = value;
                break;
            case "entryNameEn":
                changed["items"]!.AsArray()[0]!["nameEn"] = value;
                break;
            case "isActive":
                changed["items"]!.AsArray()[0]!["isActive"] = bool.Parse(value);
                break;
            default:
                changed["items"]!.AsArray()[0]!["sortOrder"] = int.Parse(value, CultureInfo.InvariantCulture);
                break;
        }

        var failure = await Record.ExceptionAsync(() => ApplyAsync(Recipe(changed)));

        failure.Should().NotBeNull("a row that disagrees with the tenant stops the step");

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync(code);

            list!.Name.En.Should().Be("Original");
            list.Items.Single().Name.En.Should().Be("Alpha");
            list.Items.Single().IsActive.Should().BeTrue();
            list.Items.Single().SortOrder.Should().Be(10);
        });
    }

    /// <summary>
    /// A file carrying <c>null</c> where the tenant holds an empty name is not a difference.
    /// </summary>
    /// <remarks>
    /// ADR-0003's addendum: null and empty both mean "no name in this language", and reporting them
    /// as different would refuse a re-run of a correct recipe. The same normalisation names already
    /// go through, which is why this goes through <c>RecipeNameComparison</c> rather than an
    /// ordinal comparison.
    /// </remarks>
    [Fact]
    public async Task AnAbsentArabicNameAndAnEmptyOneAreTheSameName()
    {
        await ApplyAsync(Recipe(List("step-noar", "No Arabic", null, Entry("A", "Alpha", order: 10))));

        var explicitlyNull = List("step-noar", "No Arabic", null, Entry("A", "Alpha", order: 10));
        explicitlyNull["nameAr"] = null;
        explicitlyNull["items"]!.AsArray()[0]!["nameAr"] = null;

        var failure = await Record.ExceptionAsync(() => ApplyAsync(Recipe(explicitlyNull)));

        failure.Should().BeNull("'nameAr': null and a tenant holding '' describe the same absent name");
    }

    [Fact]
    public async Task ACodeListedTwiceFailsTheStepAndWritesNothing()
    {
        var failure = await Record.ExceptionAsync(() => ApplyAsync(Recipe(
            List("step-dup", "First", "الأولى"),
            List("step-dup", "Second", "الثانية"))));

        failure.Should().NotBeNull();

        await InTenantAsync(async lists =>
            (await lists.GetByCodeAsync("step-dup")).Should().BeNull());
    }

    /// <summary>
    /// A malformed code anywhere in the file stops the whole step, so a typo in the last list
    /// leaves the tenant as it found it.
    /// </summary>
    [Fact]
    public async Task AMalformedCodeInTheLastListLeavesTheTenantUntouched()
    {
        var failure = await Record.ExceptionAsync(() => ApplyAsync(Recipe(
            List("step-good", "Good", "جيدة", Entry("A", "Alpha")),
            List("1bad", "Bad", "سيئة"))));

        failure.Should().NotBeNull();

        await InTenantAsync(async lists =>
            (await lists.GetByCodeAsync("step-good")).Should().BeNull(
                "everything is validated before anything is written"));
    }

    // ---- harness ---------------------------------------------------------------------------

    private static JsonObject Entry(
        string code,
        string nameEn,
        string? nameAr = null,
        bool? active = null,
        int? order = null)
    {
        var entry = new JsonObject
        {
            ["code"] = code,
            ["nameEn"] = nameEn,
        };

        if (nameAr is not null)
        {
            entry["nameAr"] = nameAr;
        }

        if (active is { } isActive)
        {
            entry["isActive"] = isActive;
        }

        if (order is { } sortOrder)
        {
            entry["sortOrder"] = sortOrder;
        }

        return entry;
    }

    private static JsonObject List(string code, string nameEn, string? nameAr, params JsonObject[] entries)
    {
        var list = new JsonObject
        {
            ["code"] = code,
            ["nameEn"] = nameEn,
            ["items"] = new JsonArray([.. entries.Cast<JsonNode>()]),
        };

        if (nameAr is not null)
        {
            list["nameAr"] = nameAr;
        }

        return list;
    }

    private static JsonObject Recipe(params JsonObject[] lists) => new()
    {
        ["name"] = "lookup-lists-step-test",
        ["steps"] = new JsonArray(
        [
            new JsonObject
            {
                ["name"] = "lookup-lists",
                ["lists"] = new JsonArray([.. lists.Cast<JsonNode>()]),
            },
        ]),
    };

    private async Task ApplyAsync(JsonObject recipe) =>
        await InTenantAsServicesAsync(async services =>
        {
            var directory = Directory.CreateTempSubdirectory("workmate-lookup-step-");

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory.FullName, "recipe.json"), recipe.ToJsonString());

                var fileProvider = new PhysicalFileProvider(directory.FullName);

                await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                    Guid.NewGuid().ToString("n"),
                    new RecipeDescriptor
                    {
                        Name = "lookup-lists-step-test",
                        BasePath = string.Empty,
                        FileProvider = fileProvider,
                        RecipeFileInfo = fileProvider.GetFileInfo("recipe.json"),
                        RequireNewScope = false,
                    },
                    new Dictionary<string, object>(),
                    CancellationToken.None);
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        });

    private Task InTenantAsync(Func<ILookupListService, Task> work) =>
        InTenantAsServicesAsync(services => work(services.GetRequiredService<ILookupListService>()));

    private Task InTenantAsServicesAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup lists recipe step test"))
            {
                await work(services);
            }
        });
}
