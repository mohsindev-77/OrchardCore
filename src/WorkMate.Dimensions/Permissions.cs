using OrchardCore.Security.Permissions;
using WorkMate.Platform;

namespace WorkMate.Dimensions;

/// <summary>
/// The seven permissions specification section 4 names for the dimension engine.
/// </summary>
/// <remarks>
/// Declared here and checked in the services, never only in a controller, so that the API, the
/// recipe import paths and the background jobs are covered by the same check as the admin screen.
///
/// Descriptions are plain strings rather than localiser calls because that is how Orchard Core
/// 3.0.1 localises them: through OrchardCore.Roles' permission localisation data provider and the
/// OrchardCore.DataLocalization feature, which the base recipe enables, rather than through the
/// PO resources a module ships. WorkMate.Platform's Permissions carries the same note.
///
/// Why these are separate permissions rather than one "manage the structure": moving and merging
/// are the two operations that silently change what every historical report resolves to, and a
/// customer routinely wants an HR administrator who can create a department but cannot
/// reorganise the company. Splitting them is what makes that expressible.
/// </remarks>
public sealed class Permissions : IPermissionProvider
{
    /// <summary>Create, change and retire dimension types and their attribute schemas. Creates content types.</summary>
    public static readonly Permission ManageDimensionTypes = new(
        nameof(ManageDimensionTypes),
        "Manage dimension types and their attribute schemas",
        isSecurityCritical: true);

    /// <summary>Define structures, their ordered levels and their rules.</summary>
    public static readonly Permission ManageStructures = new(
        nameof(ManageStructures),
        "Manage structures and their levels",
        isSecurityCritical: true);

    /// <summary>Create, change and retire dimension records.</summary>
    public static readonly Permission ManageDimensionRecords = new(
        nameof(ManageDimensionRecords),
        "Manage dimension records");

    /// <summary>Reparent a record, which changes what every descendant resolves under.</summary>
    public static readonly Permission MoveDimensionRecords = new(
        nameof(MoveDimensionRecords),
        "Move dimension records to a different parent");

    /// <summary>Fold one record into another, reassigning its children and employees.</summary>
    public static readonly Permission MergeDimensionRecords = new(
        nameof(MergeDimensionRecords),
        "Merge one dimension record into another");

    /// <summary>Resolve the structure as at a past date rather than only as it is today.</summary>
    public static readonly Permission ViewDimensionHistory = new(
        nameof(ViewDimensionHistory),
        "View the structure as it was on a past date");

    /// <summary>Place an employee on a structure, end a placement, or change an allocation.</summary>
    public static readonly Permission AssignEmployees = new(
        nameof(AssignEmployees),
        "Assign employees to dimension records");

    private static readonly IReadOnlyList<Permission> AllPermissions =
    [
        ManageDimensionTypes,
        ManageStructures,
        ManageDimensionRecords,
        MoveDimensionRecords,
        MergeDimensionRecords,
        ViewDimensionHistory,
        AssignEmployees,
    ];

    public Task<IEnumerable<Permission>> GetPermissionsAsync() =>
        Task.FromResult<IEnumerable<Permission>>(AllPermissions);

    /// <summary>
    /// Which of this module's permissions each platform role starts with.
    ///
    /// The two administrator roles get everything. The HR administrator gets the day-to-day set —
    /// create a department, place an employee, look at last March — but not the two configuration
    /// permissions, because creating a dimension type creates a content type and that is a
    /// delivery-team act, not a business one. Nor does the HR administrator get move or merge:
    /// those are the operations that reshape the organisation, and specification section 4 splits
    /// them out precisely so a customer can decide who holds them. Giving them away by default
    /// would make that split decorative.
    ///
    /// The auditor reads history and nothing else. The manager and employee roles get nothing
    /// here; what they may see of the structure is data visibility by node, which is section 9's
    /// concern and a different mechanism entirely.
    /// </summary>
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
            Permissions = [ManageDimensionRecords, AssignEmployees, ViewDimensionHistory],
        },
        new PermissionStereotype
        {
            Name = PlatformRoles.Auditor,
            Permissions = [ViewDimensionHistory],
        },
    ];
}
