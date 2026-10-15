using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The tenant that could not get its positions: built from the Zenith recipe as it stood before
/// slices F1 and F2, and brought up to date by a recipe that only adds.
/// </summary>
/// <remarks>
/// <b>This reproduces a real failure found in manual testing, not a hypothetical one.</b> On a
/// tenant built from an earlier version of the Zenith demo, Configuration → Recipes → the Zenith
/// recipe failed at <c>dimension-types</c> on every row at once:
///
/// <code>
/// zenith-division:   menuOrder 0 in tenant vs 10 in recipe
/// zenith-department: allowsSelfNesting True vs False; menuOrder 0 vs 20
/// zenith-project:    menuOrder 0 vs 40
/// zenith-team:       allowsSelfNesting True vs False; menuOrder 0 vs 50
/// </code>
///
/// Slice F5+F7 had put the positions and grades in that same file, so the tenants that already had
/// the organisation to hang them on were exactly the ones that could never receive them. "Existing
/// tenants are unaffected unless the recipe is re-applied" turned out to describe the case that
/// fails.
///
/// <b>Three things fix it, and this asserts all three.</b> The positions and grades are a second
/// recipe that states only new things. A stored <c>menuOrder</c> of zero is a gap to fill rather
/// than a difference, because the field did not exist when those types were made. And
/// <c>allowsSelfNesting</c> is still refused — deliberately, see
/// <see cref="TheMainRecipeStillRefusesOnSelfNestingAndSaysOnlyThat"/>.
/// </remarks>
public sealed class ZenithPositionsRecipeTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    private static readonly DateOnly Today = new(2026, 10, 7);

    /// <summary>The Zenith recipe as it was before F1 and F2, from the test project's own folder.</summary>
    private const string LegacyZenith = "organisation-designer-zenith-legacy.recipe.json";

    private const string Positions = "organisation-designer-zenith-positions.recipe.json";

    private const string Zenith = "organisation-designer-zenith.recipe.json";

    /// <summary>The four dimension types the legacy recipe creates, and the only ones it has.</summary>
    private static readonly string[] LegacyTypeCodes =
        ["zenith-division", "zenith-department", "zenith-project", "zenith-team"];

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        // The tenant as it was built, from the file it was built from.
        await ApplyAsync(LegacyZenith, fromTestFixtures: true);

        await BackfillPluralsTheWayAnUpgradeWouldAsync();
    }

    /// <summary>
    /// Gives the legacy types the plurals <c>Migrations.UpdateFrom11Async</c> would have given
    /// them, because a test tenant cannot be upgraded in the order a real one was.
    /// </summary>
    /// <remarks>
    /// <b>Without this the fixture is not the reported tenant.</b> The real one was built by the
    /// old recipe and <em>then</em> upgraded, so decision D24's plural back-fill found its four
    /// types and filled them from <see cref="StandardDimensionPlurals"/> — which is why the error
    /// that was reported names <c>menuOrder</c> and <c>allowsSelfNesting</c> and says nothing about
    /// plurals. A test tenant is fully migrated the moment it is created and the recipe runs after
    /// that, so the back-fill never sees these types and they are left with no plural at all.
    ///
    /// That difference is an artefact of the order a test can build a tenant in, not a difference
    /// in the product, and leaving it would make this file assert a failure nobody actually had.
    /// It goes through the same frozen table the migration reads, so it cannot drift from it.
    /// </remarks>
    private Task BackfillPluralsTheWayAnUpgradeWouldAsync() =>
        InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            foreach (var code in LegacyTypeCodes)
            {
                var type = (await types.GetByCodeAsync(code))!;
                var plural = StandardDimensionPlurals.For(type.Name);

                var filled = await types.UpdateAsync(
                    type.DimensionTypeId,
                    type.Name,
                    type.AttributeSchema,
                    type.AllowsSelfNesting,
                    new DimensionTypeMenu(type.ShowInMenu, plural, type.MenuOrder));

                filled.Succeeded.Should().BeTrue();
            }
        });

    public Task DisposeAsync() => _tenant.DisposeAsync();

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("legacy zenith test"))
            {
                await work(services);
            }
        });

    /// <summary>The tenant really is in the state that was reported, before anything is fixed.</summary>
    /// <remarks>
    /// Asserted first and on its own, because every other test here is only interesting if the
    /// starting point is the one that failed. A fixture that had quietly drifted into the current
    /// recipe's shape would make the rest of this file pass by describing nothing.
    /// </remarks>
    [Fact]
    public async Task TheTenantStartsInTheStateThatWasReported() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var division = await types.GetByCodeAsync("zenith-division");
            var department = await types.GetByCodeAsync("zenith-department");

            division!.MenuOrder.Should().Be(0, "the field did not exist when this tenant was built");
            department!.MenuOrder.Should().Be(0);

            department.AllowsSelfNesting.Should().BeTrue(
                "which is what the reported tenant holds, whatever put it there");

            // And the consequence of that flag, which is what actually refused the second attempt:
            // the map carries self-pairs no current row restates.
            var organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!;

            organisation.Permits(department.DimensionTypeId, department.DimensionTypeId).Should().BeTrue(
                "a tenant built when Department self-nested has department > department in its map");

            (await types.GetByCodeAsync("zenith-position")).Should().BeNull("no positions yet");
            (await types.GetByCodeAsync("zenith-grade")).Should().BeNull("and no grades");
        });

    /// <summary>
    /// The positions-and-grades recipe applies to that tenant and brings the whole slice with it.
    /// </summary>
    /// <remarks>
    /// <b>The acceptance test for the fix.</b> Nothing is edited by hand first: the tenant is the
    /// one the legacy recipe built, and one click on one recipe has to be enough.
    /// </remarks>
    [Fact]
    public async Task TheNewRecipeAppliesToThatTenantAndBringsGradesPositionsAndTheReportingLine()
    {
        await ApplyAsync(Positions);

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var structures = services.GetRequiredService<IStructureService>();
            var positions = services.GetRequiredService<IPositionService>();

            // The two dimensions, with the roles that are how the product finds them.
            (await types.GetByCodeAsync("zenith-grade"))!.StandardRole.Should().Be(DimensionTypeRole.Grade);
            (await types.GetByCodeAsync("zenith-position"))!.StandardRole.Should().Be(DimensionTypeRole.Position);

            // The grades.
            (await records.ListByTypeAsync((await types.GetByCodeAsync("zenith-grade"))!.DimensionTypeId))
                .Should().HaveCount(19);

            // The positions, and their establishment.
            var hrManager = (await records.GetByCodeAsync("zenith-pos-hr-manager"))!.RecordId;

            (await positions.GetEstablishmentAsync([hrManager], Today))[hrManager]
                .Approved.Should().Be(3, "one HR Manager post with three approved holders (D26 D)");

            (await positions.GetAllowedGradesAsync(hrManager, Today))
                .Select(grade => grade.NameEn).Should().Equal(["HRM-1", "HRM-2", "HRM-3"]);

            // The reporting line, which is the second axis ADR-0028 adds.
            var reporting = (await structures.GetByCodeAsync("zenith-reporting"))!.StructureId;
            var ceo = (await records.GetByCodeAsync("zenith-pos-ceo"))!.RecordId;

            (await graph.GetAncestorsAsync(reporting, ceo, Today)).Should().BeEmpty("the CEO reports to nobody");

            (await graph.GetChildrenAsync(reporting, ceo, Today)).Select(child => child.Code)
                .Should().Equal(["zenith-pos-dir-tech"]);

            // And the organisation axis was EXTENDED rather than replaced: the four levels the
            // tenant had are still there, in order, with the new one appended.
            (await structures.GetByCodeAsync("zenith-org"))!.DimensionTypeIds
                .Select(async id => (await types.GetAsync(id))!.Code)
                .Select(task => task.Result)
                .Should().Equal(
                    ["zenith-division", "zenith-department", "zenith-project", "zenith-team", "zenith-position"],
                    "a row that keeps every level and adds one is extending the axis");

            // The positions really are in the business, under the departments that were already there.
            var organisation = (await structures.GetByCodeAsync("zenith-org"))!.StructureId;

            var path = (await graph.GetAncestorPathsAsync(organisation, [hrManager], Today))[hrManager];

            path[^1].Code.Should().Be("zenith-dept-hr");

            // And the tenant's own map survived intact. The self-pairs it carries are the ones that
            // refused the describing form; an additive row has no opinion about them.
            var chart = (await structures.GetByCodeAsync("zenith-org"))!;
            var departmentType = (await types.GetByCodeAsync("zenith-department"))!.DimensionTypeId;
            var teamType = (await types.GetByCodeAsync("zenith-team"))!.DimensionTypeId;

            chart.Permits(departmentType, departmentType).Should().BeTrue(
                "an additive row leaves what it did not mention exactly as the tenant has it");

            chart.Permits(teamType, teamType).Should().BeTrue();

            chart.ShortName.En.Should().BeEmpty("and it did not acquire a short name it never had");
        });
    }

    /// <summary>The CEO's page draws the reporting chart on that tenant, by URL and then by eye.</summary>
    [Fact]
    public async Task TheCeoPageDrawsTheReportingChartOnThatTenant()
    {
        await ApplyAsync(Positions);

        var ceo = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            ceo = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync("zenith-pos-ceo"))!.RecordId;
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Dimensions/Records/Details/{ceo}?view=hierarchy");

        page.Should().Contain("value-hierarchy-establishment", "filled over approved on every box");
        page.Should().Contain("Director Technology", "the reporting line below the CEO");

        // The Headcount row, which replaced the Establishment row and the Employees row and the
        // Approved headcount attribute row — one read, one row, three numbers.
        page.Should().Contain("value-headcount");
        page.Should().Contain("1 approved");
    }

    /// <summary>
    /// A tenant where an earlier failed attempt already created the two dimension types is picked
    /// up where it left off.
    /// </summary>
    /// <remarks>
    /// <b>The state a failed run actually leaves behind.</b> A recipe is not one transaction —
    /// ADR-0008 says so in terms, and the alternative was considered and rejected — so a run that
    /// got through <c>dimension-types</c> and then failed at <c>structures</c> leaves the two types
    /// created and nothing else. That is exactly what the reported tenant was in after the second
    /// attempt, and a fix that only worked from a clean start would not reach it.
    ///
    /// Nothing special makes this work: the types step already skips a row whose stored content
    /// matches, and the additive structures row has no opinion about what else is there. Asserted
    /// because "already safe" and "happens to be safe today" look identical until something changes.
    /// </remarks>
    [Fact]
    public async Task APartlyAppliedAttemptIsPickedUpWhereItLeftOff()
    {
        // What the first step of a failed run would have left: the two types, and nothing else.
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var position = await types.CreateAsync(
                "zenith-position",
                new BilingualText("Position", "منصب"),
                [new DimensionAttributeDefinition(
                    PositionEstablishment.ApprovedHeadcountAttribute,
                    new BilingualText("Approved headcount", "العدد المعتمد"),
                    DimensionAttributeKind.Number,
                    IsRequired: false)],
                allowsSelfNesting: false,
                menu: new DimensionTypeMenu(ShowInMenu: true, new BilingualText("Positions", "المناصب"), MenuOrder: 60),
                standardRole: DimensionTypeRole.Position);

            position.Succeeded.Should().BeTrue(
                string.Join("; ", position.Errors.Select(error => error.Message.Value)));

            var grade = await types.CreateAsync(
                "zenith-grade",
                new BilingualText("Grade", "درجة"),
                [],
                allowsSelfNesting: true,
                menu: new DimensionTypeMenu(ShowInMenu: true, new BilingualText("Grades", "الدرجات"), MenuOrder: 70),
                standardRole: DimensionTypeRole.Grade);

            grade.Succeeded.Should().BeTrue(
                string.Join("; ", grade.Errors.Select(error => error.Message.Value)));
        });

        await ApplyAsync(Positions);

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            // One of each, not two: the types step recognised the rows it had already written.
            (await types.ListAsync()).Count(type => type.Code == "zenith-position").Should().Be(1);
            (await types.ListAsync()).Count(type => type.Code == "zenith-grade").Should().Be(1);

            (await records.ListByTypeAsync((await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId))
                .Should().HaveCount(8, "and the rest of the recipe ran");

            (await records.GetByCodeAsync("zenith-pos-ceo")).Should().NotBeNull();
        });
    }

    /// <summary>
    /// An additive row naming a structure that is not there is refused, and says which.
    /// </summary>
    /// <remarks>
    /// An additive row extends an axis another recipe built; it never invents one. Running the
    /// positions file on a tenant with no Zenith organisation is the ordinary way somebody meets
    /// this — by applying the two in the wrong order — so the message names the structure rather
    /// than failing somewhere further down on a missing department.
    /// </remarks>
    [Fact]
    public async Task AnAdditiveRowIsRefusedWhenTheStructureIsNotThere()
    {
        using var bare = new BaseTenantFixture();

        await bare.InitializeAsync();

        var failure = await Record.ExceptionAsync(() => ApplyToAsync(bare, Positions));

        failure.Should().NotBeNull("the organisation recipe has to run first");
        failure!.ToString().Should().Contain("zenith-org");
        failure.ToString().Should().Contain("does not exist");

        await bare.DisposeAsync();
    }

    /// <summary>Applying it twice changes nothing and refuses nothing.</summary>
    [Fact]
    public async Task TheNewRecipeReRunsCleanOnThatTenant()
    {
        await ApplyAsync(Positions);
        await ApplyAsync(Positions);

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            (await records.ListByTypeAsync((await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId))
                .Should().HaveCount(8, "a re-run creates nothing a second time");

            (await records.ListByTypeAsync((await types.GetByCodeAsync("zenith-grade"))!.DimensionTypeId))
                .Should().HaveCount(19);

            // And the second pass did not re-extend the axis into a duplicate level.
            (await services.GetRequiredService<IStructureService>().GetByCodeAsync("zenith-org"))!
                .DimensionTypeIds.Should().OnlyHaveUniqueItems();
        });
    }

    /// <summary>
    /// The main Zenith recipe now gets past menu order, and still refuses on self-nesting —
    /// deliberately.
    /// </summary>
    /// <remarks>
    /// <b>Both halves are the decision.</b>
    ///
    /// <b>menuOrder is filled.</b> It arrived with slice F2, so a type created before it holds zero
    /// because nobody could have said otherwise. ADR-0008's 14 October addendum reads that as a gap.
    ///
    /// <b>allowsSelfNesting is still refused, and no migration aligns it.</b> Three reasons, and the
    /// first is the one that settles it. <b>Nothing can establish what set it</b>: no shipped version
    /// of this recipe ever wrote <c>true</c>, no migration writes the flag, and the only other writer
    /// was a checkbox on the Dimension types screen that ADR-0010's addendum removed — so on this
    /// tenant the value is as likely to be somebody's deliberate tick as a stale default, and a
    /// migration that flipped it would be the product overwriting a customer's configuration to make
    /// a demo file re-appliable. <b>Nothing reads it</b>: since that addendum the structure's
    /// containment map is the only authority on self-nesting, and the one remaining reader is a
    /// chain-shaped <c>structures</c> row, which Zenith's is not — so the divergence changes nothing
    /// a reader sees or a rule enforces. And <b>nothing needs it</b>: the positions-and-grades recipe
    /// does not restate those types, so it applies regardless, which is the outcome that was actually
    /// wanted.
    ///
    /// What this asserts is that the refusal is now about that one field and no longer about four
    /// types at once — so a reader meeting it is told something true and narrow.
    /// </remarks>
    [Fact]
    public async Task TheMainRecipeStillRefusesOnSelfNestingAndSaysOnlyThat()
    {
        var failure = await Record.ExceptionAsync(() => ApplyAsync(Zenith));

        failure.Should().NotBeNull("allowsSelfNesting is a real divergence and is still refused");

        var message = failure!.ToString();

        message.Should().Contain("allowsSelfNesting");
        message.Should().NotContain("menuOrder", "a stored zero is a gap to fill, not a difference");

        message.Should().Contain("zenith-department");
        message.Should().Contain("zenith-team");
        message.Should().NotContain("zenith-division", "its only divergence was the menu order");
        message.Should().NotContain("zenith-project", "the same");
    }

    /// <summary>
    /// A type whose menu order somebody really did choose is still refused when it differs.
    /// </summary>
    /// <remarks>
    /// The other side of the gap rule, and the reason it is safe. Zero is read as "nobody said";
    /// any other value is read as a decision, and a recipe does not get to overwrite one.
    /// </remarks>
    [Fact]
    public async Task AMenuOrderSomebodyChoseIsStillRefused()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var division = (await types.GetByCodeAsync("zenith-division"))!;

            var ordered = await types.UpdateAsync(
                division.DimensionTypeId,
                division.Name,
                division.AttributeSchema,
                division.AllowsSelfNesting,
                new DimensionTypeMenu(division.ShowInMenu, division.PluralName, MenuOrder: 95));

            ordered.Succeeded.Should().BeTrue();
        });

        var failure = await Record.ExceptionAsync(() => ApplyAsync(Zenith));

        failure.Should().NotBeNull();
        failure!.ToString().Should().Contain(
            "menuOrder is 95 in the tenant but 10 in the recipe",
            "a value somebody typed is a decision, and a re-run does not overwrite one");
    }

    // ---- helpers ----------------------------------------------------------------------------

    private Task ApplyAsync(string fileName, bool fromTestFixtures = false) =>
        ApplyToAsync(_tenant, fileName, fromTestFixtures);

    private static async Task ApplyToAsync(
        BaseTenantFixture tenant,
        string fileName,
        bool fromTestFixtures = false)
    {
        var folder = fromTestFixtures ? TestFixtureRecipesFolder : ModuleRecipesFolder;
        var path = Path.Combine(folder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' is where this test looks for it", fileName);

        await tenant.InTenantAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(folder);

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

    private static string ModuleRecipesFolder =>
        Path.Combine(RepositoryRoot, "src", "WorkMate.Dimensions", "Recipes");

    private static string TestFixtureRecipesFolder =>
        Path.Combine(RepositoryRoot, "tests", "WorkMate.Integration.Tests", "Recipes");

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
}
