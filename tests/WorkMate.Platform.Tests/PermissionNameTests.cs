using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using OrchardCore.Security.Permissions;
using WorkMate.Platform;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Pins every permission name the base recipe grants to the constant that declares it in the
/// pinned Orchard Core version.
///
/// This exists because the Roles recipe step ignores a permission name it does not recognise and
/// says nothing: the role is created, the grant is dropped, and the tenant looks configured. A
/// typo or an Orchard rename would be invisible until somebody noticed they could not reach a
/// screen. Referencing the declaring assemblies makes a rename a build failure and a typo a test
/// failure.
/// </summary>
public sealed class PermissionNameTests
{
    /// <summary>
    /// The types that declare the permissions the base recipe uses. Found with
    /// <c>tools/api-probe</c>; ManageRoles in particular is not on a public constant that the
    /// type search finds, so it came from <c>--strings OrchardCore.Roles</c>.
    /// </summary>
    private static readonly Type[] PermissionSources =
    [
        typeof(OrchardCore.Admin.AdminPermissions),          // AccessAdminPanel
        typeof(OrchardCore.Roles.RolesPermissions),          // ManageRoles
        typeof(OrchardCore.Settings.Permissions),            // ManageSettings
        typeof(OrchardCore.Users.UsersPermissions),           // ListUsers, ViewUsers, EditUsers, …
        typeof(OrchardCore.Deployment.DeploymentPermissions),// Export, Import, ManageDeploymentPlan
        typeof(OrchardCore.AuditTrail.AuditTrailPermissions),// ViewAuditTrail, ManageAuditTrailSettings
        typeof(OrchardCore.Features.Permissions),            // ManageFeatures
        typeof(Permissions),                                 // WorkMate's own
    ];

    [Fact]
    public void ManageRolesIsSpeltTheWayOrchardSpellsIt() =>
        OrchardCore.Roles.RolesPermissions.ManageRoles.Name.Should().Be("ManageRoles");

    [Fact]
    public void EveryPermissionTheBaseRecipeGrantsActuallyExists()
    {
        var declared = DeclaredPermissionNames();

        foreach (var (role, permission) in GrantsInTheBaseRecipe())
        {
            declared.Should().Contain(
                permission,
                "the base recipe grants '{0}' to {1}, and the Roles step would drop it silently if no module declared it",
                permission,
                role);
        }
    }

    [Fact]
    public void OnlyThePlatformAdministratorMayEditRoleDefinitions()
    {
        // Product decision 2 — who configures a customer — is not settled. Until it is, editing
        // role definitions stays with the delivery team. The tenant administrator can still put
        // people into existing roles, which is the day-to-day need.
        var holders = GrantsInTheBaseRecipe()
            .Where(grant => grant.Permission == OrchardCore.Roles.RolesPermissions.ManageRoles.Name)
            .Select(grant => grant.Role)
            .ToList();

        holders.Should().BeEquivalentTo([PlatformRoles.PlatformAdministrator]);
    }

    [Fact]
    public void TheTenantAdministratorCanStillPutPeopleIntoRoles() =>
        GrantsInTheBaseRecipe().Should().Contain(
            (PlatformRoles.TenantAdministrator, OrchardCore.Users.UsersPermissions.AssignRoleToUsers.Name));

    [Fact]
    public void WorkMatesOwnPermissionIsGrantedByAStereotypeRatherThanByTheRecipe()
    {
        // The recipe never names another module's permissions; each module grants its own when
        // its feature is enabled. If this one ever appears in the recipe, the two mechanisms have
        // started to overlap and one of them is now the wrong place to look.
        GrantsInTheBaseRecipe().Should().NotContain(
            grant => grant.Permission == Permissions.ManageWorkMateSettings.Name);

        new Permissions().GetDefaultStereotypes()
            .Should().OnlyContain(stereotype =>
                stereotype.Name == PlatformRoles.PlatformAdministrator ||
                stereotype.Name == PlatformRoles.TenantAdministrator);
    }

    [Fact]
    public void EveryStereotypeNamesARoleTheRecipeCreates()
    {
        var created = PlatformRoles.All;

        foreach (var stereotype in new Permissions().GetDefaultStereotypes())
        {
            created.Should().Contain(
                stereotype.Name,
                "a stereotype for a role nothing creates grants nothing to nobody");
        }
    }

    private static HashSet<string> DeclaredPermissionNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in PermissionSources)
        {
            foreach (var field in source.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is Permission permission)
                {
                    names.Add(permission.Name);
                }
            }
        }

        return names;
    }

    private static List<(string Role, string Permission)> GrantsInTheBaseRecipe()
    {
        var grants = new List<(string, string)>();

        var roles = BaseRecipe["steps"]!.AsArray()
            .Single(step => step!["name"]!.GetValue<string>() == "Roles")!["Roles"]!.AsArray();

        foreach (var role in roles)
        {
            var name = role!["Name"]!.GetValue<string>();

            foreach (var permission in role["Permissions"]!.AsArray())
            {
                grants.Add((name, permission!.GetValue<string>()));
            }
        }

        return grants;
    }

    private static readonly JsonNode BaseRecipe = JsonNode.Parse(
        File.ReadAllText(Path.Combine(LocalisationResourceTests.RepositoryRoot, "recipes", "base.recipe.json")))!;
}
