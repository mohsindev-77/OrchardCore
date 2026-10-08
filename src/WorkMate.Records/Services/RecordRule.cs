namespace WorkMate.Records.Services;

/// <summary>
/// Every rule this module enforces on an employee record, one member per thing that can be refused.
/// </summary>
/// <remarks>
/// The same contract <c>DimensionRule</c> carries, for the same reason: a caller must be able to
/// branch on which rule was broken and a test must be able to assert it, without either matching on
/// message text. A <see cref="RecordError"/> names the rule and carries the localised sentence
/// separately, so no user is ever shown an enum.
///
/// The members are a stable contract. Audit entries name them.
/// </remarks>
public enum RecordRule
{
    /// <summary>A code is missing or is not a well-formed employee code. See <c>EmployeeCodes</c>.</summary>
    CodeFormat,

    /// <summary>
    /// A code is already in use in this tenant — including by an employee who has left.
    /// </summary>
    /// <remarks>
    /// Including leavers, deliberately. Payroll history, past approvals and last year's headcount
    /// all resolve through an exited employee, and reusing their code makes every one of those
    /// ambiguous about which person it meant.
    /// </remarks>
    CodeUniqueness,

    /// <summary>The code of an existing employee was changed. It is the natural key and is immutable.</summary>
    CodeImmutable,

    /// <summary>A name is missing in a language the tenant requires. ADR-0003's addendum.</summary>
    NameRequired,

    /// <summary>A dated write arrived with no effective date. Nothing is ever defaulted on a write.</summary>
    EffectiveDateRequired,

    /// <summary>A date is impossible: a join date before a birth date, an exit before a join.</summary>
    DateOutOfOrder,

    /// <summary>The employee named does not exist in this tenant.</summary>
    UnknownEmployee,

    /// <summary>
    /// The lifecycle does not allow this move — reinstating somebody who never left, exiting
    /// somebody already exited.
    /// </summary>
    TransitionNotPermitted,

    /// <summary>
    /// A transition is dated before the state it would replace began, which would rewrite history
    /// rather than continue it.
    /// </summary>
    TransitionOutOfOrder,

    /// <summary>
    /// An employee was named as their own line manager, directly or through a chain of them.
    /// </summary>
    ReportingLineCycle,

    /// <summary>
    /// A write would change a field the form designer and the admin screens are not allowed to
    /// touch, because payroll, leave and attendance depend on it by name.
    /// </summary>
    ReservedField,
}
