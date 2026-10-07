using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// Records: create, change, retire, move and merge. Every operation dated.
/// </summary>
/// <remarks>
/// This is the aggregate root for a dimension record. It owns the record's content item, its
/// dated name history, and — through <see cref="IDimensionGraphService"/> — its place on every
/// axis. Nothing else writes those three together, because a record that exists without its
/// closure rows, or whose name history disagrees with its current name, is the kind of
/// inconsistency that stays plausible while being wrong.
/// </remarks>
public interface IDimensionService
{
    /// <summary>
    /// Creates a record of a dimension type, with its self pairs on every axis the type is a
    /// level of, and the first period of its name history.
    /// </summary>
    Task<DimensionResult<DimensionNodeRef>> CreateAsync(
        string dimensionTypeId,
        string code,
        BilingualText name,
        EffectiveRange effectiveRange,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a unit to a structure: creates the record and puts it in its place, as one operation.
    /// </summary>
    /// <param name="parentRecordId">The unit it sits under, or null for a root of the structure.</param>
    /// <param name="attributes">
    /// Values for the custom attributes the dimension type declares. Attributes the type does not
    /// declare are rejected; attributes it declares and requires must be present.
    /// </param>
    /// <remarks>
    /// One method rather than <see cref="CreateAsync"/> followed by <see cref="MoveAsync"/>,
    /// for two reasons.
    ///
    /// The first is that the caller should not have to sequence them. A record created and then
    /// left unplaced because the second call failed is exactly the half-finished state the
    /// unplaced panel exists to reveal, and asking every caller to handle it correctly is asking
    /// for it to be handled correctly nowhere. Both the record and the placement are validated
    /// before either is written.
    ///
    /// The second is the permission. <see cref="Permissions.MoveDimensionRecords"/> exists because
    /// reparenting silently changes what every historical report resolves to — that is the whole
    /// argument in <see cref="Permissions"/>'s own remarks. A new unit's first placement changes
    /// no history: there is no prior parent and nothing resolved under it yesterday. So this is
    /// <see cref="Permissions.ManageDimensionRecords"/>, the permission for creating a record,
    /// and the HR administrator who holds it can add a department without also being given the
    /// power to reorganise the company. Moving that unit afterwards is still a move.
    /// </remarks>
    Task<DimensionResult<DimensionNodeRef>> AddUnitAsync(
        string structureId,
        string? parentRecordId,
        string dimensionTypeId,
        string code,
        BilingualText name,
        DateOnly effectiveFrom,
        IReadOnlyList<Models.DimensionAttributeValue>? attributes = null,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the fields that are not the name and not the placement.</summary>
    Task<DimensionResult<DimensionNodeRef>> UpdateAsync(
        string recordId,
        string costCentreCode,
        string glAccountRef,
        string headEmployeeId,
        int sortOrder,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A <b>corrective</b> rename: the record was always called this and the old text was a
    /// mistake. Applies retrospectively to the name period covering
    /// <paramref name="withinPeriodContaining"/>, which may be a closed one — a typo is usually
    /// found by running last year's report and not recognising the name.
    /// </summary>
    Task<DimensionResult<DimensionNodeRef>> CorrectNameAsync(
        string recordId,
        BilingualText name,
        DateOnly withinPeriodContaining,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A <b>substantive</b> rename: the record genuinely became something else on
    /// <paramref name="effectiveFrom"/>. Prior periods keep the old name.
    /// </summary>
    Task<DimensionResult<DimensionNodeRef>> RenameAsync(
        string recordId,
        BilingualText name,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What retiring a record on <paramref name="effectiveDate"/> would do, and what — if
    /// anything — stops it, without doing any of it.
    /// </summary>
    /// <remarks>
    /// Architecture section 6 makes retirement the middle of three outcomes: a record with no
    /// references at all may be deleted outright, one referenced only by history is retired, and
    /// one referenced by live data is refused "with the specific blockers listed so the user can
    /// act. Never a generic failure message." This is how the screen gets that list before the
    /// user commits, the same dry-run-then-confirm shape as a level change, a move and a merge.
    /// <see cref="RetireAsync"/> runs the same assessment again on write.
    /// </remarks>
    Task<DimensionResult<RetirePlan>> PlanRetireAsync(
        string recordId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a record from <paramref name="effectiveDate"/>: it disappears from pickers and
    /// keeps resolving for historical queries.
    /// </summary>
    /// <param name="childrenDispositions">
    /// What happens to the units underneath, keyed by structure id. One entry is required for
    /// every structure the plan reports children on; a retirement with any of them missing is
    /// refused, naming the structure it has not been told about.
    /// </param>
    /// <remarks>
    /// A parent closing over its live children is the defect this parameter exists for. Nothing
    /// about the links changes when a parent retires — the closure stops resolving them because
    /// every ancestor row is intersected with the ancestor's own effective range — so the children
    /// simply fall out of the tree, still active, with nothing anywhere to say what happened. That
    /// is a legitimate end state, but only as a decision somebody made: move them, close them with
    /// their parent, or leave them and let the screen say so.
    ///
    /// Keyed by structure because retirement closes the record itself and therefore lands on every
    /// structure it sits on at once. A unit can be a parent on more than one, and the screen only
    /// ever shows one of them; answering for the one in view and silently stranding children on
    /// the other is the same defect wearing a different hat.
    ///
    /// Checked here as well as on the screen, because specification rule 5 puts the authority in
    /// the service and because a recipe or an API client can retire a record too.
    /// </remarks>
    Task<DimensionResult<DimensionNodeRef>> RetireAsync(
        string recordId,
        DateOnly effectiveDate,
        IReadOnlyDictionary<string, ChildrenDisposition>? childrenDispositions = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reparents a record on one axis from an explicit date.</summary>
    Task<DimensionResult<DimensionNodeRef>> MoveAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What cancelling the move effective on <paramref name="effectiveFrom"/> would do, without
    /// doing any of it: what the record would be restored under, and which employees' assignment
    /// resolution would change.
    /// </summary>
    Task<DimensionResult<CancelMovePlan>> PlanCancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the move effective on <paramref name="effectiveFrom"/>: the link it created is
    /// removed and the placement it displaced is restored to cover its period again, as if the
    /// move had never been recorded.
    /// </summary>
    /// <remarks>
    /// Never a silent delete: <paramref name="reason"/> is mandatory and, together with the
    /// acting user, is recorded against <see cref="DimensionAuditTrail.MoveCancelled"/>. Refused
    /// through the validator if the restored placement would break a rule today — the old parent
    /// retired or no longer a permitted level, most often — in which case nothing is written and
    /// no event is recorded.
    /// </remarks>
    /// <param name="reason">Why the move is being cancelled. Required; never defaulted.</param>
    /// <returns>What was actually restored, which must equal what <see cref="PlanCancelMoveAsync"/> said.</returns>
    Task<DimensionResult<CancelMovePlan>> CancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a merge would do, without doing any of it.
    /// </summary>
    /// <remarks>
    /// Architecture section 6 requires the dry run: "It runs in dry-run first and produces a
    /// report of exactly what will change." Exactly is the word that matters — a report that
    /// only approximately describes the change is worse than none, because it is the thing a
    /// human signs off.
    /// </remarks>
    Task<DimensionResult<MergePlan>> PlanMergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Folds one record into another: reassigns children and employees from the effective date,
    /// retires the source, and leaves prior-period reporting resolving to the source.
    /// </summary>
    /// <returns>What was actually done, which must equal what <see cref="PlanMergeAsync"/> said.</returns>
    Task<DimensionResult<MergePlan>> MergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every record of one dimension type, undated, for an export or a bulk read.
    /// </summary>
    /// <remarks>
    /// Undated and including retired records on purpose, which is why it is not the designer's
    /// read path: an export has to carry the units history resolves through, and a unit retired
    /// last March is exactly one of those. A screen asks <see cref="GetAsync"/> or the graph
    /// service, both of which answer as at a date.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> ListByTypeAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every period this record has been called something, earliest first.
    /// </summary>
    /// <remarks>
    /// <see cref="GetAsync"/> answers "what was it called on this date", which is what a screen
    /// needs. This answers "what has it ever been called", which is what an export needs: a
    /// substantive rename is a dated fact about the record, and an export that carried only the
    /// current name would silently flatten the history on the way into the next tenant — every
    /// report for an earlier period would then resolve to the new name, which is the exact defect
    /// the two kinds of rename exist to prevent.
    ///
    /// Empty for a record with no history document, which is a record that has never been renamed.
    /// </remarks>
    Task<IReadOnlyList<DimensionNamePeriod>> GetNameHistoryAsync(
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>The record, or null. <paramref name="asAt"/> defaults to today.</summary>
    Task<DimensionNodeRef?> GetAsync(
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The record with this code, or null. This is how a recipe or an import resolves a parent
    /// reference, so it matches on the code alone across every dimension type.
    /// </summary>
    Task<DimensionNodeRef?> GetByCodeAsync(
        string code,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>What the record was called on a date, from its dated name history.</summary>
    Task<BilingualText?> GetNameAsync(
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);
}
