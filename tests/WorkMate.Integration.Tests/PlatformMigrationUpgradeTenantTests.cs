using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Data.Migration;
using OrchardCore.Data.Migration.Records;
using OrchardCore.Entities;
using OrchardCore.Localization.Models;
using OrchardCore.Settings;
using WorkMate.Platform.Models;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Reproduces a tenant on which Arabic could not be chosen at all: the admin culture picker offered
/// only English, because the tenant's <c>LocalizationSettings</c> predated
/// <c>base.recipe.json</c> listing <c>["en", "ar"]</c> and nothing afterwards ever added it.
/// </summary>
/// <remarks>
/// Every name on this platform is bilingual and every screen is built to mirror right to left, none
/// of which a customer can reach if their tenant does not support the culture. The recipe fixes new
/// tenants; <c>WorkMate.Platform.Migrations.UpdateFrom1Async</c> is what fixes the ones that already
/// exist, and this is the upgrade test CLAUDE.md requires for it.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class PlatformMigrationUpgradeTenantTests
{
    private const string MigrationClass = "WorkMate.Platform.Migrations";

    private readonly BaseTenantFixture _fixture;

    public PlatformMigrationUpgradeTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    // What a tenant set up from base.recipe.json supports is asserted where it is deterministic:
    // BaseRecipeTests.BothPlatformCulturesAreConfigured pins the recipe, and the browser suite
    // switches a freshly set-up tenant's admin to Arabic, which only works if the culture is
    // really there. Asserting it here as well would be order-dependent, because the tests below
    // rewrite the very settings it would read.

    [Fact]
    public async Task ATenantWithoutArabicGetsItBackWhenTheApplicationRestarts()
    {
        // Each step in its own scope, the way a real tenant experiences this: a tenant sits on the
        // old settings, the application restarts, and the migration runs against what is already
        // committed. Doing both in one scope writes the site document twice and the first write
        // wins, which is a quirk of writing settings in the same scope that runs the migration and
        // not something that can happen at startup.
        await _fixture.InTenantAsync(RollBackToEnglishOnlyAsync);

        await _fixture.InTenantAsync(async services =>
        {
            (await LocalizationSettingsAsync(services)).SupportedCultures.Should().NotContain(
                WorkMateSettings.ArabicCultureName, "this test's premise is a tenant without Arabic");

            // Exactly what restarting the application does, rather than calling the step directly:
            // this is the discovery-and-catch-up path a real tenant upgrades through.
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await _fixture.InTenantAsync(async services =>
        {
            var localization = await LocalizationSettingsAsync(services);

            localization.SupportedCultures.Should().Contain(
                WorkMateSettings.ArabicCultureName,
                "a tenant that restarts after this step must be able to offer Arabic");

            localization.SupportedCultures.Should().Contain(
                WorkMateSettings.DefaultCultureName,
                "the step adds Arabic, it does not replace what the tenant already supported");

            localization.DefaultCulture.Should().Be(WorkMateSettings.DefaultCultureName);

            (await VersionAsync(services)).Should().Be(2);
        });
    }

    /// <summary>
    /// Running the step on a tenant that already has Arabic changes nothing — no second entry, no
    /// reordering, no overwritten default. Migrations here are additive, and an operator who
    /// upgrades twice must not end up with a culture list that grows each time.
    /// </summary>
    [Fact]
    public async Task RunningTheUpgradeOnATenantThatAlreadyHasArabicChangesNothing()
    {
        string[] cultures = [WorkMateSettings.DefaultCultureName, WorkMateSettings.ArabicCultureName];

        // Set explicitly rather than relying on what the recipe left behind: this class's other
        // test rewrites the same settings, and the two share one tenant.
        await _fixture.InTenantAsync(services => SetCulturesAsync(services, cultures));

        await _fixture.InTenantAsync(async services =>
        {
            await SetVersionAsync(services, 1);
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await _fixture.InTenantAsync(async services =>
        {
            var after = await LocalizationSettingsAsync(services);

            after.SupportedCultures.Should().Equal(cultures);
            after.DefaultCulture.Should().Be(WorkMateSettings.DefaultCultureName);
            (await VersionAsync(services)).Should().Be(2);
        });
    }

    /// <summary>
    /// Puts the tenant back in the state the defect was found in: the platform migration recorded
    /// as never having run past its first version, and a culture list with English only.
    /// </summary>
    private static async Task RollBackToEnglishOnlyAsync(IServiceProvider services)
    {
        await SetCulturesAsync(services, [WorkMateSettings.DefaultCultureName]);
        await SetVersionAsync(services, 1);
    }

    private static async Task SetCulturesAsync(IServiceProvider services, string[] cultures)
    {
        var siteService = services.GetRequiredService<ISiteService>();
        var site = await siteService.LoadSiteSettingsAsync();
        var localization = site.GetOrCreate<LocalizationSettings>();

        localization.SupportedCultures = cultures;
        localization.DefaultCulture = WorkMateSettings.DefaultCultureName;

        site.Put(localization);
        await siteService.UpdateSiteSettingsAsync(site);
    }

    private static async Task<LocalizationSettings> LocalizationSettingsAsync(IServiceProvider services) =>
        (await services.GetRequiredService<ISiteService>().LoadSiteSettingsAsync())
            .GetOrCreate<LocalizationSettings>();

    private static async Task SetVersionAsync(IServiceProvider services, int version)
    {
        var session = services.GetRequiredService<ISession>();
        var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();

        record.Should().NotBeNull();

        var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);
        migration.Version = version;

        session.Save(record);
        await session.SaveChangesAsync();
    }

    private static async Task<int?> VersionAsync(IServiceProvider services)
    {
        var record = await services.GetRequiredService<ISession>().Query<DataMigrationRecord>().FirstOrDefaultAsync();

        return record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass).Version;
    }
}
