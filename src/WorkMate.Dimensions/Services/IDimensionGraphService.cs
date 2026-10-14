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
    /// <param name="newRootDimensionTypeIds">The proposed root types, not the current ones.</param>
    /// <param name="newContainment">
    /// The proposed containment map, not the current one. Every parameter here is the proposal
    /// rather than the saved state for the same reason: the question is what the change would do,
    /// and a change that widens the map can make a placement valid that the current one refuses.
    /// </param>
    /// <param name="newIsStrict">
    /// The proposed strictness. It decides whether an undeclared pairing is a violation at all,
    /// so a change that also turns strictness off has fewer violations, not the same ones.
    /// </param>
    Task<StructureLevelChangePlan> PlanLevelChangeAsync(
        string structureId,
        IReadOnlyList<string> newLevelDimensionTypeIds,
        IReadOnlyList<string> newRootDimensionTypeIds,
        IReadOnlyList<Models.StructureContainment> newContainment,
        bool newIsStrict,
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
    /// Which of these records were deliberately taken off the tree, and the date it happened.
    /// </summary>
    /// <remarks>
    /// The third reason a record has no parent, alongside never having been placed and having had
    /// its parent retired underneath it. Readable only since stage D2 gave leaving the tree a dated
    /// entry of its own: before that it looked identical to never having been placed, and the
    /// unplaced panel told a unit somebody had moved off the chart last week that it had never been
    /// placed in this structure, which was simply untrue of it.
    ///
    /// Keyed by record id, and absent for a record whose lack of a parent has either of the other
    /// two explanations.
    /// </remarks>
    Task<IReadOnlyDictionary<string, DateOnly>> GetRemovedFromTreeAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
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
    /// Every unit on this axis that may hold an employee, effective on the date.
    /// </summary>
    /// <remarks>
    /// The forwards reading of the employee-attachment rule, for a picker. The backwards reading —
    /// what <c>IDimensionValidator.ValidateAssignmentAsync</c> refuses — comes from the same place,
    /// <c>StructureDocument.PermitsEmployeesAt</c>, and that is deliberate: ADR-0010 was written
    /// because the two readings of the containment rule had been implemented separately and
    /// disagreed on exactly the case the rule existed for. One rule, two directions, one
    /// implementation.
    ///
    /// An axis that declares nothing is unconstrained, so this returns every unit on it. Capped and
    /// ordered like <see cref="SearchAsync"/>, because it feeds the same kind of control.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> GetEmployeeAttachableUnitsAsync(
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The page of records of one dimension type a list screen should show: those matching
    /// <paramref name="searchText"/>, optionally only those at or below
    /// <paramref name="withinRecordId"/> on <paramref name="structureId"/>, in the state asked for.
    /// </summary>
    /// <remarks>
    /// <b>One indexed query for the type, then filtered and paged.</b> Specification section 7's
    /// "paged and never unbounded" is about the descendant and employee-under-a-node queries, where
    /// the bound is the whole tenant; here it is the number of records of one type, which is the
    /// number the screen is about — the same argument <c>GetAssignmentsAtAsync</c> makes for being
    /// unpaged underneath. A caller still gets a <see cref="Page{T}"/>, because a screen still has
    /// to page it.
    ///
    /// <paramref name="withinRecordId"/> is resolved through the closure index on the date given,
    /// so "within Retail Banking" means what it meant then rather than what it means today.
    ///
    /// <paramref name="structureId"/> does not by itself narrow the set: whether a record is on a
    /// structure is a property of its <em>type</em>, so every record of one type is on the same
    /// structures. It is what <paramref name="withinRecordId"/> is resolved against, and what a
    /// caller shows parents and headcounts for.
    /// </remarks>
    /// <param name="status">
    /// Which records to include, judged on <paramref name="asAt"/>: the ones effective then, the
    /// ones that are not, or both.
    /// </param>
    Task<Page<DimensionNodeRef>> SearchRecordsOfTypeAsync(
        string dimensionTypeId,
        string? searchText,
        string? structureId,
        string? withinRecordId,
        DimensionRecordStatus status = DimensionRecordStatus.Active,
        DateOnly? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same page, over several dimension types at once — the "All <em>Departments</em>" screen
    /// of decision D23 C, where a tenant's three Department types are one list.
    /// </summary>
    /// <remarks>
    /// <b>Several types, not a group.</b> The grouping is a menu concept (see
    /// <c>DimensionMenuGroup</c>) and nothing in the graph knows about it; what reaches here is the
    /// set of type ids the group resolved to, so this method stays a plain widening of
    /// <see cref="SearchRecordsOfTypeAsync"/> and the one-type call is the one-element case.
    ///
    /// Ordered across the whole set rather than type by type: the screen is a list of departments,
    /// and clustering Zenith's before Crescent's would make the Organisation column the sort key
    /// without saying so. Sort order first, then English name, which is what the single-type call
    /// already does — ordinal and language-neutral, because paging has to agree with itself between
    /// two readers.
    /// </remarks>
    Task<Page<DimensionNodeRef>> SearchRecordsOfTypesAsync(
        IReadOnlyList<string> dimensionTypeIds,
        string? searchText,
        string? structureId,
        string? withinRecordId,
        DimensionRecordStatus status = DimensionRecordStatus.Active,
        DateOnly? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every unit on this structure that may be the parent of a record of
    /// <paramref name="childDimensionTypeId"/>, matching <paramref name="searchText"/>.
    /// </summary>
    /// <remarks>
    /// The containment map read forwards, from the same <c>StructureDocument.Permits</c> that
    /// <c>IDimensionValidator.ValidatePlacementAsync</c> reads backwards — ADR-0010's lesson
    /// applied to the one rule it was written about. A picker that offered a parent the validator
    /// would refuse is a form whose only outcome is a refusal.
    ///
    /// It is the search-shaped counterpart of <see cref="GetPermittedChildTypeIdsAsync"/>, which
    /// answers the same question from the other end: that one is "given this parent, what may go
    /// under it", for a form that already knows where it is. This is "given this type, where may it
    /// go", for a form that does not.
    ///
    /// A non-strict structure tolerates a pairing it has no rule for, and this honours that the
    /// same way the pickers already do: on a non-strict structure every unit is offered, because
    /// every unit would be accepted.
    /// </remarks>
    Task<Page<DimensionNodeRef>> SearchPermittedParentsAsync(
        string structureId,
        string childDimensionTypeId,
        string? searchText,
        string? withinRecordId,
        DateOnly? asAt = null,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The page of employee-attachable units a picker should offer: those matching
    /// <paramref name="searchText"/>, optionally only those at or below
    /// <paramref name="withinRecordId"/>.
    /// </summary>
    /// <remarks>
    /// <b>Server-side, for the reason the employee picker is.</b> A control that filters a list it
    /// already holds can only narrow what was sent, and what was sent is the first page — so on a
    /// tenant with four hundred units the rest are unreachable and nothing says so.
    ///
    /// <paramref name="withinRecordId"/> is applied through the closure index, which is what makes
    /// "within Retail Banking" mean the whole subtree on the date rather than the immediate
    /// children. The same date resolves the subtree and the units, because a unit that was under a
    /// division last year and is not now must not be offered as though it still were.
    ///
    /// <see cref="GetEmployeeAttachableUnitsAsync"/> remains the no-script fallback the picker's
    /// <c>&lt;select&gt;</c> is rendered from; this is the same rule with a search and a scope on
    /// it, read from the same <c>PermitsEmployeesAt</c>.
    /// </remarks>
    /// <param name="searchText">Null or empty offers the first page unfiltered.</param>
    /// <param name="withinRecordId">Null does not narrow. A unit is within itself.</param>
    Task<Page<DimensionNodeRef>> SearchEmployeeAttachableUnitsAsync(
        string structureId,
        string? searchText,
        string? withinRecordId,
        DateOnly? asAt = null,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The units that can usefully scope a "within" filter: the containers on this axis.
    /// </summary>
    /// <remarks>
    /// A structure's employee-attachable types are the ones people sit at; everything else on the
    /// axis is a container, and a container is what somebody means by "within Retail Banking". On
    /// an axis that constrains nothing — where everything may hold employees — the root types are
    /// offered, because an unconstrained axis still has a top.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> GetEmployeeScopeUnitsAsync(
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Each of <paramref name="recordIds"/>' ancestors on this axis, root first, keyed by record
    /// id.
    /// </summary>
    /// <remarks>
    /// Batched, and that is the whole reason it exists rather than a loop over
    /// <see cref="GetAncestorsAsync"/>: its caller is a picker drawing twenty options with the full
    /// path on each, and a call per option would be twenty queries on every keystroke.
    ///
    /// The node itself is not in its own list, so a root's entry is empty rather than absent — a
    /// caller builds the displayed path by appending the node to what it is given.
    /// </remarks>
    /// <summary>
    /// How far below its root each of <paramref name="recordIds"/> sits on this structure, keyed by
    /// record id, absent for a record that is not on the structure at all on that date.
    /// </summary>
    /// <remarks>
    /// Batched <see cref="GetDepthAsync"/>, for a list screen drawing a page of rows: a call per
    /// row would be fifty queries to fill one column.
    ///
    /// <b>Absent and zero are different answers, and the distinction is the whole point.</b> Zero
    /// is a root — a unit at the top of the chart, which is where a Division belongs. Absent is
    /// unplaced: the record exists and is on no structure, which is what an import leaves behind and
    /// what the designer's unplaced panel is for. A screen that showed both as "no parent" would be
    /// telling a reader their Division was fine when it was invisible.
    /// </remarks>
    Task<IReadOnlyDictionary<string, int>> GetDepthsAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, IReadOnlyList<DimensionNodeRef>>> GetAncestorPathsAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
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

    /// <summary>Removes a node's links and closure rows on every structure. Half of a deletion.</summary>
    /// <remarks>
    /// The other half is the record itself, and a caller that does only this leaves the closure
    /// legitimately inconsistent: a record whose type is on a structure's levels is expected to have
    /// a self pair there whether or not it is placed, so <see cref="VerifyAsync"/> will report one
    /// missing until the content item is gone too.
    ///
    /// It has no caller today. It had one — <c>IDimensionService.AddUnitAsync</c>, undoing a record
    /// whose placement had been refused after it was created — and that is gone, because the
    /// placement is now judged before anything is written. It is kept because it is the only
    /// sanctioned way to take those rows out and the first real "delete a unit" feature will need
    /// it, and it is covered by <c>DimensionRemovalTenantTests</c> on a tenant with more than one
    /// structure, which is the shape it used to crash on.
    /// </remarks>
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
