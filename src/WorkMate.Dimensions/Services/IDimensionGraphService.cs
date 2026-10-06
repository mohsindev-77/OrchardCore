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
    /// The dimension types a new unit may be, if it is placed under
    /// <paramref name="parentRecordId"/> on this structure.
    /// </summary>
    /// <param name="parentRecordId">The parent, or null for a root of the structure.</param>
    /// <remarks>
    /// The same rules <see cref="IDimensionValidator.ValidatePlacementAsync"/> enforces, read
    /// forwards instead of backwards: the level below the parent's, every deeper level too when
    /// the structure allows skipping, and the parent's own level when its type declares
    /// self-nesting. It lives here rather than being worked out by the screen that offers the
    /// choice, so that what the designer lets a user pick and what the validator accepts cannot
    /// come to disagree.
    ///
    /// Empty means nothing may be added there — the parent is already at the deepest level, and
    /// the caller should say so rather than offering a form that can only be refused.
    /// </remarks>
    Task<IReadOnlyList<string>> GetPermittedChildTypeIdsAsync(
        string structureId,
        string? parentRecordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that these units were left with no parent on purpose, because
    /// <paramref name="formerParentId"/> retired on <paramref name="retiredOn"/>.
    /// </summary>
    /// <remarks>
    /// Changes nothing about where the records sit: they already have no parent from that date,
    /// because the closure intersects every ancestor row with the ancestor's own effective range
    /// and the parent's has just been capped. This only records <em>why</em>, so the designer can
    /// say so instead of filing them under "never placed".
    /// </remarks>
    Task MarkOrphanedByParentRetirementAsync(
        string structureId,
        IReadOnlyList<string> childRecordIds,
        string formerParentId,
        DateOnly retiredOn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// For each of <paramref name="recordIds"/> that lost its parent to a retirement, which parent
    /// and when. Records absent from the result were never placed, or were placed and are still.
    /// </summary>
    Task<IReadOnlyDictionary<string, OrphanedByParentRetirement>> GetOrphanedByParentRetirementAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a node on a structure for the first time, from <paramref name="effectiveFrom"/>.
    /// </summary>
    /// <param name="parentRecordId">The parent, or null to make the node a root of this structure.</param>
    /// <remarks>
    /// Refuses, rather than moving, if the node already has a parent link on this structure: a
    /// first placement and a reparenting are different acts under different permissions, and this
    /// one must not become a way to do the other. It checks
    /// <see cref="Permissions.ManageDimensionRecords"/>, because placing a record that has never
    /// been anywhere changes no historical resolution — see
    /// <see cref="IDimensionService.AddUnitAsync"/>, which is the only intended caller.
    /// </remarks>
    Task<DimensionResult<int>> PlaceAsync(
        string structureId,
        string recordId,
        string? parentRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every unit <paramref name="recordId"/> could legitimately be placed under on this
    /// structure, by its own level rules, as at a date.
    /// </summary>
    /// <remarks>
    /// Excludes the unit itself and everything under it, which would be a cycle, and anything
    /// whose level does not permit this unit's type. The write re-validates whichever is chosen;
    /// this is what keeps the picker from offering choices that were never going to work, and what
    /// a drag checks before it will accept a drop.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> GetPlacementTargetsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every unit <paramref name="recordId"/> could be merged into: the units that could stand
    /// where it does, which is to say ones of its own dimension type.
    /// </summary>
    Task<IReadOnlyList<DimensionNodeRef>> GetMergeTargetsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The placements on record for a unit on one structure, newest first — what a cancellation
    /// can be asked to undo.
    /// </summary>
    /// <remarks>
    /// Read off the link's own dated parent list rather than from the closure, because that list
    /// is the record of decisions: each entry is one move somebody made, on the date they made it
    /// effective from, and cancelling identifies a move by exactly that date.
    /// </remarks>
    Task<IReadOnlyList<RecordedMove>> GetRecordedMovesAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What <see cref="MoveAsync"/> would do, without doing any of it.
    /// </summary>
    /// <remarks>
    /// Runs the same validator the move runs, so the screen shows the same violations the write
    /// would raise; the write runs them again and remains the authority. It also reports the range
    /// the new link would actually claim, which is the one thing a person cannot work out from the
    /// form: a backdated move stops at the day before whatever was recorded after it, and ADR-0005
    /// requires the designer to say so rather than let a late correction look like a replacement.
    /// </remarks>
    Task<DimensionResult<MovePlan>> PlanMoveAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
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
