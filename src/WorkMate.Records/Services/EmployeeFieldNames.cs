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

            // D31 addendum 2. Reserved for the same reason as the id beside it: a section field
            // called LineManagerKind would read as the core's answer on every screen and in every
            // Liquid template while holding something else.
            nameof(EmployeePart.LineManagerKind),
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

            // The vocabulary slice F6 DERIVES. An employee's position, their grade and who they
            // report to are read from the dated assignment and from where that position sits — D30
            // — and are on the record nowhere. A tenant that added a "Position" field through the
            // form designer would have built an undated shadow of all three, which would disagree
            // with the organisation block on the same page the first time somebody transferred.
            //
            // "JobTitle" AND "GradeCode" ARE NOT HERE, and that is still deliberate — see
            // RetiredOnSections below, which is the set F6c added for them. They cannot go in this
            // set because the shipped JobDetails section defines both, and this set is consulted
            // as that definition is written: putting them here would make CreateAsync throw on
            // every brand-new tenant.
            "Position",
            "PositionId",
            "PositionCode",
            "Grade",
            "GradeId",
            "ReportsTo",

            // The line manager, by every name a designer would reach for. Slice B3: the record
            // holds exactly one answer to "who manages this person" and D31 addendum 2 made it a
            // LIVE one — stored only where somebody chose, derived everywhere else. A designer
            // field called LineManager would be a second answer, undated and never derived, and
            // the two would part company the first time the post above changed hands. The id and
            // the kind are already reserved as core names; these are the ones a person types.
            "LineManager",
            "LineManagerId",
            "LineManagerCode",
            "Manager",
            "ManagerId",
        ]);

        return names;
    }

    /// <summary>
    /// Names no NEW section or form field may take, because D22 retired them. Slice F6c.
    /// </summary>
    /// <remarks>
    /// <b>Why these are a separate set from <see cref="ForbiddenOnSections"/>.</b> That set is
    /// consulted while this module's own migration writes the shipped sections, and the shipped
    /// <c>EmployeeJobDetail</c> section defines both of these — so a name in both sets would make
    /// tenant setup throw. These are forbidden <em>everywhere else</em>: the one part that already
    /// holds them keeps them, and nothing new may take the name.
    ///
    /// <b>Why forbid a name nobody is using.</b> Because the values are still there and still
    /// shown. A form the designer publishes with a field called <c>JobTitle</c> on another section
    /// would read as the employee's job title in Liquid, in GraphQL and to anybody scanning the
    /// record, while holding something else — and the one it collides with is the historical value
    /// D22 promised to keep legible.
    /// </remarks>
    public static readonly IReadOnlySet<string> RetiredOnSections =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "JobTitle", "GradeCode" };

    /// <summary>
    /// Whether a NEW field of this name is refused because D22 retired it on the section that
    /// already has it.
    /// </summary>
    /// <param name="partName">The part the field would go on.</param>
    /// <param name="fieldName">The name it would take.</param>
    public static bool IsRetiredOnSection(string? partName, string? fieldName) =>
        fieldName is not null &&
        RetiredOnSections.Contains(fieldName) &&

        // The one part that already holds them. Its own declaration is the history D22 keeps, so
        // it is exempt; everything else is not.
        !string.Equals(partName, EmployeeSections.JobDetailType, StringComparison.Ordinal);

    /// <summary>Whether the fixed core owns this name.</summary>
    public static bool IsReserved(string? name) => name is not null && Reserved.Contains(name);

    /// <summary>Whether a section or form field may be called this.</summary>
    public static bool IsForbiddenOnSection(string? name) =>
        name is not null && ForbiddenOnSections.Contains(name);
}
