using WorkMate.Core;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// Where people sit on an axis, and when.
/// </summary>
/// <remarks>
/// The employee record holds no department field: every placement is an assignment row. That is
/// the one rule that follows directly from the model, per architecture section 2, and it is what
/// makes a mid-month transfer a pair of dated rows rather than a destructive edit.
/// </remarks>
public interface IEmployeeAssignmentService
{
    /// <summary>
    /// Places an employee at a node from <paramref name="effectiveFrom"/>, closing whatever
    /// placement it displaces the day before.
    /// </summary>
    /// <param name="allocationPercent">
    /// How much of the employee this placement accounts for. 100 for an ordinary placement; less
    /// when the cost is split, in which case the concurrent placements must total 100.
    /// </param>
    /// <param name="isPrimary">
    /// Whether this is the placement that answers "where does this person work". Exactly one of
    /// an employee's concurrent placements on a structure is primary.
    /// </param>
    /// <param name="gradeRecordId">
    /// The grade the employee holds here, when the node is a position (D26 G). Null for a placement
    /// at a unit, and null is what every caller written before slice F6 passes. ADR-0026 gives the
    /// argument for its being a column on this row rather than an assignment of its own.
    /// </param>
    Task<DimensionResult<EmployeeAssignment>> PlaceAsync(
        string employeeId,
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        decimal allocationPercent = 100m,
        bool isPrimary = true,
        string? gradeRecordId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends an employee's placements on this axis on <paramref name="lastDay"/>, inclusive.
    /// </summary>
    Task<DimensionResult<int>> EndAsync(
        string employeeId,
        string structureId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an employee's concurrent placements on this axis from
    /// <paramref name="effectiveFrom"/> with the split given.
    /// </summary>
    /// <remarks>
    /// One operation rather than several places, because the rules that matter — allocations
    /// totalling 100, exactly one primary — are about the set as a whole. Applying a split one
    /// row at a time would be invalid at every step but the last.
    /// </remarks>
    Task<DimensionResult<IReadOnlyList<EmployeeAssignment>>> ReallocateAsync(
        string employeeId,
        string structureId,
        IReadOnlyList<AssignmentSplitEntry> split,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Where the employee sits on this axis on a date: the primary placement, or null if they
    /// are not placed then.
    /// </summary>
    Task<EmployeeAssignment?> GetEffectiveAsync(
        string employeeId,
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every placement the employee has on this axis on a date, including split ones.</summary>
    Task<IReadOnlyList<EmployeeAssignment>> GetAllEffectiveAsync(
        string employeeId,
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every employee at or below a node as at a date. The hot query: payroll, reporting,
    /// approvals and data visibility all issue it.
    /// </summary>
    /// <param name="includeDescendants">
    /// When false, only employees placed at the node itself. When true, the whole subtree as at
    /// that date.
    /// </param>
    /// <remarks>
    /// Paged and never unbounded. The subtree is narrowed by a correlated sub-select against the
    /// closure index rather than by passing a list of descendant ids: at 5,000 records the list
    /// would exceed SQL Server's 2,100-parameter limit and the query would stop working on a
    /// customer's database while continuing to pass on SQLite.
    /// </remarks>
    Task<Page<EmployeeAssignment>> GetEmployeesUnderAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        bool includeDescendants = true,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everybody at a unit on a date, with the rows that put them there.
    /// </summary>
    /// <param name="includeDescendants">
    /// When false, only people placed at the node itself — which is what the designer card counts.
    /// When true, the whole subtree as at that date.
    /// </param>
    /// <remarks>
    /// Unpaged, and one of the few reads here that is. Its caller is the employee list filtered to a
    /// unit, which needs the whole membership before it can search within it: a page of the
    /// members is not something a search can be applied to without giving the wrong total. The set
    /// is bounded by the unit's own headcount rather than by the tenant's, which is the number the
    /// screen is about.
    ///
    /// Rows rather than ids, because the screen says what each row <em>is</em> — primary or
    /// secondary, and for how much — and a caller given only ids would have to ask again per
    /// person to find out.
    /// </remarks>
    Task<IReadOnlyList<EmployeeAssignment>> GetAssignmentsAtAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        bool includeDescendants = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every placement each of these employees holds on one structure, as at a date.
    /// </summary>
    /// <remarks>
    /// <see cref="GetPrimaryPlacementsAsync"/> answers "where does this person work" and is
    /// deliberately one row per person. This answers the other question a list of people asks —
    /// "and what else" — because a split allocation is several rows and a screen that showed one of
    /// them would be picking arbitrarily between facts that are all true.
    ///
    /// Batched for the reason every other read here is: the caller is a page of fifty people, and
    /// <see cref="GetAllEffectiveAsync"/> per row would be fifty queries to draw one column.
    ///
    /// An employee with no placement on the date is absent rather than present with an empty list,
    /// so the common case on a tenant with no structure costs nothing to carry.
    /// </remarks>
    Task<IReadOnlyDictionary<string, IReadOnlyList<EmployeeAssignment>>> GetPlacementsAsync(
        string structureId,
        IReadOnlyList<string> employeeIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every placement row on record for one employee on one structure, earliest first — the
    /// closed ones as well as the open one.
    /// </summary>
    /// <remarks>
    /// The placement counterpart of <see cref="GetHeadHistoryAsync"/>, and it exists for one
    /// caller: the screen that changes a split has to know the date of the latest change already
    /// recorded before it accepts a new one.
    ///
    /// <see cref="ReallocateAsync"/> states the whole picture from its effective date onwards, so a
    /// reallocation backdated before a change already on record would replace that change rather
    /// than slot in behind it — which is the "overwrite" behaviour ADR-0005's addendum rejected for
    /// moves, reached by a different door. The screen refuses it instead, and this is what it reads
    /// to know.
    /// </remarks>
    Task<IReadOnlyList<EmployeeAssignment>> GetHistoryAsync(
        string employeeId,
        string structureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Where each of these employees primarily sits on one axis, as at a date.
    /// </summary>
    /// <remarks>
    /// One query for a set of people rather than one per person, for the reason
    /// <see cref="CountEmployeesAtAsync"/> is batched: the caller is a list of candidates on a
    /// picker, and a lookup per row would be a query per row on every keystroke.
    ///
    /// Primary only, because the question is "where does this person work" and that is what the
    /// primary flag answers — a split allocation or a matrix secondary would give two answers to a
    /// question with one. Somebody not placed on the axis is absent rather than present with null,
    /// so the common case of a picker on a tenant with no structure costs nothing to carry.
    /// </remarks>
    Task<IReadOnlyDictionary<string, EmployeeAssignment>> GetPrimaryPlacementsAsync(
        string structureId,
        IReadOnlyList<string> employeeIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every node the employee touches, across every axis, as at a date. Used by visibility and
    /// by the employee's own profile.
    /// </summary>
    Task<IReadOnlyList<EmployeeAssignment>> GetAllAxesAsync(
        string employeeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many employees are placed at each of <paramref name="recordIds"/> as at a date — the
    /// node itself only, not its descendants.
    /// </summary>
    /// <remarks>
    /// One query for a set of nodes rather than one per node, because the caller is the
    /// organisation designer drawing a row of cards: a count per card issued separately would be
    /// a query per card on every expand.
    ///
    /// The ids are passed as an <c>IN</c> list, unlike <see cref="GetEmployeesUnderAsync"/>'s
    /// correlated sub-select, and that is safe for the opposite reason: this is called with one
    /// node's children or one page of roots — tens of ids — never a whole 5,000-record subtree.
    /// A caller that needs a subtree's total asks <see cref="GetEmployeesUnderAsync"/> for it.
    ///
    /// A node with nobody at it is absent from the result rather than present with zero, so the
    /// common case on a tenant with no employees yet costs nothing to carry.
    /// </remarks>
    Task<IReadOnlyDictionary<string, int>> CountEmployeesAtAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    // ---- unit heads -------------------------------------------------------------------
    //
    // On this interface rather than on one of their own because a head is answered from the same
    // place a placement is, and because the one thing that must never happen is two sources for
    // "who leads this unit" — the designer's card and prompt 5's approval routing both read these,
    // so what the chart shows and what an approval routes to cannot disagree.
    //
    // Not an assignment, though. ADR-0012: a head need not be a member of the unit they head, so
    // an appointment carries no allocation, is counted by no headcount and is charged to no cost
    // centre. None of the methods below touches AllocationPercent or IsPrimary, and that is the
    // whole of the distinction.

    /// <summary>
    /// Records <paramref name="employeeId"/> as the head of a unit from
    /// <paramref name="effectiveFrom"/>, closing whatever term it displaces the day before.
    /// </summary>
    /// <remarks>
    /// The same displacement arithmetic as a placement: the outgoing head's term runs to the day
    /// before and the incoming one from the date given, with no overlap and no gap. Re-appointing
    /// the employee who already holds the post from that date changes nothing and succeeds, which
    /// is what makes <c>unit-heads</c> safe to re-run.
    /// </remarks>
    Task<DimensionResult<HeadAppointment>> SetHeadAsync(
        string structureId,
        string recordId,
        string employeeId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the unit's current headship on <paramref name="lastDay"/>, inclusive, leaving the post
    /// vacant from the day after.
    /// </summary>
    /// <returns>How many terms were closed: one, or zero when the post was already vacant.</returns>
    Task<DimensionResult<int>> ClearHeadAsync(
        string structureId,
        string recordId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Who led the unit on a date, or null when the post was vacant then.
    /// </summary>
    /// <remarks>
    /// Null is "Vacant", and a caller must say so rather than say nothing: a card that omits the
    /// line when there is no head is a card whose height changes as posts are filled, and one that
    /// shows a dash is making a claim about the organisation that nothing has checked.
    /// </remarks>
    Task<HeadAppointment?> GetHeadAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Who led each of <paramref name="recordIds"/> on a date, keyed by record id, absent for a
    /// unit whose post was vacant.
    /// </summary>
    /// <remarks>
    /// Batched for the same reason <see cref="CountEmployeesAtAsync"/> is, and with the same
    /// caveat: the ids go in as an <c>IN</c> list because the caller is a row of designer cards —
    /// tens of ids — never a whole subtree.
    /// </remarks>
    Task<IReadOnlyDictionary<string, HeadAppointment>> GetHeadsAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every unit this employee leads on a date, across every axis.</summary>
    /// <remarks>
    /// Read by the employee's own profile, and by the exit dry run, which has to be able to say
    /// which units would be left vacant before anybody commits to leaving them that way.
    /// </remarks>
    Task<IReadOnlyList<HeadAppointment>> GetHeadshipsOfAsync(
        string employeeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every headship this employee holds, on every axis, on <paramref name="lastDay"/>.
    /// </summary>
    /// <remarks>
    /// What <c>IEmployeeService.ExitAsync</c> calls, alongside ending their placements. A leaver
    /// who stays recorded as a head is how an approval routes to somebody who no longer works here
    /// — the failure is silent, because the chart and the routing agree with each other and both
    /// are wrong.
    /// </remarks>
    Task<DimensionResult<int>> EndHeadshipsOfAsync(
        string employeeId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every term on record for a unit, earliest first — who led it and when, not only who leads
    /// it now.
    /// </summary>
    /// <remarks>
    /// "Who led this unit last March" is answerable from <see cref="GetHeadAsync"/>; this is what
    /// the export carries, because an export that kept only the current head would answer that
    /// question differently in the imported tenant.
    /// </remarks>
    Task<IReadOnlyList<HeadAppointment>> GetHeadHistoryAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken = default);

    // ---- the whole tenant, for an export ----------------------------------------------
    //
    // Deliberately unpaged, and the only two reads here that are. Everything else on this
    // interface answers a question about one employee, one node or one page, because everything
    // else is on a screen's path. These two have one caller — the deployment source — whose job is
    // to reproduce a tenant: a page of the placements is not a tenant, and an export that silently
    // stopped at fifty would be wrong in the way nobody notices until a restore.

    /// <summary>
    /// Every placement of every employee on every axis, over all time.
    /// </summary>
    /// <remarks>
    /// Rows, not effective rows: a split allocation is several, and a person who transferred in
    /// March has the closed row as well as the open one. The export reproduces the decisions, not
    /// the position on one date.
    /// </remarks>
    Task<IReadOnlyList<EmployeeAssignment>> GetAllAssignmentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every head term on every unit on every axis, over all time.
    /// </summary>
    /// <remarks>
    /// Separate from the placements because a head is not a placement — ADR-0012 — so a tenant can
    /// have appointments and no assignments, or the reverse, and neither implies the other.
    /// </remarks>
    Task<IReadOnlyList<HeadAppointment>> GetAllHeadAppointmentsAsync(
        CancellationToken cancellationToken = default);
}
