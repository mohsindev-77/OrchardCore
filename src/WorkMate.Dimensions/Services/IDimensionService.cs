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
    /// <param name="sortOrder">
    /// Where this record sorts among its siblings. Omit it and the record is placed after every
    /// record already in the tenant, so records come out in the order they were created — which
    /// for an import is the order the recipe lists them in.
    /// </param>
    /// <param name="aliasName">
    /// The short label the dimension's list and tree show beside the name (FRS v2.5 §4.1). Optional
    /// in both halves, and undated — see <see cref="SetAliasAsync"/>.
    ///
    /// Called <c>aliasName</c> rather than <c>alias</c> because <c>Alias</c> is a reserved word in
    /// Visual Basic, and CA1716 refuses it on an interface member.
    /// </param>
    Task<DimensionResult<DimensionNodeRef>> CreateAsync(
        string dimensionTypeId,
        string code,
        BilingualText name,
        EffectiveRange effectiveRange,
        DimensionValidationBatch? batch = null,
        int? sortOrder = null,
        BilingualText? aliasName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a unit to a structure: creates the record and puts it in its place, as one operation.
    /// </summary>
    /// <param name="parentRecordId">The unit it sits under, or null for a root of the structure.</param>
    /// <param name="attributes">
    /// Values for the custom attributes the dimension type declares. Attributes the type does not
    /// declare are rejected; attributes it declares and requires must be present.
    /// </param>
    /// <param name="sortOrder">As on <see cref="CreateAsync"/>: omit it and the new unit sorts
    /// after the siblings that were already there.</param>
    /// <remarks>
    /// One method rather than <see cref="CreateAsync"/> followed by <see cref="MoveAsync"/>,
    /// for two reasons.
    ///
    /// The first is that the caller should not have to sequence them. A record created and then
    /// left unplaced because the second call failed is exactly the half-finished state the
    /// unplaced panel exists to reveal, and asking every caller to handle it correctly is asking
    /// for it to be handled correctly nowhere. Both the record and the placement are validated
    /// before either is written — the placement through
    /// <see cref="IDimensionValidator.ValidateNewPlacementAsync"/>, which exists so that this
    /// method can ask about a unit it has not created yet. It used to create first and ask
    /// afterwards, and delete the record when the answer was no; a refused placement now writes
    /// nothing and there is nothing to undo.
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
        int? sortOrder = null,
        BilingualText? aliasName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the record's alias — the short label its list and tree show beside the name.
    /// </summary>
    /// <remarks>
    /// <b>No effective date, and that is the point.</b> Every other write on this interface takes
    /// one, because the thing it changes is dated: a name has a history, a placement has a history,
    /// existence itself has a range. An alias has none — nothing resolves it as at a date and no
    /// report groups by it — so asking for a date would be asking the caller to supply an answer
    /// nothing will ever read, and the first screen to default it would have invented a dated fact
    /// nobody stated. <c>DimensionRecordPart.AliasEn</c> sets out the full argument.
    ///
    /// <b>Its own method rather than a parameter on a general update.</b>
    /// <see cref="UpdateAsync"/> states every unmanaged field at once — cost centre, GL account,
    /// sort order, the active flag and the retired <c>HeadEmployeeId</c> — so a screen that wanted
    /// to change only the alias would have to read the rest and post them back unchanged. A form
    /// that round-trips fields it does not show is a form that silently rewrites whatever changed
    /// underneath it between the read and the write, and <c>HeadEmployeeId</c> is exactly the field
    /// that must not be rewritten by anything (ADR-0012 retired it without deleting it).
    ///
    /// Gated by <see cref="Permissions.ManageDimensionRecords"/>: an alias is ordinary record
    /// upkeep, not a reorganisation, and it changes nothing any historical report resolves.
    /// </remarks>
    Task<DimensionResult<DimensionNodeRef>> SetAliasAsync(
        string recordId,
        BilingualText aliasName,
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
    /// The custom attribute values this record carries, one per populated field of its type's
    /// schema.
    /// </summary>
    /// <remarks>
    /// Undated, because the values are not dated. A record's attributes are fields on its own
    /// content part with no effective range of their own — unlike its name, its placement and its
    /// own existence, all three of which are. That is a deliberate limit of the engine as it
    /// stands, not an oversight of this method: if a customer ever needs "what was this branch's
    /// cost centre last March" the attribute would have to become a dated thing in its own right,
    /// which is a model change rather than a reader change.
    ///
    /// Empty entries are omitted. An attribute nobody filled in is absent, not present-and-blank,
    /// which is what lets an export and a re-run comparison treat "not set" the same way the
    /// editor does.
    /// </remarks>
    Task<IReadOnlyList<DimensionAttributeValue>> GetAttributeValuesAsync(
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, for several records at once, keyed by record id.
    /// </summary>
    /// <remarks>
    /// <b>One content-item read for the whole set</b>, through the pinned version's
    /// <c>IContentManager.GetAsync(IEnumerable&lt;string&gt;, VersionOptions)</c> — verified against
    /// Orchard Core 3.0.1 with <c>tools/api-probe</c>. Its caller is the Positions list asking every
    /// row's approved headcount, and a call per row is a content item loaded per row to draw one
    /// column.
    ///
    /// A record with no values of its own is absent rather than present with an empty list, the
    /// same contract <see cref="GetReferenceSetsAsync"/> states.
    /// </remarks>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DimensionAttributeValue>>> GetAttributeValueSetsAsync(
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes custom attribute values onto an existing record, validated against its type's schema.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CreateAsync"/> because the paths that need it already create the
    /// record first: a recipe import creates a record, places it, and then has values to write;
    /// the designer's Add unit screen does all three in one call through
    /// <c>AddUnitAsync</c>. One writer behind both, so a value written by a recipe and a value
    /// written by a form go through the same conversion and the same validation.
    /// </remarks>
    Task<DimensionResult<DimensionNodeRef>> SetAttributeValuesAsync(
        string recordId,
        IReadOnlyList<DimensionAttributeValue> attributes,
        CancellationToken cancellationToken = default);

    // ---- references (D21, D26 C) --------------------------------------------------------
    //
    // A reference is not a parent. It never enters the closure index, nothing rolls up through it,
    // no tree draws it, and a record may hold as many as it likes — which is the one shape a link
    // cannot carry, because a link gives a record one parent per axis in either direction.
    // DimensionReferenceKinds sets out the argument in full; ADR-0028 records it as the reason the
    // reference table exists.
    //
    // On this interface rather than on one of its own because a record's references are a fact
    // about the record, and this is the record aggregate's root (specification rule 9). The
    // position-shaped reads built on top of them live on IPositionService.

    /// <summary>
    /// What <paramref name="recordId"/> points at, of one kind, in the order somebody chose.
    /// </summary>
    /// <param name="asAt">
    /// The date the <em>targets</em> are judged on, defaulting to today. The reference itself is
    /// undated; a target that is not effective then is left out, which is what makes "the grades
    /// this position allows today" exclude one retired last year without the reference having to
    /// have been removed.
    /// </param>
    Task<IReadOnlyList<DimensionNodeRef>> GetReferencesAsync(
        string recordId,
        string referenceKind,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, for several records at once, keyed by record id.
    /// </summary>
    /// <remarks>
    /// Batched for the reason every read of its shape in this module is: the caller is a page of
    /// positions drawing the allowed grades on each row, and a call per row would be fifty queries
    /// to fill one column. A record with no references of this kind is absent from the result
    /// rather than present with an empty list.
    /// </remarks>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DimensionNodeRef>>> GetReferenceSetsAsync(
        IReadOnlyList<string> recordIds,
        string referenceKind,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The raw stored target ids, in order, undated. What a record <em>holds</em> rather than what
    /// resolves today.
    /// </summary>
    /// <remarks>
    /// <b>The one undated reference read, and it exists for the comparison a re-run makes.</b>
    /// <see cref="GetReferencesAsync"/> filters out a target that is not effective on the date,
    /// which is right for every screen and wrong for ADR-0008: a recipe row stating three grades,
    /// one of them retired last year, would read the tenant's set as two and report a difference
    /// nobody made. An export needs the same answer for the same reason.
    ///
    /// Not for a screen. A screen asking "what grades does this position offer" must have the dated
    /// read, or it will offer a grade that has closed.
    /// </remarks>
    Task<IReadOnlyList<string>> GetReferenceTargetIdsAsync(
        string recordId,
        string referenceKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The records that point at <paramref name="targetRecordId"/>, of one kind — the other end of
    /// the same rows.
    /// </summary>
    /// <remarks>
    /// This is decision D26 C's <em>"Used by positions: …"</em> on a grade's value page, and it is
    /// the reason references are a table rather than a list on the referring record's own document.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> GetReferrersAsync(
        string targetRecordId,
        string referenceKind,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// States the whole set of references of one kind for a record: what is not named is removed.
    /// </summary>
    /// <remarks>
    /// <b>States rather than adds</b>, for the reason <c>ReallocateAsync</c> states a whole
    /// placement picture: a multi-select posts the set it ended up with, and an interface that took
    /// an add and a remove would make the screen work out the difference and be the only thing that
    /// knew whether it had got it right.
    ///
    /// <b>No effective date</b>, because a reference has none — see
    /// <c>DimensionReferenceKinds</c> and <see cref="SetAliasAsync"/>, which refuses a date for the
    /// same reason. Taking one would ask a caller for an answer nothing will ever read.
    ///
    /// <b>It never reaches an employee's record.</b> Removing a grade a position's holder is on is
    /// permitted and leaves that employee's own dated grade exactly as it is (D26 G); the screen
    /// warns, and this does not refuse.
    ///
    /// Validated: every target must exist and be of a type this kind permits. Gated by
    /// <see cref="Permissions.ManageDimensionRecords"/> — configuring a post is ordinary record
    /// upkeep, not a reorganisation, and it changes nothing any historical report resolves.
    /// </remarks>
    /// <returns>How many references the record now holds.</returns>
    Task<DimensionResult<int>> SetReferencesAsync(
        string recordId,
        string referenceKind,
        IReadOnlyList<string> targetRecordIds,
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
