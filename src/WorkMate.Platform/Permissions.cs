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

    // The platform roles and the permissions each one starts with are declared alongside the
    // roles themselves. Orchard's RoleUpdater applies stereotypes both when a role is created
    // and when a feature is enabled, so a later module's permissions reach these roles without
    // the base recipe being reopened.
    public IEnumerable<PermissionStereotype> GetDefaultStereotypes() => [];
}
