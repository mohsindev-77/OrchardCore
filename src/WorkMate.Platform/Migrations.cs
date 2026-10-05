using System.Diagnostics.CodeAnalysis;
using OrchardCore.Data.Migration;
using OrchardCore.Entities;
using OrchardCore.Localization.Models;
using OrchardCore.Settings;
using WorkMate.Platform.Models;

namespace WorkMate.Platform;

/// <summary>
/// Schema for WorkMate.Platform. Migrations are additive and append-only: a migration never
/// destroys data, and a step that has shipped is never edited — see CLAUDE.md and ADR-0009.
/// </summary>
public sealed class Migrations : DataMigration
{
    private readonly ISiteService _siteService;

    public Migrations(ISiteService siteService) => _siteService = siteService;

    /// <summary>
    /// The platform owns no tables. WorkMateSettings is a site settings section, which Orchard
    /// stores inside the site document, so it needs no schema of its own. This method exists so
    /// that the module has a migration record from its first release and later versions can be
    /// added as UpdateFrom1Async onwards.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Orchard discovers migration methods by reflection on the DataMigration instance, so CreateAsync cannot be static.")]
    public Task<int> CreateAsync() => Task.FromResult(1);

    /// <summary>
    /// Makes Arabic a supported culture on a tenant that does not already have it.
    /// </summary>
    /// <remarks>
    /// Every name on this platform is bilingual and every admin screen is built to render
    /// right-to-left, which is worth nothing on a tenant whose culture picker offers only English.
    /// <c>base.recipe.json</c> has set <c>SupportedCultures</c> to <c>["en", "ar"]</c> since the
    /// first commit, so a tenant set up from it has both — but a tenant set up some other way, or
    /// from a setup that did not complete, has only whatever Orchard defaulted to, and nothing
    /// would ever give it Arabic. Found when Arabic could not be selected on a real dev tenant.
    ///
    /// Additive, like every migration here: Arabic is appended to whatever the tenant already
    /// supports, the default culture is left alone, and a tenant that already has Arabic is left
    /// untouched. A customer who has deliberately removed Arabic gets it back once, which is the
    /// cost of making the guarantee hold for every tenant; removing it again is a settings change,
    /// not a schema one.
    /// </remarks>
    public async Task<int> UpdateFrom1Async()
    {
        var site = await _siteService.LoadSiteSettingsAsync();
        // GetOrCreate, not the obsolete As: a tenant that has never had the section at all is
        // exactly the tenant this step exists for.
        var localization = site.GetOrCreate<LocalizationSettings>();

        var supported = localization.SupportedCultures ?? [];

        if (supported.Contains(WorkMateSettings.ArabicCultureName, StringComparer.OrdinalIgnoreCase))
        {
            return 2;
        }

        localization.SupportedCultures = [.. supported, WorkMateSettings.ArabicCultureName];

        // A tenant with no supported cultures at all has no default either; without one, Orchard
        // has nothing to fall back to and the picker stays empty whatever is added to the list.
        if (string.IsNullOrEmpty(localization.DefaultCulture))
        {
            localization.DefaultCulture = WorkMateSettings.DefaultCultureName;
        }

        site.Put(localization);
        await _siteService.UpdateSiteSettingsAsync(site);

        return 2;
    }
}
