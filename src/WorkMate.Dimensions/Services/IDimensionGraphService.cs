using WorkMate.Core;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The hierarchy: who is under whom, on which axis, on which date. The only way anything reads
/// the link or closure tables.
/// </summary>
/// <remarks>
/// Every read takes an effective date defaulting to today. There is no undated read path,
/// because an undated read path is how historical questions quietly start returning present-day
/// answers.
///
/// The maintenance operations are on this interface rather than hidden behind
/// <c>IDimensionService</c> because the record handler has to call them too: a record created
/// from Orchard's own content screens still needs its self pairs, and the handler and the
/// service share the ambient session so the record and its index rows commit together.
/// </remarks>
public interface IDimensionGraphService
{
    // ---- resolve ----------------------------------------------------------------------

    /// <summary>The node's ancestors on this axis, nearest first.</summary>
    Task<IReadOnlyList<DimensionNodeRef>> GetAncestorsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The node's descendants on this axis, excluding itself. Paged and never unbounded, per
    /// specification section 7.
    /// </summary>
    Task<Page<DimensionNodeRef>> GetDescendantsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>The node's immediate children on this axis.</summary>
    Task<IReadOnlyList<DimensionNodeRef>> GetChildrenAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many immediate children each of <paramref name="recordIds"/> has on this axis as at a
    /// date.
    /// </summary>
    /// <remarks>
    /// One query for a set of nodes rather than one per node: the caller is the organisation
    /// designer, which needs the count on every card it draws so that the expand control can say
    /// "2" before anything is fetched, and can be left off entirely for a unit with nothing under
    /// it. Without it a card cannot tell "not expanded yet" from "nothing to expand", and the two
    /// look different only after a click that appears to do nothing.
    ///
    /// A node with no children is absent from the result rather than present with zero.
    /// </remarks>
    Task<IReadOnlyDictionary<string, int>> CountChildrenAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The roots of this axis: every record of the dimension type declared at ordinal zero,
    /// effective on the date. The designer's tree starts here rather than at every self pair,
    /// because a record of a deeper level with no parent is not a second root — it is unplaced,
    /// and <see cref="GetUnplacedAsync"/> is where it is shown instead.
    /// </summary>
    Task<IReadOnlyList<DimensionNodeRef>> GetRootsAsync(
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every record whose dimension type is a level of this axis below ordinal zero, effective on
    /// the date, that has no parent on this axis as of that date.
    /// </summary>
    /// <remarks>
    /// A record created outside the designer — through a generic content screen, an import, or an
    /// API call — has no parent until something places it; see the README's note on "unplaced".
    /// This is the organisation designer's panel for finding exactly those, so nothing created
    /// outside the designer is silently invisible inside it.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> GetUnplacedAsync(
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every record of a dimension type on this axis, effective on the date, whose code or name in
    /// either language contains <paramref name="searchText"/>. Ordered, capped, and carrying no
    /// ancestor path — a caller that needs to expand the tree down to a match calls
    /// <see cref="GetAncestorsAsync"/> for it.
    /// </summary>
    Task<IReadOnlyList<DimensionNodeRef>> SearchAsync(
        string structureId,
        string searchText,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="recordId"/> sits at or below <paramref name="ancestorId"/>.</summary>
    Task<bool> IsUnderAsync(
        string structureId,
        string recordId,
        string ancestorId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>How far below its root the node sits, or null if it is not on this axis then.</summary>
    Task<int?> GetDepthAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    // ---- maintenance ------------------------------------------------------------------

    /// <summary>
    /// Gives a record its self pair on every structure whose levels include its dimension type,
    /// so a newly created record is resolvable as a root of those axes immediately.
    /// </summary>
    Task EnsureSelfPairsAsync(
        string recordId,
        string dimensionTypeId,
        EffectiveRange effectiveRange,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacts to a structure's levels changing: gives self pairs to the records of a dimension
    /// type the structure has just gained, and closes the rows for one it has lost.
    /// </summary>
    /// <remarks>
    /// Adding a level is retrospective by nature — the records of that type already exist, and
    /// they become part of this axis the moment the level is declared. Without this they would
    /// be invisible on an axis they belong to, with nothing to indicate why.
    /// </remarks>
    Task OnStructureLevelsChangedAsync(
        string structureId,
        IReadOnlyList<string> addedDimensionTypeIds,
        IReadOnlyList<string> removedDimensionTypeIds,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What replacing a structure's levels with <paramref name="newLevelDimensionTypeIds"/> would
    /// do to the records already on this axis, without changing anything.
    /// </summary>
    /// <remarks>
    /// <c>IStructureService.UpdateAsync</c> calls this before saving and refuses the whole change
    /// if <see cref="StructureLevelChangePlan.HasViolations"/>, the same dry-run-then-apply shape
    /// <see cref="PreviewCancelMoveAsync"/> and <c>IDimensionService</c>'s merge already use, so
    /// the preview a designer shows and what actually happens cannot drift apart.
    /// </remarks>
    /// <param name="newAllowSkipLevel">
    /// The proposed value, not the structure's current one — a change that also starts allowing
    /// skipped levels can make a reorder valid that would otherwise be refused.
    /// </param>
    Task<StructureLevelChangePlan> PlanLevelChangeAsync(
        string structureId,
        IReadOnlyList<string> newLevelDimensionTypeIds,
        bool newAllowSkipLevel,
        DateOnly asAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reparents a node from <paramref name="effectiveFrom"/>, closing the old link the day
    /// before and rebuilding the closure for the moved subtree only.
    /// </summary>
    /// <param name="newParentId">The new parent, or null to make the node a root of this axis.</param>
    Task<DimensionResult<int>> MoveAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What cancelling the move effective on <paramref name="effectiveFrom"/> would restore,
    /// without doing it.
    /// </summary>
    /// <remarks>
    /// Runs the same validation <see cref="CancelMoveAsync"/> does, against today's rules, so a
    /// caller can show a refusal before anyone asks to apply it.
    /// </remarks>
    Task<DimensionResult<CancelledMoveRestoration>> PreviewCancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the move effective on <paramref name="effectiveFrom"/> and restores the placement
    /// it displaced to cover its period again, as if the move had never been recorded.
    /// </summary>
    /// <remarks>
    /// Refused through the validator, against today's rules, if the restored placement would
    /// break one — the old parent is no longer a permitted level, most often. Carries no audit
    /// or reason: those belong to <c>IDimensionService.CancelMoveAsync</c>, the aggregate root's
    /// wrapper, in the same way <c>IDimensionService.MoveAsync</c> wraps this layer's
    /// <see cref="MoveAsync"/> without auditing it either.
    /// </remarks>
    Task<DimensionResult<CancelledMoveRestoration>> CancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a node's links and closure rows on every structure. Used by deletion.</summary>
    Task RemoveAsync(string recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconstructs the whole closure index for a structure from the link table.
    /// </summary>
    /// <returns>How many closure documents were written.</returns>
    Task<int> RebuildAsync(string structureId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the closure index against the links and reports any divergence, changing
    /// nothing.
    /// </summary>
    /// <param name="asAtDates">
    /// The dates to compare on. When null the service picks them: every date on which any link
    /// on this structure starts or ends, and the day either side of each. A check that only
    /// looked at today would miss exactly the drift that matters, because the index is correct
    /// today far more often than it is correct for last March.
    /// </param>
    Task<ClosureVerificationReport> VerifyAsync(
        string structureId,
        IReadOnlyList<DateOnly>? asAtDates = null,
        CancellationToken cancellationToken = default);
}
