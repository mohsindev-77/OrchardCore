using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// The names <see cref="EmployeePart"/> occupies on the <c>Employee</c> content type, and the names
/// nothing else may take.
/// </summary>
/// <remarks>
/// Specification section 5: the fixed core "is not editable by administrators because payroll,
/// leave and attendance depend on these fields by name". That has to be enforced, not requested —
/// the brief is explicit that it should be impossible rather than discouraged — and this is the set
/// the enforcement is written against.
///
/// Three things use it, and each closes a different route in:
/// <list type="bullet">
/// <item>the part is declared <em>not attachable</em> in <c>Migrations</c>, so Orchard's own type
/// editor cannot move it onto another type or off this one;</item>
/// <item><c>EmployeeContentDefinitionGuard</c> refuses any field added to <c>EmployeePart</c> or any
/// section field whose name collides with one of these;</item>
/// <item>prompt 4 session B's form-definition compiler refuses at publish time, which is the only
/// place the last route — a form definition naming the <c>Employee</c> type — can be closed.</item>
/// </list>
///
/// Kept as a declared list and then checked against the type by reflection, rather than only
/// generated from it. The same shape as <c>DimensionTypeService.StandardFieldNames</c>, and for the
/// same reason: generating it would make any field somebody adds to the part automatically
/// "reserved and therefore fine", which is the opposite of a guard. <c>EmployeePartTests</c> fails
/// the build when the two disagree, so adding a core field is a deliberate act in two places.
/// </remarks>
public static class EmployeeFieldNames
{
    /// <summary>The name of the fixed core part on the generated type.</summary>
    public const string PartName = nameof(EmployeePart);

    /// <summary>The content type the employee record is.</summary>
    public const string ContentType = "Employee";

    /// <summary>Every field name the fixed core owns.</summary>
    public static readonly IReadOnlySet<string> Reserved =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(EmployeePart.EmployeeCode),
            nameof(EmployeePart.NameEn),
            nameof(EmployeePart.NameAr),
            nameof(EmployeePart.DateOfBirth),
            nameof(EmployeePart.NationalityCode),
            nameof(EmployeePart.Gender),
            nameof(EmployeePart.JoinDate),
            nameof(EmployeePart.Status),
            nameof(EmployeePart.StatusEffectiveFrom),
            nameof(EmployeePart.LineManagerEmployeeId),
            nameof(EmployeePart.PhotoPath),
        };

    /// <summary>
    /// Names no section or form field may take, whatever part it is going on.
    /// </summary>
    /// <remarks>
    /// Two groups. The reserved core names, because a section field called <c>JoinDate</c> would
    /// read as the employee's join date on every screen and in every Liquid template while holding
    /// something else entirely. And the placement vocabulary, because the employee record holds no
    /// department and no cost centre by design — architecture section 2 — and a tenant that added
    /// one through the form designer would have built exactly the field that decision exists to
    /// rule out, with none of the dating that makes a transfer answerable.
    /// </remarks>
    public static readonly IReadOnlySet<string> ForbiddenOnSections = BuildForbiddenOnSections();

    private static HashSet<string> BuildForbiddenOnSections()
    {
        var names = new HashSet<string>(Reserved, StringComparer.OrdinalIgnoreCase);

        names.UnionWith(
        [
            "Department",
            "DepartmentId",
            "DepartmentCode",
            "CostCentre",
            "CostCenter",
            "CostCentreCode",
            "CostCenterCode",
            "Division",
            "Branch",
            "Section",
            "Unit",
            "StructureId",
            "ParentId",
        ]);

        return names;
    }

    /// <summary>Whether the fixed core owns this name.</summary>
    public static bool IsReserved(string? name) => name is not null && Reserved.Contains(name);

    /// <summary>Whether a section or form field may be called this.</summary>
    public static bool IsForbiddenOnSection(string? name) =>
        name is not null && ForbiddenOnSections.Contains(name);
}
