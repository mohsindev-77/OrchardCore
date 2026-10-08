using OrchardCore.Security.Permissions;
using WorkMate.Platform;

namespace WorkMate.Records;

/// <summary>
/// What a caller may do to an employee record.
/// </summary>
/// <remarks>
/// Declared here and checked in the services, never only in a controller, so the admin screens, the
/// recipe steps and any future API are covered by the same check.
///
/// Descriptions are plain strings rather than localiser calls because that is how Orchard Core 3.0.1
/// localises them: through <c>OrchardCore.Roles</c>' permission localisation data provider and the
/// <c>OrchardCore.DataLocalization</c> feature, which the base recipe enables. <c>WorkMate.Platform</c>
/// and <c>WorkMate.Dimensions</c> both carry the same note.
///
/// <b>Four, and placement is deliberately not one of them.</b> Placing an employee on a structure is
/// a dimension-engine write and already has <c>WorkMate.Dimensions.Permissions.AssignEmployees</c>;
/// a second permission here would split one capability across two modules and leave a customer
/// granting both to achieve one thing. Setting a unit head is the same write and takes the same
/// permission.
///
/// <b>Why reading sensitive data is separate from reading an employee.</b> A line manager needs to
/// see who works for them; they have no business seeing their team's bank details, passport numbers
/// or dates of birth. Those are the fields a data-protection complaint is actually about, so they
/// get their own permission rather than arriving free with the list.
/// </remarks>
public sealed class Permissions : IPermissionProvider
{
    /// <summary>Create employee records and change their editable core fields.</summary>
    public static readonly Permission ManageEmployees = new(
        nameof(ManageEmployees),
        "Create and change employee records");

    /// <summary>See the employee list and an employee's record.</summary>
    public static readonly Permission ViewEmployees = new(
        nameof(ViewEmployees),
        "View employee records");

    /// <summary>
    /// Move an employee through the lifecycle, and correct a join date.
    /// </summary>
    /// <remarks>
    /// Security-critical, and the only permission in this module that is. An exit closes every
    /// placement and every headship the person holds and starts a final settlement; a corrected
    /// join date moves gratuity, accrual and probation. Neither is the same capability as editing a
    /// record, and a customer routinely wants an HR officer who can keep records current without
    /// being able to terminate anybody.
    /// </remarks>
    public static readonly Permission ChangeEmploymentStatus = new(
        nameof(ChangeEmploymentStatus),
        "Change an employee's employment status and correct their join date",
        isSecurityCritical: true);

    /// <summary>See date of birth, bank details, documents and dependants.</summary>
    public static readonly Permission ViewEmployeeSensitiveData = new(
        nameof(ViewEmployeeSensitiveData),
        "View sensitive employee data such as bank details and identity documents",
        isSecurityCritical: true);

    private static readonly IReadOnlyList<Permission> AllPermissions =
    [
        ManageEmployees,
        ViewEmployees,
        ChangeEmploymentStatus,
        ViewEmployeeSensitiveData,
    ];

    public Task<IEnumerable<Permission>> GetPermissionsAsync() =>
        Task.FromResult<IEnumerable<Permission>>(AllPermissions);

    /// <summary>
    /// Which of this module's permissions each platform role starts with.
    /// </summary>
    /// <remarks>
    /// The two administrator roles get everything, and so does the HR administrator: keeping
    /// employee records, moving people through the lifecycle and seeing their documents is the
    /// whole of that role's job, and withholding any of it would leave it unable to do the thing it
    /// exists for.
    ///
    /// The auditor reads records and nothing else — not sensitive data, which is the point of the
    /// split. The manager and employee roles get nothing here: what a manager may see of their own
    /// team is data visibility by node, which is specification section 9's concern and a different
    /// mechanism entirely. Granting <see cref="ViewEmployees"/> to managers now would give every
    /// one of them the whole tenant's staff list, which is exactly what that mechanism exists to
    /// prevent.
    /// </remarks>
    public IEnumerable<PermissionStereotype> GetDefaultStereotypes() =>
    [
        new PermissionStereotype
        {
            Name = PlatformRoles.PlatformAdministrator,
            Permissions = AllPermissions,
        },
        new PermissionStereotype
        {
            Name = PlatformRoles.TenantAdministrator,
            Permissions = AllPermissions,
        },
        new PermissionStereotype
        {
            Name = PlatformRoles.HrAdministrator,
            Permissions = AllPermissions,
        },
        new PermissionStereotype
        {
            Name = PlatformRoles.Auditor,
            Permissions = [ViewEmployees],
        },
    ];
}
