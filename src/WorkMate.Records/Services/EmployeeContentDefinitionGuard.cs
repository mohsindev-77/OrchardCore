using WorkMate.Records.Models;

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

        // D22, slice F6c. Asked of the PART as well as of the name, because the one section that
        // already holds these keeps them — that is the history D22 promised — and nothing else may
        // take the name beside it.
        if (EmployeeFieldNames.IsRetiredOnSection(partName, fieldName))
        {
            return new EmployeeDefinitionViolation(
                partName,
                fieldName,
                EmployeeDefinitionViolationKind.RetiredByTheDimension);
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

    /// <summary>
    /// Whether a form definition may put fields on this part at all. Slice B3.
    /// </summary>
    /// <remarks>
    /// <b>The question <see cref="Check"/> does not ask.</b> That one is about a field's name on a
    /// part the caller has already decided is fair game; this is about the part. The form designer
    /// is the first caller that chooses its own target, and the two are different refusals: "you
    /// may not call it that" and "you may not write there at all".
    ///
    /// <b>What is designable.</b> The part named for an employee-section item type —
    /// <c>EmployeeJobDetail</c> and the rest, and any a tenant has added — and
    /// <c>EmployeeProfilePart</c>, which exists for exactly this. Nothing else.
    ///
    /// <b>What is refused, and why each.</b> <c>EmployeePart</c> is the fixed core and is closed
    /// by specification section 5; <c>TitlePart</c>, <c>BagPart</c> and anything else of Orchard's
    /// are not ours to weld fields onto, and a product that let an administrator do it would be
    /// changing Orchard rather than extending it — the hard boundary CLAUDE.md states. A part this
    /// module does not recognise is refused rather than permitted, because the safe default for
    /// "I have not heard of this" is no.
    /// </remarks>
    /// <param name="partName">The part a definition would add fields to.</param>
    /// <param name="knownSectionTypes">
    /// The employee-section item types this tenant has, which is a runtime question —
    /// <c>IEmployeeSectionCatalogue</c> answers it from the content definition, so a section a
    /// previous publish created is designable without this class being changed.
    /// </param>
    public static EmployeeDefinitionViolation? CheckTarget(
        string partName,
        IReadOnlySet<string> knownSectionTypes)
    {
        ArgumentNullException.ThrowIfNull(knownSectionTypes);

        if (string.Equals(partName, EmployeeFieldNames.PartName, StringComparison.Ordinal))
        {
            return new EmployeeDefinitionViolation(
                partName, string.Empty, EmployeeDefinitionViolationKind.CoreIsClosed);
        }

        if (string.Equals(partName, nameof(EmployeeProfilePart), StringComparison.Ordinal) ||
            knownSectionTypes.Contains(partName))
        {
            return null;
        }

        return new EmployeeDefinitionViolation(
            partName, string.Empty, EmployeeDefinitionViolationKind.TargetIsNotDesignable);
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

    /// <summary>
    /// The name was retired by D22, and a dimension answers for it now. Slice F6c.
    /// </summary>
    /// <remarks>
    /// <c>JobTitle</c> and <c>GradeCode</c>. The shipped Job details section still holds both,
    /// read-only, because a tenant's history is exactly the values it already typed — so a NEW
    /// field of either name would collide with something that is still on the screen and still
    /// meaningful, while holding something else entirely.
    /// </remarks>
    RetiredByTheDimension,

    /// <summary>
    /// The part is not somewhere a form definition may write. Slice B3.
    /// </summary>
    /// <remarks>
    /// An employee-section item type or <c>EmployeeProfilePart</c>, and nothing else. Orchard's
    /// own parts are not ours to weld fields onto — that would be changing Orchard rather than
    /// extending it — and a part this module has not heard of is refused rather than permitted,
    /// because the safe default for an unknown target is no.
    /// </remarks>
    TargetIsNotDesignable,
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

        EmployeeDefinitionViolationKind.RetiredByTheDimension =>
            $"A field on '{PartName}' cannot be called '{FieldName}': decision D22 retired that "
            + "name, and the Position and Grade dimensions answer for it now. The shipped Job "
            + "details section still holds the historical value under it.",

        EmployeeDefinitionViolationKind.TargetIsNotDesignable =>
            $"A form definition cannot add fields to '{PartName}'. A definition targets an "
            + "employee section's item type or EmployeeProfilePart; Orchard's own parts are not "
            + "ours to extend.",

        _ =>
            $"A field on '{PartName}' cannot be called '{FieldName}': an employee's placement is a "
            + "dated assignment row in the dimension engine, not a field on the record. See "
            + "IEmployeeAssignmentService.",
    };
}
