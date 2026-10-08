namespace WorkMate.Records.Services;

/// <summary>
/// The single policy on what may be added to the employee record, and where.
/// </summary>
/// <remarks>
/// Specification section 5 requires the fixed core to be beyond an administrator's reach, and the
/// brief asks for that to be impossible rather than discouraged. Making it impossible takes several
/// mechanisms, because there are several routes in; this is the one rule all of them consult, so
/// they cannot come to different conclusions.
///
/// <b>What is closed, as of this slice:</b>
/// <list type="bullet">
/// <item><c>EmployeePart</c> is declared <em>not attachable</em> in <c>Migrations</c>, so Orchard's
/// own content-type editor does not offer it in "Add parts" and cannot move it onto another type or
/// take it off <c>Employee</c>.</item>
/// <item>Every section field this module defines is routed through <see cref="EnsureMayDefine"/> as
/// the migration declares it, so a section that collided with a core name would fail at tenant
/// setup rather than ship.</item>
/// <item><c>EmployeeContentDefinitionTenantTests</c> reads the live definition on a real tenant and
/// asserts that <c>EmployeePart</c> carries exactly the reserved names and that no section field
/// takes one.</item>
/// </list>
///
/// <b>What is not closed yet, and where it will be.</b> Orchard's <c>IContentDefinitionEventHandler</c>
/// was considered as a runtime backstop and rejected: verified against the pinned version, every one
/// of its seventeen members is <c>void</c> and past-tense — <c>ContentFieldAttached</c>,
/// <c>ContentPartUpdated</c> — so it is a notification that the change has happened, not a veto on
/// it happening. A guard built on it could report a violation and could not prevent one. The route
/// that genuinely needs closing is the form designer publishing a definition that names the
/// <c>Employee</c> type, and that is closed in prompt 4 session B, in the compiler, which is a
/// write path of ours and can refuse. This class is what it will call.
/// </remarks>
public static class EmployeeContentDefinitionGuard
{
    /// <summary>
    /// Whether a field of this name may be defined on this part, and why not if not.
    /// </summary>
    /// <param name="partName">The part the field would go on.</param>
    /// <param name="fieldName">The field's name, which is what Liquid and GraphQL address it by.</param>
    /// <returns>Null when it is permitted; the violation otherwise.</returns>
    public static EmployeeDefinitionViolation? Check(string partName, string fieldName)
    {
        if (string.Equals(partName, EmployeeFieldNames.PartName, StringComparison.Ordinal))
        {
            return new EmployeeDefinitionViolation(
                partName,
                fieldName,
                EmployeeDefinitionViolationKind.CoreIsClosed);
        }

        if (EmployeeFieldNames.IsReserved(fieldName))
        {
            return new EmployeeDefinitionViolation(
                partName,
                fieldName,
                EmployeeDefinitionViolationKind.ReservedName);
        }

        if (EmployeeFieldNames.IsForbiddenOnSection(fieldName))
        {
            return new EmployeeDefinitionViolation(
                partName,
                fieldName,
                EmployeeDefinitionViolationKind.PlacementBelongsInTheDimensionEngine);
        }

        return null;
    }

    /// <summary>
    /// Throws if a field may not be defined. For this module's own migration, which is building the
    /// definitions and has nobody to report a validation message to.
    /// </summary>
    /// <remarks>
    /// Deliberately an exception here and a returned violation everywhere else. A section field name
    /// that breaks this rule is a mistake in <em>our</em> source, caught at tenant setup, where
    /// failing loudly is exactly right; an administrator's form definition breaking it is a mistake
    /// in their input, which has to come back as a message on the field they typed it into.
    /// </remarks>
    public static void EnsureMayDefine(string partName, string fieldName)
    {
        var violation = Check(partName, fieldName);

        if (violation is not null)
        {
            throw new InvalidOperationException(violation.Describe());
        }
    }
}

/// <summary>Why a field may not be defined where it was going.</summary>
public enum EmployeeDefinitionViolationKind
{
    /// <summary>The fixed core takes no additions at all.</summary>
    CoreIsClosed,

    /// <summary>
    /// The name belongs to the fixed core. A section field called <c>JoinDate</c> would read as the
    /// employee's join date on every screen and in every template while holding something else.
    /// </summary>
    ReservedName,

    /// <summary>
    /// The name is an organisational placement, which the employee record does not hold by design.
    /// Architecture section 2: every placement is a dated assignment row, because an employee sits
    /// on several axes at once and a field would collapse them into one — and would answer "where
    /// did they sit last March" with today's answer.
    /// </summary>
    PlacementBelongsInTheDimensionEngine,
}

/// <summary>One refused addition to the employee record.</summary>
public sealed record EmployeeDefinitionViolation(
    string PartName,
    string FieldName,
    EmployeeDefinitionViolationKind Kind)
{
    /// <summary>
    /// A plain sentence, for an exception message and a test failure.
    /// </summary>
    /// <remarks>
    /// Not localised, and deliberately: the two readers are a developer whose migration failed at
    /// tenant setup and a test run. A violation shown to an administrator is localised by the
    /// caller that shows it, which in session B is the form designer, where the message can also
    /// name the field they typed.
    /// </remarks>
    public string Describe() => Kind switch
    {
        EmployeeDefinitionViolationKind.CoreIsClosed =>
            $"'{FieldName}' cannot be added to {PartName}: the employee record's fixed core is not "
            + "extensible, because payroll, leave and attendance depend on its fields by name. Add a "
            + "section instead.",

        EmployeeDefinitionViolationKind.ReservedName =>
            $"A field on '{PartName}' cannot be called '{FieldName}': the name belongs to the "
            + "employee record's fixed core and would read as that field everywhere.",

        _ =>
            $"A field on '{PartName}' cannot be called '{FieldName}': an employee's placement is a "
            + "dated assignment row in the dimension engine, not a field on the record. See "
            + "IEmployeeAssignmentService.",
    };
}
