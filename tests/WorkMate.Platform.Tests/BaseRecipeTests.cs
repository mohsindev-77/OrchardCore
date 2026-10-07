using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using WorkMate.Platform;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Structural checks on the base recipe. They run in the unit suite because they are fast and
/// catch the cheap mistakes early; whether the recipe actually applies is a different question,
/// answered by applying it to a fresh tenant in the pipeline.
/// </summary>
public sealed class BaseRecipeTests
{
    [Fact]
    public void TheBaseRecipeIsTheSetupRecipe() =>
        Recipe["issetuprecipe"]!.GetValue<bool>().Should().BeTrue(
            "the base recipe is what a new tenant is created with");

    [Fact]
    public void TheSixStepsOfSectionThreeAreAllPresentAndInOrder()
    {
        var names = Steps.Select(step => step!["name"]!.GetValue<string>()).ToList();

        names.Should().ContainInOrder(
            "Feature",          // 1. enable the platform and entitled module features
            "Roles",            // 2. create the platform roles
            "Settings",         // 3. default site settings and the en/ar cultures
            "dimension-types",  // 4. system dimension types and the Organisation structure
            "structures",
            "form-definitions", // 5. the standard content types
            "reports");         // 6. the standard report library
    }

    [Fact]
    public void TheRecipeCreatesExactlyTheSixPlatformRoles()
    {
        var roles = RoleStep["Roles"]!.AsArray()
            .Select(role => role!["Name"]!.GetValue<string>())
            .ToList();

        roles.Should().BeEquivalentTo(PlatformRoles.All);
    }

    [Fact]
    public void EveryRoleAddsItsPermissionsRatherThanReplacingThem()
    {
        // The Roles step defaults to Replace. Left at the default, an edition recipe layered on
        // top of the base would wipe these grants instead of adding to them, which contradicts
        // section 3: "Edition recipes and sector packs layer on top; they never replace it."
        foreach (var role in RoleStep["Roles"]!.AsArray())
        {
            role!["PermissionBehavior"]!.GetValue<string>().Should().Be(
                "Add",
                "{0} would otherwise be reset by any recipe that runs after the base",
                role["Name"]!.GetValue<string>());
        }
    }

