using WorkMate.Core;

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
    /// Retires a record from <paramref name="effectiveDate"/>: it disappears from pickers and
    /// keeps resolving for historical queries.
    /// </summary>
    Task<DimensionResult<DimensionNodeRef>> RetireAsync(
        string recordId,
        DateOnly effectiveDate,
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
