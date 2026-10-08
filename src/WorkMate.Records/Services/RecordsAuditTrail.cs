using Microsoft.Extensions.Options;
using OrchardCore.AuditTrail.Services.Models;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// The audit trail category and event names this module records under.
/// </summary>
/// <remarks>
/// Constants rather than literals at the call sites, for the reason <c>DimensionAuditTrail</c>
/// gives: the same name appears in the options that declare the event, in the manager call that
/// records one, and in the filter an administrator uses to find them. A typo in any one of the
/// three produces an event that is recorded and never surfaced — worse than no audit trail,
/// because it looks like one.
/// </remarks>
public static class RecordsAuditTrail
{
    /// <summary>The category every event in this module is filed under.</summary>
    public const string Category = "Employee";

    /// <summary>An employee record was created, or an editable core field was changed.</summary>
    public const string EmployeeChanged = "EmployeeChanged";

    /// <summary>
    /// An employee moved between lifecycle states.
    /// </summary>
    /// <remarks>
    /// Its own event rather than part of <see cref="EmployeeChanged"/>, because a lifecycle change
    /// is the one kind of edit to an employee that changes what other modules do: an exit closes
    /// placements and headships and starts a final settlement. An auditor asking "when did this
    /// person leave, and who recorded it" should not have to read every phone-number change to
    /// find out.
    /// </remarks>
    public const string EmploymentStatusChanged = "EmploymentStatusChanged";

    /// <summary>
    /// The join date was corrected.
    /// </summary>
    /// <remarks>
    /// Separate again, and for the reason the operation is separate on the service: it rewrites the
    /// past deliberately. Gratuity, leave accrual and probation all move with it, so an auditor
    /// reconciling a changed gratuity figure needs to be able to find this in one filter rather
    /// than by reading the diff of every edit.
    /// </remarks>
    public const string JoinDateCorrected = "JoinDateCorrected";
}

/// <summary>
/// Declares this module's audit trail category and events, so an administrator can find and filter
/// them alongside Orchard's own.
/// </summary>
/// <remarks>
/// All three are mandatory rather than merely enabled by default. Specification section 9's
/// definition of done requires the audit trail to cover material changes, and an event an
/// administrator can quietly switch off is not an audit trail — least of all the one that records
/// somebody leaving.
/// </remarks>
public sealed class RecordsAuditTrailOptionsConfiguration : IConfigureOptions<AuditTrailOptions>
{
    public void Configure(AuditTrailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.For<RecordsAuditTrailOptionsConfiguration>(
                RecordsAuditTrail.Category,
                S => S["Employees"])
            .WithEvent(
                RecordsAuditTrail.EmployeeChanged,
                S => S["Employee changed"],
                S => S["An employee record was created or its core fields were changed."],
                enableByDefault: true,
                isMandatory: true)
            .WithEvent(
                RecordsAuditTrail.EmploymentStatusChanged,
                S => S["Employment status changed"],
                S => S["An employee was activated, put on leave, suspended, exited or reinstated."],
                enableByDefault: true,
                isMandatory: true)
            .WithEvent(
                RecordsAuditTrail.JoinDateCorrected,
                S => S["Join date corrected"],
                S => S["An employee's join date was corrected, which moves every figure computed from tenure."],
                enableByDefault: true,
                isMandatory: true);
    }
}

/// <summary>What an employee's record said, for the before-and-after on an audit entry.</summary>
/// <remarks>
/// A snapshot of the fields a person would want to compare, not of the whole record: an audit entry
/// is read by somebody asking what changed, and a payload carrying every field makes them find it.
/// </remarks>
public sealed class EmployeeState
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public DateOnly JoinDate { get; set; }

    public EmploymentStatus Status { get; set; }

    public DateOnly StatusEffectiveFrom { get; set; }

    public string LineManagerEmployeeId { get; set; } = string.Empty;

    public static EmployeeState Of(EmployeePart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        return new EmployeeState
        {
            Code = part.EmployeeCode,
            NameEn = part.NameEn,
            NameAr = part.NameAr,
            JoinDate = part.JoinDate,
            Status = part.Status,
            StatusEffectiveFrom = part.StatusEffectiveFrom,
            LineManagerEmployeeId = part.LineManagerEmployeeId,
        };
    }
}

/// <summary>The payload of an <see cref="RecordsAuditTrail.EmployeeChanged"/> entry.</summary>
public sealed class EmployeeAuditEvent
{
    public string EmployeeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    /// <summary>What it said before, or null on creation.</summary>
    public EmployeeState? Before { get; set; }

    public EmployeeState? After { get; set; }

    /// <summary>
    /// The operation, named, so a reader does not have to infer it from the diff.
    /// </summary>
    /// <remarks>
    /// "Exited" and "Suspended" look alike in a before-and-after of two enum values; what they do
    /// to the employee's placements does not. Naming the operation is what lets an auditor filter
    /// to the one they are investigating.
    /// </remarks>
    public string Operation { get; set; } = string.Empty;

    /// <summary>The date the operation takes effect, which is never the date it was recorded.</summary>
    public DateOnly? EffectiveOn { get; set; }

    /// <summary>How many placements an exit closed. Zero for everything else.</summary>
    public int AssignmentsClosed { get; set; }

    /// <summary>How many units an exit left without a head. Zero for everything else.</summary>
    public int HeadshipsClosed { get; set; }
}
