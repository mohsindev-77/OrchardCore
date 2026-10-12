using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Deployment;
using OrchardCore.Deployment.Services;
using WorkMate.Dimensions.Deployment;
using WorkMate.Platform.Services;
using WorkMate.Records.Deployment;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The two deployment steps as an operator meets them: the screen, the rules, and the plan.
/// </summary>
/// <remarks>
/// <b>The gap this closes.</b> Both export round trips drive <c>IDeploymentSource</c> directly,
/// which is what a plan eventually calls but not how anybody configures one. Everything between the
/// screen and that call went untested — and the first thing this found was that the screen did not
/// open at all. The export was being proved end to end while the only way for a person to reach it
/// threw a dependency-injection exception; the same shape as the lifecycle buttons that had no
/// controls and the designer menu entry the script dropped.
///
/// <b>What is tested where.</b> The screen, over HTTP, because that is the only way to know it
/// renders. The combination rules as a function on the step, because they are rules about what an
/// export means rather than about how a form is drawn — see <c>DimensionsDeploymentStep.Problems</c>
/// for why they moved there. And the plan, through <c>IDeploymentManager</c>, which is what the
/// Export button runs.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DeploymentStepDriverTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DeploymentStepDriverTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// The screen an operator builds a deployment plan on actually opens.
    /// </summary>
    /// <remarks>
    /// <b>It did not.</b> Every request to it threw: <c>OrchardCore.Contents</c> registers a
    /// data-localization provider that depends on <c>OrchardCore.AdminMenu</c>, the base recipe
    /// enables <c>OrchardCore.DataLocalization</c> so content type names can be translated, and
    /// resolving the providers therefore tried to construct one whose dependency was not enabled.
    /// The result was a dependency-injection exception naming three Orchard features and nothing
    /// about the cause. <c>WorkMate.Platform</c> now declares AdminMenu, which is also the fix that
    /// reaches tenants that already exist.
    /// </remarks>
    [Fact]
    public async Task TheDeploymentPlanScreenOpens()
    {
        var response = await _tenant.Administrator.GetAsync("/Admin/DeploymentPlan/Index");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, "an export nobody can configure is an export nobody has");
    }

    /// <summary>
    /// Both steps are offered when a plan asks what can be added to it.
    /// </summary>
    /// <remarks>
    /// A step is reachable only if its <c>IDeploymentStepFactory</c> is registered under the right
    /// name — <c>AddDeployment</c> does that, and a step registered without one is a step that
    /// exists and cannot be added to anything. Checked against the factories rather than by
    /// scraping the add-step screen, because the screen's markup is not the contract.
    /// </remarks>
    [Fact]
    public async Task BothStepsCanBeAddedToAPlan() =>
        await _tenant.InTenantAsync(services =>
        {
            var offered = services.GetServices<IDeploymentStepFactory>()
                .Select(factory => factory.Name)
                .ToList();

            offered.Should().Contain(nameof(DimensionsDeploymentStep));
            offered.Should().Contain(nameof(RecordsDeploymentStep));

            return Task.CompletedTask;
        });

    /// <summary>
    /// Each combination that could not import is refused, with a reason naming what is missing.
    /// </summary>
    [Theory]
    [InlineData(false, true, true, false, false, "IncludeRecords", "dimension type")]
    [InlineData(true, false, true, false, false, "IncludeRecords", "structure")]
    [InlineData(true, true, false, true, false, "IncludeAssignments", "unit")]
    [InlineData(true, true, false, false, true, "IncludeHeads", "unit")]
    public void ACombinationThatCouldNotImportIsRefusedWithAReason(
        bool types,
        bool structures,
        bool records,
        bool assignments,
        bool heads,
        string beside,
        string mentions)
    {
        var step = new DimensionsDeploymentStep
        {
            IncludeTypes = types,
            IncludeStructures = structures,
            IncludeRecords = records,
            IncludeAssignments = assignments,
            IncludeHeads = heads,
        };

        var problems = step.Problems();

        problems.Should().NotBeEmpty("this combination produces a file that cannot apply anywhere");

        problems.Should().Contain(
            problem => problem.Property == beside,
            "the message belongs beside the switch it is about, or it appears at the top of a form "
            + "with five checkboxes and says nothing about which one");

        problems.Should().Contain(
            problem => DimensionsDeploymentStepDriver.Describe(problem.Problem, Plain)
                .Contains(mentions, StringComparison.OrdinalIgnoreCase),
            "the refusal must say what is missing, or it is an unexplained 'no'");
    }

    /// <summary>
    /// A combination that can import is accepted.
    /// </summary>
    /// <remarks>
    /// Not a formality: a rule written slightly too wide refuses something legitimate, and the
    /// symptom is an operator who cannot export what they are looking at. Heads without placements
    /// is the case worth naming — a head is not a placement (ADR-0012), so a tenant may well want
    /// one and not the other, and a single switch covering both would have made that unexpressible.
    /// </remarks>
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    public void ACombinationThatCanImportIsAccepted(
        bool types, bool structures, bool records, bool assignments, bool heads) =>
        new DimensionsDeploymentStep
        {
            IncludeTypes = types,
            IncludeStructures = structures,
            IncludeRecords = records,
            IncludeAssignments = assignments,
            IncludeHeads = heads,
        }
        .Problems()
        .Should().BeEmpty();

    /// <summary>
    /// Every problem the step can report has something to say about it.
    /// </summary>
    /// <remarks>
    /// The rules and the sentences are deliberately in different places — the rules on the step so
    /// they can be tested without an HTTP context, the sentences on the driver so the localisation
    /// checks can see them. The cost of that split is that a new rule can be added with no message,
    /// and <c>Describe</c> would then throw in front of an operator. This is the seam, so this is
    /// where it is held.
    /// </remarks>
    [Fact]
    public void EveryProblemHasAMessage()
    {
        foreach (var problem in Enum.GetValues<DimensionsExportProblem>())
        {
            DimensionsDeploymentStepDriver.Describe(problem, Plain)
                .Should().NotBeNullOrWhiteSpace(
                    "'{0}' is a refusal with no reason on it", problem);
        }
    }

    /// <summary>
    /// A localiser that hands back the key, so a test reads the English without a tenant.
    /// </summary>
    /// <remarks>
    /// Not a stand-in for the real one: that the strings resolve to Arabic is
    /// <c>LocalisationResourceTests</c>' job, and it can only do it because they are written as
    /// literals inside <c>Describe</c>. This is here so the assertions above are about the wording
    /// rather than about localisation.
    /// </remarks>
    private static readonly Microsoft.Extensions.Localization.IStringLocalizer Plain =
        new PassThroughLocalizer();

    private sealed class PassThroughLocalizer : Microsoft.Extensions.Localization.IStringLocalizer
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name] => new(name, name);

        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments));

        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(
            bool includeParentCultures) => [];
    }

    /// <summary>
    /// A plan carrying both steps writes the same recipe the sources write when driven directly.
    /// </summary>
    /// <remarks>
    /// The join the round-trip tests assume and never check. They call
    /// <c>ProcessDeploymentStepAsync</c> themselves, in an order they choose; a real export is a
    /// plan, executed by Orchard, in the order the plan holds — and if those two produced different
    /// files then the thing every round trip proves would be a thing no operator can obtain.
    ///
    /// Compared on the step names and their order, because order is the part that can differ and
    /// the part that matters: <c>employees</c> has to precede <c>employee-assignments</c>, which
    /// resolves people by a code that step creates.
    /// </remarks>
    [Fact]
    public async Task APlanBuiltFromTheStepsWritesTheSameRecipeAsTheSourcesDo() =>
        await _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("deployment driver test"))
            {
                var records = new RecordsDeploymentStep { IncludeEmployees = true };

                var dimensions = new DimensionsDeploymentStep
                {
                    IncludeAssignments = true,
                    IncludeHeads = true,
                };

                // Through the plan, which is what the Export button runs.
                var plan = new DeploymentPlan { Name = "driver-test", DeploymentSteps = [records, dimensions] };

                var viaPlan = new DeploymentPlanResult(
                    new NothingFileBuilder(), new OrchardCore.Recipes.Models.RecipeDescriptor());

                await services.GetRequiredService<IDeploymentManager>()
                    .ExecuteDeploymentPlanAsync(plan, viaPlan);

                await viaPlan.FinalizeAsync();

                // And directly, which is what both round-trip tests do.
                var viaSources = new DeploymentPlanResult(
                    new NothingFileBuilder(), new OrchardCore.Recipes.Models.RecipeDescriptor());

                var sources = services.GetServices<IDeploymentSource>().ToList();

                await sources.Single(source => source.GetType() == typeof(RecordsDeploymentSource))
                    .ProcessDeploymentStepAsync(records, viaSources);

                await sources.Single(source => source.GetType() == typeof(DimensionsDeploymentSource))
                    .ProcessDeploymentStepAsync(dimensions, viaSources);

                await viaSources.FinalizeAsync();

                StepNamesIn(viaPlan).Should().Equal(
                    StepNamesIn(viaSources),
                    "a plan and the sources it is made of must write the same file, or the round-trip "
                    + "tests are proving something no operator can produce");

                // The order an operator has to be able to apply, which is why it is asserted rather
                // than merely counted. lookup-lists is first because a stored value has to resolve
                // against a list that already exists; employees before the dimension engine's two
                // people steps, which name people by code.
                StepNamesIn(viaPlan).Should().Equal(
                    "lookup-lists",
                    "employees",
                    "dimension-types",
                    "structures",
                    "dimension-records",
                    "employee-assignments",
                    "unit-heads");
            }
        });

    private static IReadOnlyList<string> StepNamesIn(DeploymentPlanResult result) =>
        [.. (result.Recipe["steps"]?.AsArray() ?? []).Select(step => (string)step!["name"]!)];

    /// <summary>
    /// A file builder that keeps nothing: the recipe is read from
    /// <see cref="DeploymentPlanResult.Recipe"/>, not from a file on disk.
    /// </summary>
    private sealed class NothingFileBuilder : IFileBuilder
    {
        public Task SetFileAsync(string subpath, Stream stream) => Task.CompletedTask;
    }
}
