using OrchardCore.ContentManagement;
using WorkMate.Core;

namespace WorkMate.Records.Models;

/// <summary>
/// The fixed core of the employee record: the fields specification section 5 names, and nothing
/// else.
/// </summary>
/// <remarks>
/// <b>Not editable by an administrator, and that is enforced rather than asked for.</b> Payroll,
/// leave and attendance depend on these fields by name, so a tenant that could rename
/// <c>JoinDate</c> or remove <c>Status</c> through the form designer would break three modules that
/// have no way to notice. The guard rails are: this part is declared in <c>Migrations</c> and is
/// <em>not attachable</em>, so Orchard's own type editor cannot move it anywhere; and
/// <c>EmployeeFieldNames</c> holds the names it occupies, which <c>EmployeeContentDefinitionGuard</c>
/// refuses to let anything else write. Prompt 4's session B adds the last of them, where the form
/// designer's compiler can refuse at publish time.
///
/// <b>What is deliberately not here: any organisational placement.</b> No department, no cost
/// centre, no structure, no parent. Placement is an <c>EmployeeAssignment</c> row in the dimension
/// engine — architecture section 2's one rule that follows from the model — because an employee
/// sits on several axes at once and a single department field would collapse them into one, and
/// because a mid-month transfer has to be a pair of dated rows rather than a destructive edit.
/// <c>EmployeePartTests</c> reflects over this type and fails on any property that looks like one.
///
/// <b>Line manager is not placement either</b>, and it is here precisely because the two are
/// different facts that customers routinely have differ: who you report to and which unit you sit
/// in are not the same question, and a model that answered both from one field would have to pick
/// one of them to be wrong about.
///
/// Public setters, like <c>DimensionRecordPart</c>: a content part is the one place the coding
/// standard allows them, and Orchard requires them — it materialises a part by deserialising the
/// content item's JSON into it, and the display driver writes to it when an editor posts. Not
/// sealed, for the same reason: Orchard builds parts and their editor shapes through proxies.
/// </remarks>
public class EmployeePart : ContentPart
{
    /// <summary>
    /// The natural key: unique within the tenant including against employees who have left, and
    /// immutable once the record exists.
    /// </summary>
    /// <remarks>
    /// This is what a recipe, an export and every integration name an employee by, because a
    /// generated content item id is tenant-specific and a file carrying one is portable nowhere.
    /// <c>EmployeeCodes</c> holds the format rule and says why it is looser than a dimension code's.
    /// </remarks>
    public string EmployeeCode { get; set; } = string.Empty;

    /// <summary>The English name. Required, and what the generated title is driven from.</summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// The Arabic name. Optional unless the tenant requires it — ADR-0003's addendum.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// Nationality as an ISO 3166-1 alpha-2 code, or empty when it has not been recorded.
    /// </summary>
    /// <remarks>
    /// A code rather than a taxonomy reference, for now. Nationality drives statutory treatment —
    /// GOSI in Saudi Arabia, LMRA in Bahrain, the national-vs-expatriate split every Gulf payroll
    /// needs — and those rules are written against a stable code, not against whatever a tenant
    /// happened to call the row. A lookup list for the editor is session B's concern and will not
    /// change what is stored here.
    /// </remarks>
    public string NationalityCode { get; set; } = string.Empty;

    public Gender Gender { get; set; } = Gender.Unspecified;

    /// <summary>The first day of employment. Required: every dated question about tenure starts here.</summary>
    public DateOnly JoinDate { get; set; }

    /// <summary>
    /// Where the employee is in their lifecycle. Changed only through <c>IEmployeeService</c>'s
    /// dated transitions, never by editing the record.
    /// </summary>
    /// <remarks>
    /// Read-only in the editor on purpose. Exiting an employee closes their placements and their
    /// headships on the exit date and raises an event three modules subscribe to; none of that can
    /// happen from a dropdown on a form, and a dropdown that silently did none of it would leave an
    /// employee who reads as gone and is still placed, still heading units and still being paid.
    /// </remarks>
    public EmploymentStatus Status { get; set; } = EmploymentStatus.Prospective;

    /// <summary>
    /// The first day <see cref="Status"/> was true. Always the first day, including for
    /// <see cref="EmploymentStatus.Exited"/>.
    /// </summary>
    /// <remarks>
    /// Every transition is dated and nothing is defaulted on the write, so this is always a date
    /// somebody stated. The full history of transitions is in the audit trail; this is the one the
    /// record currently stands on.
    ///
    /// <b>The exit is the one worth being careful about.</b> A person's <em>last day</em> and the
    /// first day they are an ex-employee are consecutive days, not the same day, and both numbers
    /// get used: placements and headships are closed on the last day inclusive, while "had they
    /// left by this date" is asked of the day after. Rather than let one field mean a last day in
    /// one state and a first day in every other — which is how an off-by-one survives review —
    /// <c>IEmployeeService.ExitAsync</c> takes the last day, stores the day after it here, and
    /// <see cref="ExitedOn"/> converts back. So this property means exactly what it is called,
    /// always.
    /// </remarks>
    public DateOnly StatusEffectiveFrom { get; set; }

    /// <summary>
    /// The content item id of the employee this one reports to, or empty at the top of a reporting
    /// line.
    /// </summary>
    /// <remarks>
    /// Stored as an id rather than a code because it is a reference within one tenant, where an id
    /// is stable and a code is what a person types. The recipe step resolves it by code on the way
    /// in and the export writes it back as a code on the way out, which is where portability is
    /// actually needed.
    /// </remarks>
    public string LineManagerEmployeeId { get; set; } = string.Empty;

    /// <summary>The media path of the employee's photograph, or empty.</summary>
    public string PhotoPath { get; set; } = string.Empty;

    /// <summary>The name as the value object the services pass around.</summary>
    public BilingualText Name => new(NameEn, NameAr);

    /// <summary>Whether the employee had already left by <paramref name="asAt"/>.</summary>
    /// <remarks>
    /// Read from the status and its date together rather than from the status alone, so that a
    /// question about last March is not answered by what is true today. An employee who left in
    /// June was not an ex-employee in March, and an approval re-resolving that period must not be
    /// told otherwise.
    ///
    /// False on the last day itself: somebody leaving on 30 June is employed on 30 June.
    /// </remarks>
    public bool HasLeftBy(DateOnly asAt) =>
        Status == EmploymentStatus.Exited && StatusEffectiveFrom <= asAt;

    /// <summary>
    /// The last day of employment, or null while the employee has not left.
    /// </summary>
    /// <remarks>
    /// The day before <see cref="StatusEffectiveFrom"/>, which is the first day they were an
    /// ex-employee. This is the number a screen shows and a person recognises, and the one every
    /// placement and headship is closed on.
    /// </remarks>
    public DateOnly? ExitedOn =>
        Status == EmploymentStatus.Exited ? StatusEffectiveFrom.AddDays(-1) : null;
}
