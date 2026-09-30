using OrchardCore.Security.Permissions;

namespace WorkMate.Platform;

/// <summary>Permissions are declared here and checked in services, never only in controllers.</summary>
/// <remarks>
/// Permission descriptions are plain strings rather than localiser calls because that is how
/// Orchard Core 3.0.1 localises them: descriptions are surfaced as localisable data through
/// OrchardCore.Roles' PermissionsLocalizationDataProvider and the OrchardCore.DataLocalization
/// feature, not through the PO resources a module ships. The base recipe enables that feature.
/// </remarks>
public sealed class Permissions : IPermissionProvider
{
    /// <summary>
    /// Read and change the platform-wide settings: locale, calendar, direction, fiscal year,
    /// currency, working week and customer code.
    /// </summary>
    public static readonly Permission ManageWorkMateSettings = new(
        nameof(ManageWorkMateSettings),
        "Manage WorkMate platform settings",
        isSecurityCritical: false);

    private static readonly IReadOnlyList<Permission> AllPermissions = [ManageWorkMateSettings];

    public Task<IEnumerable<Permission>> GetPermissionsAsync() =>
        Task.FromResult<IEnumerable<Permission>>(AllPermissions);

    /// <summary>
    /// Which of this module's permissions each platform role starts with.
    ///
    /// Orchard's RoleUpdater applies these when a role is created and again when a feature is
    /// enabled, so the grant happens whichever comes first: the base recipe creating the roles,
    /// or this feature being switched on later. Every module declares its own permissions the
    /// same way, which is why the base recipe never has to list another module's permissions.
    ///
    /// Changing the platform settings changes currency, fiscal year and the working week, which
    /// every calculation downstream depends on. It stays with the two administrator roles. The
    /// HR administrator configures the business inside those constraints; the manager, employee
    /// and auditor roles get nothing from this module, and their permissions arrive with the
    /// modules that give them work to do.
    /// </summary>
    public IEnumerable<PermissionStereotype> GetDefaultStereotypes() =>
    [
        new PermissionStereotype
        {
            Name = PlatformRoles.PlatformAdministrator,
            Permissions = [ManageWorkMateSettings],
        },
        new PermissionStereotype
        {
            Name = PlatformRoles.TenantAdministrator,
            Permissions = [ManageWorkMateSettings],
        },
    ];
}
