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
    Task<DimensionResult<EmployeeAssignment>> PlaceAsync(
        string employeeId,
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        decimal allocationPercent = 100m,
        bool isPrimary = true,
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
}
