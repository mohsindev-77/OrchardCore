using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Deployment;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Deployment;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// ADR-0011's proof for lookup lists: seed a tenant, export it, import the export into a brand-new
/// tenant, and assert the two hold the same lists.
/// </summary>
/// <remarks>
/// <b>Why a round trip rather than two tests.</b> The export writes the import format on purpose —
/// ADR-0011's central decision — and the whole value of that is that one test exercises both. A
/// field the exporter forgets is a field this notices; a field the importer cannot read is the
/// same failure from the other end. Two separate tests, each against a fixture of its own, would
/// agree with each other and both be wrong.
///
/// <b>What the seeded tenant is built to contain</b>, chosen because each is a thing a careless
/// export flattens into something that still looks right:
/// <list type="bullet">
/// <item>a retired entry, which must survive as retired rather than as absent or as active;</item>
/// <item>an entry with no Arabic name, which is optional since ADR-0003's addendum and must not
/// come back as an empty string that the next re-run then reports as a difference;</item>
/// <item>a sort order that is not the alphabetical order, so a round trip that re-sorted would
/// show;</item>
/// <item>a tenant's own list beside a system-defined one, because the flag decides whether a list
/// can ever be deleted.</item>
/// </list>
/// </remarks>
public sealed class LookupListsExportRoundTripTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// Both hosts are <c>WebApplicationFactory</c> instances, which are disposable.
    /// <see cref="DisposeAsync"/> is what actually releases them; this exists because the analyser
    /// cannot see that, and disposing twice is harmless.
    /// </summary>
    public void Dispose()
    {
        _source?.Dispose();
        _destination?.Dispose();
    }

    private BaseTenantFixture _source = default!;
    private BaseTenantFixture _destination = default!;

    public async Task InitializeAsync()
    {
        _source = new BaseTenantFixture();
        _destination = new BaseTenantFixture();

        await _source.InitializeAsync();
        await _destination.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _destination.DisposeAsync();
    }

    [Fact]
    public async Task ATenantsLookupListsSurviveAnExportAndAnImport()
    {
        await SeedAsync();

        var recipe = await ExportAsync(_source);

        await ApplyAsync(_destination, recipe);

        await InTenantAsync(_destination, async lists =>
        {
            var grade = await lists.GetByCodeAsync("rt-grade");

            grade.Should().NotBeNull("the export carried it and the import read it");
            grade!.Name.En.Should().Be("Grade");
            grade.Name.Ar.Should().Be("الدرجة");
            grade.IsSystemDefined.Should().BeTrue();

            // In sort order, which is deliberately not alphabetical: a round trip that re-sorted
            // would reproduce a list a reader is offered in a different order.
            grade.Items.Select(item => item.Code).Should().Equal(["G9", "G1", "G2"]);

            var retired = grade.Items.Single(item => item.Code == "G9");

            retired.IsActive.Should().BeFalse("a retired entry must arrive retired, not absent and not active");
            retired.Name.En.Should().Be("Grade 9 (old)");

            var noArabic = grade.Items.Single(item => item.Code == "G2");

            noArabic.Name.Ar.Should().BeEmpty(
                "Arabic is optional, and an absent half must not come back as something the next "
                + "re-run reports as a difference");

            var tenantsOwn = await lists.GetByCodeAsync("rt-shift");

            tenantsOwn.Should().NotBeNull();
            tenantsOwn!.Items.Should().ContainSingle().Which.Code.Should().Be("NIGHT");
        });
    }

    /// <summary>
    /// Applying the export a second time changes nothing, which is what makes a promotion from a
    /// configuration tenant to a production one repeatable.
    /// </summary>
    /// <remarks>
    /// This is the join between ADR-0011 and ADR-0008, and it is the one most likely to break
    /// silently: the exporter states every field, including the ones a hand-written file would omit,
    /// so every one of them is compared on the next run. A single field written in a shape the
    /// comparison reads as different — <c>null</c> against <c>""</c> being the classic — turns a
    /// correct export into a file that cannot be applied twice.
    /// </remarks>
    [Fact]
    public async Task TheExportCanBeAppliedTwice()
    {
        await SeedAsync();

        var recipe = await ExportAsync(_source);

        await ApplyAsync(_destination, recipe);

        var second = await Record.ExceptionAsync(() => ApplyAsync(_destination, recipe));

        second.Should().BeNull("an export states every field, so every field has to compare equal");

        await InTenantAsync(_destination, async lists =>
        {
            var grade = await lists.GetByCodeAsync("rt-grade");

            grade!.Items.Should().HaveCount(3, "the second run wrote nothing at all");
        });
    }

    // ---- harness ---------------------------------------------------------------------------

    private Task SeedAsync() =>
        InTenantAsync(_source, async lists =>
        {
            var grade = await lists.CreateAsync(
                "rt-grade",
                new BilingualText("Grade", "الدرجة"),
                [
                    // Deliberately not in alphabetical order, and the retired one first.
                    new LookupListItem("G9", new BilingualText("Grade 9 (old)", "الدرجة ٩"), IsActive: false, SortOrder: 5),
                    new LookupListItem("G1", new BilingualText("Grade 1", "الدرجة ١"), SortOrder: 10),
                    new LookupListItem("G2", new BilingualText("Grade 2", string.Empty), SortOrder: 20),
                ],
                isSystemDefined: true);

            grade.Succeeded.Should().BeTrue(grade.Describe());

            var shift = await lists.CreateAsync(
                "rt-shift",
                new BilingualText("Shift", "الوردية"),
                [new LookupListItem("NIGHT", new BilingualText("Night", "ليلية"), SortOrder: 10)]);

            shift.Succeeded.Should().BeTrue(shift.Describe());
        });

    /// <summary>
    /// Drives the deployment source directly rather than through a plan, like the other two round
    /// trips: a plan would add a file builder and a download, neither of which is under test.
    /// </summary>
    private static async Task<JsonObject> ExportAsync(BaseTenantFixture tenant)
    {
        JsonObject? recipe = null;

        await InTenantAsServicesAsync(tenant, async services =>
        {
            var result = new DeploymentPlanResult(new MemoryFileBuilder(), new RecipeDescriptor());

            var records = services.GetServices<IDeploymentSource>()
                .Single(candidate => candidate.GetType() == typeof(RecordsDeploymentSource));

            // Lists only. The employees half has its own round trip, and mixing them here would
            // mean a failure in either one failed both.
            await records.ProcessDeploymentStepAsync(
                new RecordsDeploymentStep { IncludeEmployees = false, IncludeLookupLists = true },
                result);

            await result.FinalizeAsync();

            recipe = result.Recipe;
        });

        recipe.Should().NotBeNull();

        var steps = recipe!["steps"]!.AsArray();

        steps.Should().ContainSingle("only the lookup lists were asked for")
            .Which!["name"]!.GetValue<string>().Should().Be("lookup-lists");

        return recipe;
    }

    private static Task ApplyAsync(BaseTenantFixture tenant, JsonObject recipe) =>
        InTenantAsServicesAsync(tenant, async services =>
        {
            var directory = Directory.CreateTempSubdirectory("workmate-lookup-roundtrip-");

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory.FullName, "recipe.json"), recipe.ToJsonString());

                var fileProvider = new PhysicalFileProvider(directory.FullName);

                await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                    Guid.NewGuid().ToString("n"),
                    new RecipeDescriptor
                    {
                        Name = "lookup-lists-round-trip",
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

    private static Task InTenantAsync(BaseTenantFixture tenant, Func<ILookupListService, Task> work) =>
        InTenantAsServicesAsync(tenant, services => work(services.GetRequiredService<ILookupListService>()));

    private static Task InTenantAsServicesAsync(BaseTenantFixture tenant, Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup list round trip"))
            {
                await work(services);
            }
        });

    /// <summary>
    /// A file builder that keeps nothing: the recipe is read from
    /// <see cref="DeploymentPlanResult.Recipe"/>, not from a file on disk.
    /// </summary>
    private sealed class MemoryFileBuilder : IFileBuilder
    {
        public Task SetFileAsync(string subpath, Stream stream) => Task.CompletedTask;
    }
}
