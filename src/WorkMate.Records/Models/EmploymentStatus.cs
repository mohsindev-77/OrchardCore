namespace WorkMate.Records.Models;

/// <summary>
/// The five states specification section 5 names, and the only values
/// <c>EmployeePart.Status</c> ever holds.
/// </summary>
/// <remarks>
/// A persisted enum member is a contract — the same rule <c>DimensionRule</c> carries — so a value
/// here is never renamed or renumbered once it has shipped, and a state that stops being used is
/// retired by nothing transitioning into it rather than by being removed.
///
/// Stored by name rather than by number, so a reordering cannot silently turn every suspended
/// employee into an active one.
/// </remarks>
public enum EmploymentStatus
{
    /// <summary>
    /// Hired and on record, not yet started. Payroll, leave and attendance all ignore them.
    /// </summary>
    /// <remarks>
    /// The state every employee is created in, including one created by an import with a join date
    /// in the past. Activating them is a dated decision somebody makes, not something inferred from
    /// a date having passed, because "we hired them and they never turned up" is a real outcome and
    /// a status that activates itself cannot represent it.
    /// </remarks>
    Prospective,

    /// <summary>Working. The only state in which an employee accrues and is paid normally.</summary>
    Active,

    /// <summary>
    /// Away on extended leave — maternity, study, unpaid — still employed and still placed.
    /// </summary>
    /// <remarks>
    /// Distinct from a leave <em>request</em>, which is a transaction in <c>WorkMate.Hcm.Leave</c>
    /// against an employee who stays <see cref="Active"/>. This is the status for an absence long
    /// enough that payroll and accrual treat the person differently for its whole duration.
    /// </remarks>
    OnLeave,

    /// <summary>Suspended, usually pending an investigation. Employed, placed, and not working.</summary>
    Suspended,

    /// <summary>
    /// Left. Their placements and headships are closed on the exit date and nothing reopens them.
    /// </summary>
    /// <remarks>
    /// Not a deletion and never one. Payroll history, past approvals and last year's headcount all
    /// resolve through an exited employee, which is why the code stays unique against them too.
    /// </remarks>
    Exited,
}