    [Fact]
    public void EveryRoleSaysWhatItIsFor() =>
        RoleStep["Roles"]!.AsArray().Should().AllSatisfy(role =>
            role!["Description"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace());

    [Fact]
    public void BothPlatformCulturesAreConfigured()
    {
        var localization = SettingsStep["LocalizationSettings"]!;

        localization["SupportedCultures"]!.AsArray()
            .Select(culture => culture!.GetValue<string>())
            .Should().BeEquivalentTo(["en", "ar"]);

        localization["DefaultCulture"]!.GetValue<string>().Should().Be("en");
    }

    [Fact]
    public void ThePlatformSettingsTheRecipeWritesMatchTheDefaultsInCode()
    {
        // If these drift apart, a tenant created by the recipe and a tenant that never opened the
        // settings screen behave differently, which is the sort of difference nobody finds until
        // payroll disagrees with leave.
        var settings = SettingsStep["WorkMateSettings"]!;
        var defaults = new Models.WorkMateSettings();

        settings["CurrencyCode"]!.GetValue<string>().Should().Be(defaults.CurrencyCode);
        settings["WeekStartsOn"]!.GetValue<string>().Should().Be(defaults.WeekStartsOn.ToString());
        settings["FiscalYearStartMonth"]!.GetValue<int>().Should().Be(defaults.FiscalYearStartMonth);
        settings["FiscalYearStartDay"]!.GetValue<int>().Should().Be(defaults.FiscalYearStartDay);

        settings["WorkingDays"]!.AsArray()
            .Select(day => day!.GetValue<string>())
            .Should().BeEquivalentTo(defaults.WorkingDays.Select(day => day.ToString()));
    }

    [Fact]
    public void NoObsoleteOrchardFeatureIsEnabled()
    {
        // Verified against the pinned version with tools/api-probe --features: in 3.0.1 these
        // ids still resolve but are named "(Obsolete)" and will go. See ADR-0004.
        string[] obsolete = ["OrchardCore.Search.Lucene", "OrchardCore.Search.Elasticsearch"];

        EnabledFeatures.Should().NotIntersectWith(obsolete);
    }

    [Fact]
    public void TheFeaturesEveryOtherStepNeedsAreEnabled()
    {
        // Each of these is what makes a later step in this same recipe work at all.
        EnabledFeatures.Should().Contain("OrchardCore.Roles");        // the Roles step
        EnabledFeatures.Should().Contain("OrchardCore.Settings");     // the Settings step
        EnabledFeatures.Should().Contain("OrchardCore.Localization"); // the cultures
        EnabledFeatures.Should().Contain("WorkMate.Platform");        // WorkMateSettings
    }

    [Fact]
    public void PermissionDescriptionsCanBeTranslated() =>
        // UserVisibleStringTests excuses permission descriptions from the localiser on the
        // grounds that OrchardCore.DataLocalization translates them instead. That excuse only
        // holds while the base recipe actually enables it.
        EnabledFeatures.Should().Contain(
            "OrchardCore.DataLocalization",
            "permission descriptions are localised as data, and nothing else localises them");

    [Fact]
    public void BothThemesAreSelectedAndBothAreAlsoEnabled()
    {
        // Found by applying the recipe, twice over. A theme needs enabling as a feature *and*
        // selecting in the themes step, and neither half announces that the other is missing:
        // enabled but not selected left the admin with no layout at all, and selected but not
        // enabled made Orchard fall back to the admin theme for the front end, where TheAdmin has
        // no Layout shape, so /Login threw and the tenant could not be signed into.
        //
        // TheTheme is a placeholder for the site until WorkMate.Hcm.SelfService ships the employee
        // front end; when it does, both the selection and the feature change together.
        var themes = StepNamed("themes");

        var admin = themes["admin"]!.GetValue<string>();
        var site = themes["site"]!.GetValue<string>();

        admin.Should().Be("TheAdmin");
        site.Should().NotBeNullOrWhiteSpace("without a site theme nobody can sign in to the tenant");

        EnabledFeatures.Should().Contain(admin, "a selected theme whose feature is off has no shapes");
        EnabledFeatures.Should().Contain(site, "a selected theme whose feature is off has no shapes");
    }

    [Fact]
    public void NoRoleIsGivenAPermissionThatIsNotAssignable()
    {
        // Also found by applying the recipe: the Roles step ignores a permission name it does not
        // recognise, without warning. ManageGroupSettings is the trap that caught us — in 3.0.1 it
        // is a template for per-group permissions, not a permission a role can hold, so granting
        // it looked fine in the recipe and did nothing in the tenant.
        string[] notAssignable = ["ManageGroupSettings"];

        foreach (var role in RoleStep["Roles"]!.AsArray())
        {
            var permissions = role!["Permissions"]!.AsArray().Select(p => p!.GetValue<string>());

            permissions.Should().NotIntersectWith(
                notAssignable,
                "the Roles step would accept it and grant nothing");
        }
    }

    [Fact]
    public void UsersCannotRegisterThemselves()
    {
        // The base recipe selects a site theme, which means every tenant has a reachable public
        // login page. Registration must not come with it: a WorkMate user is an employee the HR
        // administrator creates, and anyone who could sign themselves up would land inside the
        // tenant's boundary. It stays off until someone turns it on deliberately.
        EnabledFeatures.Should().NotContain(
            "OrchardCore.Users.Registration",
            "a public login page must not come with a public sign-up page");
    }

    [Fact]
    public void EveryStubStepNamesTheModuleThatWillOwnIt()
    {
        var stubs = Steps.Where(step => step!["$stub"] is not null).ToList();

        stubs.Should().NotBeEmpty("the later steps of section 3 are not built yet");

        foreach (var stub in stubs)
        {
            var module = stub!["$stub"]!["module"]!.GetValue<string>();

            module.Should().StartWith("WorkMate.");
            Directory.Exists(Path.Combine(LocalisationResourceTests.RepositoryRoot, "src", module))
                .Should().BeTrue("a stub must name a module that exists, not a guess");
        }
    }

    [Fact]
    public void EveryStepIsEitherRealOrExplicitlyMarkedAsAStub()
    {
        // A step that does nothing and does not say so is the one that gets forgotten.
        // dimension-types and structures became real in prompt 3: the base recipe now seeds the
        // CostCentre type and an empty primary Organisation structure.
        string[] implemented = ["Feature", "themes", "Roles", "Settings", "dimension-types", "structures"];

        foreach (var step in Steps)
        {
            var name = step!["name"]!.GetValue<string>();

            if (implemented.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            step!["$stub"].Should().NotBeNull("step '{0}' does nothing yet, so it must say so", name);
            step!["$comment"].Should().NotBeNull("step '{0}' must explain what it will do", name);
        }
    }

    private static JsonArray Steps => Recipe["steps"]!.AsArray();

    private static JsonNode RoleStep => StepNamed("Roles");

    private static JsonNode SettingsStep => StepNamed("Settings");

    private static List<string> EnabledFeatures =>
        [.. StepNamed("Feature")["enable"]!.AsArray().Select(feature => feature!.GetValue<string>())];

    private static JsonNode StepNamed(string name) =>
        Steps.Single(step => step!["name"]!.GetValue<string>() == name)!;

    private static readonly JsonNode Recipe = JsonNode.Parse(
        File.ReadAllText(Path.Combine(LocalisationResourceTests.RepositoryRoot, "recipes", "base.recipe.json")),
        documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
}
