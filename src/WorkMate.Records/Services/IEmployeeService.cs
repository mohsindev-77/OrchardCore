using WorkMate.Core;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// The only way the rest of the product creates, changes or moves an employee through their
/// lifecycle.
/// </summary>
/// <remarks>
/// Tenant-scoped by construction — nothing here takes a tenant id, the shell scope supplies it —
/// and permission-checked here rather than only in a controller, so the admin screens, the recipe
/// steps and the API are held to the same rules by the same code.
///
/// <b>Every dated write states its date.</b> There is no defaulting on a write anywhere on this
/// interface: a silent default is how dating errors enter the data, and an employee's dates are the
/// ones payroll, leave accrual and gratuity are all computed from. Reads default to today.
/// </remarks>
public interface IEmployeeService
{
    /// <summary>
    /// Creates an employee in <see cref="EmploymentStatus.Prospective"/> and raises
    /// <c>EmployeeCreated</c>.
    /// </summary>
    /// <remarks>
    /// Always prospective, whatever the join date. Activating somebody is a decision a person makes
    /// and dates, not something inferred from a date having passed — "we hired them and they never
    /// turned up" is a real outcome, and a status that activated itself could not represent it. An
    /// import that wants them active says so with <see cref="ActivateAsync"/>, which is one more
    /// line in the recipe step and one fewer thing the model has to guess.
    /// </remarks>
    /// <param name="code">The natural key. Validated, required, and immutable from here on.</param>
    Task<RecordResult<EmployeeRecord>> CreateAsync(
        string code,
        BilingualText name,
        DateOnly joinDate,
        EmployeeDetails? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the editable core fields. Not the code, not the status, and not the join date.
    /// </summary>
    /// <remarks>
    /// The three it refuses are the three something else depends on. The code is the natural key
    /// every recipe and integration names the employee by. The status is only reachable through the
    /// dated transitions below, because changing it has consequences — an exit closes placements and
    /// headships — that a field edit cannot carry out. The join date is changed through
    /// <see cref="CorrectJoinDateAsync"/>, which exists so that correcting it is visibly a
    /// correction rather than an ordinary edit.
    /// </remarks>
    Task<RecordResult<EmployeeRecord>> UpdateAsync(
        string employeeId,
        BilingualText name,
        EmployeeDetails details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the join date, which every question about tenure is computed from.
    /// </summary>
    /// <remarks>
    /// Its own operation rather than a field on <see cref="UpdateAsync"/>, for the reason the
    /// dimension engine splits a corrective rename from a substantive one: this rewrites the past
    /// on purpose. Gratuity, leave accrual and probation all move when it does, so it is audited
    /// separately and needs the same permission as a lifecycle change rather than the one that
    /// edits a phone number.
    /// </remarks>
    Task<RecordResult<EmployeeRecord>> CorrectJoinDateAsync(
        string employeeId,
        DateOnly joinDate,
        CancellationToken cancellationToken = default);

    // ---- lifecycle --------------------------------------------------------------------
    //
    // Specification section 5's five states, each transition dated, each raising a domain event.
    // The permitted moves are declared once in EmployeeLifecycle and enforced here, so a screen
    // and a recipe cannot disagree about what is possible.

    /// <summary>Puts a prospective, on-leave or suspended employee to work from a stated date.</summary>
    Task<RecordResult<EmployeeRecord>> ActivateAsync(
        string employeeId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an active employee as away on extended leave from a stated date.</summary>
    Task<RecordResult<EmployeeRecord>> PutOnLeaveAsync(
        string employeeId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>Suspends an employee from a stated date. They stay employed and stay placed.</summary>
    Task<RecordResult<EmployeeRecord>> SuspendAsync(
        string employeeId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends employment: closes every placement and every headship on <paramref name="lastDay"/>,
    /// inclusive, and raises the event payroll computes a final settlement from.
    /// </summary>
    /// <remarks>
    /// <paramref name="lastDay"/> is the last day of <em>service</em>, which is the number a person
    /// recognises and the number placements are closed on. The status is recorded as taking effect
    /// the day after it; see <c>EmployeePart.StatusEffectiveFrom</c>.
    ///
    /// Closing the headships matters as much as closing the placements and is easier to forget: a
    /// leaver who stays recorded as a head is somebody an approval still routes to, and nothing
    /// about that failure is visible — the chart and the routing agree with each other, and both
    /// are wrong.
    /// </remarks>
    Task<RecordResult<EmployeeExit>> ExitAsync(
        string employeeId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What <see cref="ExitAsync"/> would close, without closing any of it.
    /// </summary>
    /// <remarks>
    /// The dry-run-then-apply shape this platform uses for every operation that quietly changes
    /// what something else resolves to — a move, a merge, a retirement, a level change. The units
    /// left without a head are the part worth seeing before committing: a department losing its
    /// approver on Friday is a fact somebody has to act on, and discovering it from a stuck
    /// approval the following week is too late.
    /// </remarks>
    Task<RecordResult<EmployeeExitPlan>> PlanExitAsync(
        string employeeId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings a leaver back as <see cref="EmploymentStatus.Active"/> from a stated date.
    /// </summary>
    /// <remarks>
    /// Reopens nothing. Their old placements stay closed on the day they left and their old
    /// headships stay ended, because where somebody sat before they left is not where they sit now
    /// — and a rehire that silently restored last year's placements would put them back in a unit
    /// that may no longer exist. Placing them again is a separate, dated decision.
    /// </remarks>
    Task<RecordResult<EmployeeRecord>> ReinstateAsync(
        string employeeId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    // ---- reads ------------------------------------------------------------------------

    /// <summary>The employee with this id, or null.</summary>
    Task<EmployeeRecord?> GetAsync(string employeeId, CancellationToken cancellationToken = default);

    /// <summary>The employee with this code, or null. How a recipe and an export name one.</summary>
    Task<EmployeeRecord?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of employees, newest search surface first: code or name, optionally one status.
    /// </summary>
    /// <param name="search">Matched against the code and both halves of the name.</param>
    /// <param name="status">One status, or null for every status including leavers.</param>
    Task<Page<EmployeeRecord>> ListAsync(
        string? search = null,
        EmploymentStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An employee as the service hands them out: everything the fixed core holds, and nothing that
/// needs the content item loaded to read.
/// </summary>
/// <remarks>
/// A record rather than the content item, for the reason <c>DimensionNodeRef</c> is: a list page,
/// a picker and a designer card all need to name an employee, and none of them should pay for
/// loading one content item per row to do it.
/// </remarks>
public sealed record EmployeeRecord(
    string EmployeeId,
    string Code,
    string NameEn,
    string NameAr,
    DateOnly JoinDate,
    EmploymentStatus Status,
    DateOnly StatusEffectiveFrom,
    DateOnly? DateOfBirth,
    string NationalityCode,
    Gender Gender,
    string LineManagerEmployeeId,
    string PhotoPath)
{
    /// <summary>The name as the value object the services pass around.</summary>
    public BilingualText Name => new(NameEn, NameAr);

    /// <summary>The last day of service, or null while they have not left.</summary>
    public DateOnly? ExitedOn =>
        Status == EmploymentStatus.Exited ? StatusEffectiveFrom.AddDays(-1) : null;

    /// <summary>Whether they had already left by <paramref name="asAt"/>. False on the last day.</summary>
    public bool HasLeftBy(DateOnly asAt) =>
        Status == EmploymentStatus.Exited && StatusEffectiveFrom <= asAt;
}

/// <summary>
/// The core fields that are not the name, the code, the status or the join date.
/// </summary>
/// <remarks>
/// Grouped into one value so that <see cref="IEmployeeService.CreateAsync"/> and
/// <see cref="IEmployeeService.UpdateAsync"/> do not grow a parameter every time the core does, and
/// so a caller that only wants to set a name can pass nothing at all. Every member is optional
/// because every one of them legitimately is: an import that carries a name and a join date and
/// nothing else is an ordinary first import.
/// </remarks>
public sealed record EmployeeDetails(
    DateOnly? DateOfBirth = null,
    string? NationalityCode = null,
    Gender Gender = Gender.Unspecified,
    string? LineManagerEmployeeId = null,
    string? PhotoPath = null);

/// <summary>What an exit did: the record as it now stands, and what it closed.</summary>
public sealed record EmployeeExit(
    EmployeeRecord Employee,
    DateOnly LastDayOfService,
    int AssignmentsClosed,
    int HeadshipsClosed,
    IReadOnlyList<VacatedUnit> UnitsLeftWithoutAHead);

/// <summary>What an exit would do, before any of it is done.</summary>
/// <param name="Assignments">Every placement still running on the last day, across every axis.</param>
/// <param name="UnitsLosingTheirHead">
/// The units this person heads that would be left vacant. The part of the plan worth reading.
/// </param>
public sealed record EmployeeExitPlan(
    EmployeeRecord Employee,
    DateOnly LastDayOfService,
    IReadOnlyList<Dimensions.Services.EmployeeAssignment> Assignments,
    IReadOnlyList<VacatedUnit> UnitsLosingTheirHead)
{
    /// <summary>Whether the exit would leave any unit without a head.</summary>
    public bool LeavesAUnitVacant => UnitsLosingTheirHead.Count > 0;
}

/// <summary>
/// A unit an exit leaves without a head, named rather than identified.
/// </summary>
/// <remarks>
/// Carries the names rather than only the ids, for the reason <c>OrphanedByParentRetirement</c>
/// does: the caller would otherwise have to resolve them itself on a date the unit may not be
/// effective on, and a screen that falls back to a content item id has told the reader nothing.
/// </remarks>
public sealed record VacatedUnit(
    string StructureId,
    string StructureCode,
    string RecordId,
    string RecordCode,
    string NameEn,
    string NameAr);
