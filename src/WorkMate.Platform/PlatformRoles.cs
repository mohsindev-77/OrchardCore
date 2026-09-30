namespace WorkMate.Platform;

/// <summary>
/// The six roles every WorkMate tenant has, from specification section 3.
///
/// The names are constants because two separate mechanisms have to agree on them: the base
/// recipe's Roles step creates them, and every module's
/// <see cref="OrchardCore.Security.Permissions.PermissionStereotype"/> grants its own
/// permissions to them by name. Orchard's RoleUpdater applies stereotypes both when a role is
/// created and when a feature is enabled, so a module that ships later reaches these roles
/// without the base recipe being reopened.
///
/// A role name is data, not a display string. It is not localised: renaming a role in Arabic
/// would break the stereotype match and every recipe that references it.
/// </summary>
public static class PlatformRoles
{
    /// <summary>The delivery team. Configures the tenant, including features and deployment.</summary>
    public const string PlatformAdministrator = "Platform Administrator";

    /// <summary>The customer's own administrator. Configures the business, not the platform.</summary>
    public const string TenantAdministrator = "Tenant Administrator";

    /// <summary>Runs human capital administration. Its permissions come from the HCM modules.</summary>
    public const string HrAdministrator = "HR Administrator";

    /// <summary>Sees and approves for their own part of the organisation.</summary>
    public const string Manager = "Manager";

    /// <summary>Sees and requests for themselves.</summary>
    public const string Employee = "Employee";

    /// <summary>Reads everything and changes nothing.</summary>
    public const string Auditor = "Auditor";

    /// <summary>All six, in the order specification section 3 lists them.</summary>
    public static readonly string[] All =
    [
        PlatformAdministrator,
        TenantAdministrator,
        HrAdministrator,
        Manager,
        Employee,
        Auditor,
    ];
}
