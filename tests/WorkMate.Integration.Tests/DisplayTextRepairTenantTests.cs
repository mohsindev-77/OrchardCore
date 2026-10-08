using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.Data.Migration;
using OrchardCore.Data.Migration.Records;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Every record and every employee carries the display text Orchard's own screens show them by,
/// and the ones written before that was true are repaired.
/// </summary>
/// <remarks>
/// <b>The defect.</b> Both content types bind <c>TitlePart</c> to the English name with
/// <c>TitlePartOptions.GeneratedDisabled</c> and a Liquid pattern, which is what specification
/// section 4 asks for and what the dimension engine has done since its record layer landed. On a
/// real tenant it produces nothing: <c>TitlePartHandler</c> renders the pattern through
/// <c>ILiquidTemplateManager</c>, and that throws a <c>NullReferenceException</c> outside an HTTP
/// request — so every record created by a service, a recipe import or a background job was left
/// with a null <c>DisplayText</c>, which is every record on a tenant seeded from a recipe.
///
/// It is silent. The record is correct in every other respect and simply has no name on Orchard's
/// content list, in a content picker, or anywhere else reading <c>DisplayText</c>. Nothing in
/// WorkMate reads it, which is why it survived three prompts.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DisplayTextRepairTenantTests
{
    private const string DimensionsMigration = "WorkMate.Dimensions.Migrations";
    private const string RecordsMigration = "WorkMate.Records.Migrations";

    private readonly BaseTenantFixture _fixture;

    public DisplayTextRepairTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task ANewDimensionRecordCarriesItsEnglishNameAsItsDisplayText() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var id = await DimensionGraphScenario.RecordAsync(services, types.Department, "dt-new-dep", Opened);

            var item = await services.GetRequiredService<IContentManager>().GetAsync(id);

            item!.DisplayText.Should().Be(
                "dt-new-dep",
                "the scenario names a record after its code, and the title follows the English name");
        });

    [Fact]
    public async Task ANewEmployeeCarriesTheirEnglishNameAsTheirDisplayText() =>
        await InTenantAsSystemAsync(async services =>
        {
            var id = await EmployeeScenario.CreateAsync(
                services, "dt-new-emp", nameEn: "Farhan Siddiqui", nameAr: "فرحان صديقي");

            var item = await services.GetRequiredService<IContentManager>().GetAsync(id);

            item!.DisplayText.Should().Be("Farhan Siddiqui");
        });

    /// <summary>
    /// A rename moves the display text with it, so the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// The whole reason the title is bound to the name rather than typed separately. A rename that
    /// left the old name on every picker would be worse than no title at all, because it would look
    /// right.
    /// </remarks>
    [Fact]
    public async Task RenamingAUnitMovesItsDisplayTextWithIt() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var records = services.GetRequiredService<IDimensionService>();

            var id = await DimensionGraphScenario.RecordAsync(services, types.Department, "dt-rename-dep", Opened);

            var renamed = await records.RenameAsync(
                id, new BilingualText("Support Services", "خدمات الدعم"), new DateOnly(2026, 4, 1));

            renamed.Succeeded.Should().BeTrue(
                string.Join("; ", renamed.Errors.Select(error => error.Message.Value)));

            var item = await services.GetRequiredService<IContentManager>().GetAsync(id);

            item!.DisplayText.Should().Be("Support Services");
        });

    [Fact]
    public async Task ChangingAnEmployeesNameMovesTheirDisplayTextWithIt() =>
        await InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "dt-rename-emp", nameEn: "Before");

            var updated = await employees.UpdateAsync(
                id, new BilingualText("After Marriage", "بعد الزواج"), new EmployeeDetails());

            updated.Succeeded.Should().BeTrue(updated.Describe());

            var item = await services.GetRequiredService<IContentManager>().GetAsync(id);

            item!.DisplayText.Should().Be("After Marriage");
        });

    /// <summary>
    /// A tenant whose records predate the fix is repaired by upgrading, not by being rebuilt.
    /// </summary>
    /// <remarks>
    /// The migration is data rather than schema, so "stuck before the fix" is reproduced by
    /// blanking the stored titles and rolling the recorded version back — which is exactly the state
    /// a tenant seeded by a recipe before this landed is in.
    ///
    /// Asserted on a record whose title was <em>already</em> set as well, because the step must fill
    /// what is absent and never overwrite what is there: a customer who typed a title by hand keeps
    /// it.
    /// </remarks>
    [Fact]
    public async Task DimensionRecordsWrittenBeforeTheFixAreRepairedByUpgrading()
    {
        string blanked = null!;
        string kept = null!;

        await InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);

            blanked = await DimensionGraphScenario.RecordAsync(services, types.Department, "dt-repair-blank", Opened);
            kept = await DimensionGraphScenario.RecordAsync(services, types.Department, "dt-repair-kept", Opened);

            var session = services.GetRequiredService<ISession>();

            var items = await session
                .Query<ContentItem, DimensionRecordPartIndex>(index =>
                    index.ContentItemId == blanked || index.ContentItemId == kept)
                .ListAsync();

            foreach (var item in items)
            {
                // Exactly what the tenant held before the handler existed, and a hand-typed title
                // beside it so the step has something it must not touch.
                item.DisplayText = item.ContentItemId == blanked ? null! : "Set by hand";

                await session.SaveAsync(item);
            }

            await RollBackAsync(session, DimensionsMigration, 6);
        });

        await InTenantAsSystemAsync(async services =>
        {
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            var content = services.GetRequiredService<IContentManager>();

            (await content.GetAsync(blanked))!.DisplayText.Should().Be(
                "dt-repair-blank", "the upgrade fills a title that was never generated");

            (await content.GetAsync(kept))!.DisplayText.Should().Be(
                "Set by hand", "and never overwrites one somebody already has");
        });
    }

    [Fact]
    public async Task EmployeesWrittenBeforeTheFixAreRepairedByUpgrading()
    {
        string blanked = null!;

        await InTenantAsSystemAsync(async services =>
        {
            blanked = await EmployeeScenario.CreateAsync(
                services, "dt-repair-emp", nameEn: "Repaired Person");

            var session = services.GetRequiredService<ISession>();

            var item = (await session
                .Query<ContentItem, EmployeeIndex>(index => index.ContentItemId == blanked)
                .ListAsync())
                .Single();

            item.DisplayText = null!;

            await session.SaveAsync(item);

            await RollBackAsync(session, RecordsMigration, 1);
        });

        await InTenantAsSystemAsync(async services =>
        {
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            (await services.GetRequiredService<IContentManager>().GetAsync(blanked))!
                .DisplayText.Should().Be("Repaired Person");
        });
    }

    /// <summary>
    /// Rolls one module's recorded schema version back, so the next catch-up re-runs from there.
    /// </summary>
    /// <remarks>
    /// Safe for a data-repair step in a way it would not be for one that creates a table: re-running
    /// this one writes nothing the second time. A step that created a table needs its table dropped
    /// too — see <c>DimensionsMigrationUpgradeTenantTests.TablesByVersion</c>.
    /// </remarks>
    private static async Task RollBackAsync(ISession session, string migrationClass, int version)
    {
        var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
        var migration = record!.DataMigrations.Single(entry => entry.DataMigrationClass == migrationClass);

        migration.Version.Should().BeGreaterThan(
            version, "this test's premise is a tenant that has already run the step being replayed");

        migration.Version = version;

        session.Save(record);
        await session.SaveChangesAsync();
    }
}
