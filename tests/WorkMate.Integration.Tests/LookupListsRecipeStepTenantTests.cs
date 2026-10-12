using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
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
    /// A stated entry the tenant does not hold is added rather than refused.
    /// </summary>
    /// <remarks>
    /// ADR-0008's lookup-list addendum, and the reason it exists: the standard lists are the kind
    /// of reference data that grows — a new document type when a country changes its visa regime, a
    /// grade when a customer adds a band — and under the original rule the second version of that
    /// file could not be applied to any tenant holding the first. An entry the tenant does not hold
    /// has never been in the list, because ADR-0014 guarantees an entry is only ever retired, so
    /// there is no decision to overwrite.
    /// </remarks>
    [Fact]
    public async Task AnEntryTheFileAddsToAnExistingListIsFilledIn()
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

        failure.Should().BeNull("a missing entry is a gap to fill, not a difference to refuse");

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-added");

            list!.Items.Select(item => item.Code).Should().Equal(["ONE", "TWO"]);
            list.Items.Single(item => item.Code == "TWO").Name.Ar.Should().Be(
                "اثنان", "the entry arrives with everything the file says about it");
        });
    }

    /// <summary>
    /// A file that adds an entry <em>and</em> disagrees about an existing one fails, and fills
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The half of the addendum that keeps it narrow. The fill is only safe because the rest of the
    /// list matched; a file that contradicts the tenant about an entry that exists is a file whose
    /// intent is in doubt, and partially applying it would leave the tenant in a state neither the
    /// file nor the operator asked for.
    /// </remarks>
    [Fact]
    public async Task AFileThatAlsoDisagreesAboutAnExistingEntryFillsNothing()
    {
        await ApplyAsync(Recipe(List(
            "step-added-conflict",
            "Conflict",
            "تعارض",
            Entry("ONE", "One", "واحد", order: 10))));

        var conflicting = Recipe(List(
            "step-added-conflict",
            "Conflict",
            "تعارض",
            Entry("ONE", "Renamed", "واحد", order: 10),
            Entry("TWO", "Two", "اثنان", order: 20)));

        var failure = await Record.ExceptionAsync(() => ApplyAsync(conflicting));

        failure.Should().NotBeNull();

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-added-conflict");

            list!.Items.Should().ContainSingle("the gap was not filled either");
            list.Items.Single().Name.En.Should().Be("One");
        });
    }

    /// <summary>
    /// A tenant missing a shipped entry gets it, and the newer file stays re-appliable.
    /// </summary>
    /// <remarks>
    /// The upgrade path, which is what the addendum is for: a tenant on an older version of a
    /// shipped file is by definition a tenant missing entries the current one has. The second
    /// application is what proves the fill is idempotent rather than a one-shot.
    /// </remarks>
    [Fact]
    public async Task ATenantMissingAShippedEntryGetsItAddedAndTheFileStaysReAppliable()
    {
        await ApplyAsync(Recipe(List(
            "step-upgrade",
            "Upgrade",
            "ترقية",
            Entry("A", "Alpha", "ألفا", order: 10),
            Entry("B", "Bravo", "برافو", order: 20))));

        var newer = Recipe(List(
            "step-upgrade",
            "Upgrade",
            "ترقية",
            Entry("A", "Alpha", "ألفا", order: 10),
            Entry("B", "Bravo", "برافو", order: 20),
            Entry("C", "Charlie", "تشارلي", order: 30)));

        await ApplyAsync(newer);

        await InTenantAsync(async lists =>
        {
            var list = await lists.GetByCodeAsync("step-upgrade");

            list!.Items.Select(item => item.Code).Should().Equal(["A", "B", "C"]);
        });

        var again = await Record.ExceptionAsync(() => ApplyAsync(newer));

        again.Should().BeNull("the fill is idempotent: the second run finds nothing missing");
    }

    /// <summary>
    /// Re-applying the real standard lists over a tenant that has extended one changes nothing.
    /// </summary>
    /// <remarks>
    /// The same rules against the file an operator actually applies and the base recipe actually
    /// names, rather than against a fixture. A failure here is a customer unable to take a platform
    /// update.
    /// </remarks>
    [Fact]
    public async Task TheStandardListsReApplyOverATenantThatHasExtendedThem()
    {
        await InTenantAsync(async lists =>
        {
            var grade = (await lists.GetByCodeAsync(StandardLookupLists.Grade))!;

            var extended = await lists.UpdateAsync(
                grade.LookupListId,
                grade.Name,
                [
                    .. grade.Items,
                    new LookupListItem(
                        "G99",
                        new BilingualText("The customer's own grade", "درجة العميل"),
                        SortOrder: 999),
                ]);

            extended.Succeeded.Should().BeTrue(extended.Describe());
        });

        var failure = await Record.ExceptionAsync(ApplyStandardListsAsync);

        failure.Should().BeNull("the file a customer already has must stay appliable");

        await InTenantAsync(async lists =>
        {
            var grade = (await lists.GetByCodeAsync(StandardLookupLists.Grade))!;

            grade.Items.Should().Contain(item => item.Code == "G99", "the customer's own entry survives");
            grade.Items.Should().Contain(item => item.Code == "G1", "and so do the shipped ones");
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

    /// <summary>
    /// Applies the real <c>standard-lookup-lists</c> file, the way an operator would from
    /// Configuration → Recipes.
    /// </summary>
    /// <remarks>
    /// From the module's own <c>Recipes</c> folder rather than from a copy, so that a change to the
    /// shipped file is exercised by this test rather than silently diverging from it.
    /// </remarks>
    private Task ApplyStandardListsAsync() =>
        InTenantAsServicesAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(
                Path.Combine(RepositoryRoot, "src", "WorkMate.Records", "Recipes"));

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                new RecipeDescriptor
                {
                    Name = "WorkMate.StandardLookupLists",
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo("standard-lookup-lists.recipe.json"),
                    RequireNewScope = false,
                },
                new Dictionary<string, object>(),
                CancellationToken.None);
        });

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkMate.sln")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull("the test runs inside the repository");

            return directory!.FullName;
        }
    }

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
