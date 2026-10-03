using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Localization;
using OrchardCore.Security.Permissions;
using OrchardCore.Security.Services;
using WorkMatePermissions = WorkMate.Platform.Permissions;
using WorkMate.Platform;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// What a tenant actually looks like after the base recipe has been applied to it.
///
/// The unit suite checks the recipe's shape; it cannot check what Orchard does with it. That
/// distinction is not academic: the Roles step drops a permission name it does not recognise
/// without a word, and two names in this recipe were being dropped that way before these ran.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class BaseRecipeTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public BaseRecipeTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    [Fact]
    public async Task TheSixPlatformRolesExist()
    {
        var roles = await RoleNamesAsync();

        roles.Should().Contain(PlatformRoles.All);
    }

    [Fact]
    public async Task EveryPermissionTheRecipeGrantsIsActuallyHeld()
    {
        // The assertion that catches a silently dropped permission name. Each grant is checked
        // against the role as the tenant holds it, not against the file that asked for it.
        foreach (var (role, expected) in PermissionsTheRecipeGrants())
        {
            var held = await PermissionsHeldByAsync(role);

            held.Should().Contain(
                expected,
                "the base recipe grants these to {0}, and the Roles step drops a name it does not recognise without saying so",
                role);
        }
    }

    [Fact]
    public async Task TheAdministratorRolesHoldWorkMatesOwnPermissionFromTheStereotype()
    {
        // ManageWorkMateSettings is never named in the recipe. It reaches these roles only
        // because WorkMate.Platform declares a stereotype and Orchard's RoleUpdater applies it,
        // which is the mechanism every later module depends on.
        foreach (var role in new[] { PlatformRoles.PlatformAdministrator, PlatformRoles.TenantAdministrator })
        {
            (await PermissionsHeldByAsync(role)).Should().Contain(
                WorkMatePermissions.ManageWorkMateSettings.Name,
                "{0} should get it from the stereotype, not from the recipe",
                role);
        }
    }

    [Fact]
    public async Task OnlyThePlatformAdministratorMayEditRoleDefinitions()
    {
        // Until product decision 2 settles who configures a customer, editing role definitions
        // stays with the delivery team.
        var manageRoles = OrchardCore.Roles.RolesPermissions.ManageRoles.Name;

        (await PermissionsHeldByAsync(PlatformRoles.PlatformAdministrator)).Should().Contain(manageRoles);

        foreach (var role in PlatformRoles.All.Except([PlatformRoles.PlatformAdministrator]))
        {
            (await PermissionsHeldByAsync(role)).Should().NotContain(manageRoles, "{0} must not edit roles", role);
        }
    }

    [Fact]
    public async Task OnlyThePlatformAdministratorMayImportARecipe()
    {
        // The effective-permissions half of the same assertion in PermissionNameTests. A recipe
        // step runs on the platform's own authority through ISystemOperation, which outranks the
        // signed-in user, so Import is in practice a grant of everything a recipe step can do.
        // It stays with the delivery team, and this checks what the tenant actually holds rather
        // than what the recipe file says.
        var import = OrchardCore.Deployment.DeploymentPermissions.Import.Name;

        (await PermissionsHeldByAsync(PlatformRoles.PlatformAdministrator)).Should().Contain(import);

        foreach (var role in PlatformRoles.All.Except([PlatformRoles.PlatformAdministrator]))
        {
            (await PermissionsHeldByAsync(role)).Should().NotContain(
                import,
                "{0} must not be able to import a recipe: the steps would run as the platform", role);
        }
    }

    [Fact]
    public async Task TheTenantAdministratorCanStillPutPeopleIntoRoles() =>
        (await PermissionsHeldByAsync(PlatformRoles.TenantAdministrator))
            .Should().Contain(OrchardCore.Users.UsersPermissions.AssignRoleToUsers.Name);

    [Fact]
    public async Task TheEmployeeAndManagerRolesHoldNothingFromThePlatform()
    {
        // Their permissions arrive with the modules that give them work to do. If something
        // appears here, a module has granted more than it meant to.
        foreach (var role in new[] { PlatformRoles.Employee, PlatformRoles.Manager })
        {
            (await PermissionsHeldByAsync(role)).Should().BeEmpty(
                "{0} gets its permissions from the HCM modules, not from the platform", role);
        }
    }

    [Fact]
    public async Task TheAuditorReadsAndChangesNothing()
    {
        var held = await PermissionsHeldByAsync(PlatformRoles.Auditor);

        held.Should().Contain(OrchardCore.AuditTrail.AuditTrailPermissions.ViewAuditTrail.Name);
        held.Should().NotContain(WorkMatePermissions.ManageWorkMateSettings.Name);
        held.Should().NotContain(OrchardCore.Settings.SettingsPermissions.ManageSettings.Name);
    }

    [Fact]
    public async Task TheLoginPageIsReachable()
    {
        // The base recipe selects a site theme. Without one the front end falls back to the admin
        // theme, which has no front-end Layout shape, and this throws — which is how a tenant
        // nobody could sign in to shipped once already.
        var response = await _tenant.Anonymous.GetAsync("/Login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("LoginForm.UserName");
    }

    [Fact]
    public async Task UsersCannotRegisterThemselves()
    {
        // A reachable public login page must not come with a public sign-up page: a WorkMate user
        // is an employee the HR administrator creates.
        var response = await _tenant.Anonymous.GetAsync("/Register");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BothPlatformCulturesAreConfigured()
    {
        string[] cultures = [];

        await _tenant.InTenantAsync(async services =>
        {
            cultures = await services.GetRequiredService<ILocalizationService>().GetSupportedCulturesAsync();
        });

        cultures.Should().BeEquivalentTo(["en", "ar"]);
    }

    [Fact]
    public async Task EnglishIsTheDefaultCulture()
    {
        var culture = string.Empty;

        await _tenant.InTenantAsync(async services =>
        {
            culture = await services.GetRequiredService<ILocalizationService>().GetDefaultCultureAsync();
        });

        culture.Should().Be("en");
    }

    private async Task<IReadOnlyCollection<string>> RoleNamesAsync()
    {
        var names = Array.Empty<string>();

        await _tenant.InTenantAsync(async services =>
        {
            var roles = await services.GetRequiredService<IRoleService>().GetRoleNamesAsync();
            names = [.. roles];
        });

        return names;
    }

    private async Task<IReadOnlyCollection<string>> PermissionsHeldByAsync(string role)
    {
        var permissions = Array.Empty<string>();

        await _tenant.InTenantAsync(async services =>
        {
            var claims = await services.GetRequiredService<IRoleService>()
                .GetRoleClaimsAsync(role, CancellationToken.None);

            permissions =
            [
                .. claims
                    .Where(claim => string.Equals(claim.Type, Permission.ClaimType, StringComparison.Ordinal))
                    .Select(claim => claim.Value),
            ];
        });

        return permissions;
    }

    /// <summary>
    /// Read from the recipe rather than restated here, so that this asserts the tenant matches
    /// what the recipe asked for rather than what a test author remembered.
    /// </summary>
    private static IEnumerable<(string Role, string[] Permissions)> PermissionsTheRecipeGrants()
    {
        var roles = BaseRecipe["steps"]!.AsArray()
            .Single(step => step!["name"]!.GetValue<string>() == "Roles")!["Roles"]!.AsArray();

        foreach (var role in roles)
        {
            var permissions = role!["Permissions"]!.AsArray()
                .Select(permission => permission!.GetValue<string>())
                .ToArray();

            if (permissions.Length > 0)
            {
                yield return (role["Name"]!.GetValue<string>(), permissions);
            }
        }
    }

    private static readonly JsonNode BaseRecipe = JsonNode.Parse(
        File.ReadAllText(Path.Combine(RepositoryRoot, "recipes", "base.recipe.json")))!;

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find the repository root above the test output directory.");
        }
    }
}
