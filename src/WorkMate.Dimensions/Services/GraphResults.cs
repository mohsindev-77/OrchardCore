using WorkMate.Core;
using WorkMate.Dimensions.Indexes;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// A record as the graph services hand it out: enough to show it, scope by it or link to it,
/// without loading the content item.
/// </summary>
/// <remarks>
/// Specification section 7 is the reason this exists rather than returning content items: "a
/// structure with a thousand records under one node must not load a thousand content items to
/// answer a count". Everything here comes from <c>DimensionRecordPartIndex</c>.
/// </remarks>
/// <param name="Depth">
/// Distance from whatever the query was anchored on: zero for the node itself, one for a parent
/// or a child. Zero on a result that has no natural distance, such as a lookup by code.
/// </param>
public sealed record DimensionNodeRef(
    string RecordId,
    string Code,
    string NameEn,
    string NameAr,
    string DimensionTypeId,
    EffectiveRange EffectiveRange,
    bool IsActive,
    int SortOrder,
    int Depth = 0)
{
    /// <summary>The name to show in <paramref name="culture"/>, falling back rather than to nothing.</summary>
    public string ForCulture(string? culture)
    {
        var wantsArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true;
        var preferred = wantsArabic ? NameAr : NameEn;

        return string.IsNullOrWhiteSpace(preferred) ? (wantsArabic ? NameEn : NameAr) : preferred;
    }

    /// <summary>
    /// Builds one from its index row. The one place this mapping is written, so the graph
    /// service's batch hydration and the record lookup's single-row reads cannot drift apart.
    /// </summary>
    public static DimensionNodeRef FromIndex(DimensionRecordPartIndex row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new DimensionNodeRef(
            row.ContentItemId,
            row.Code,
            row.NameEn,
            row.NameAr,
            row.DimensionTypeId,
            new EffectiveRange(
                EffectiveDates.FromColumn(row.EffectiveFrom),
                EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive)),
            row.IsActive,
            row.SortOrder);
    }
}

/// <summary>An employee's placement at a node, as the assignment service hands it out.</summary>
public sealed record EmployeeAssignment(
    string EmployeeId,
    string StructureId,
    string RecordId,
    EffectiveRange Range,
    decimal AllocationPercent,
    bool IsPrimary);

/// <summary>
/// One disagreement between the closure index and the links it is supposed to be derived from.
/// </summary>
/// <remarks>
/// Architecture section 4: "An index that has silently drifted from the links is the worst
/// failure mode in this design, because every downstream number stays plausible while being
/// wrong." A divergence therefore names the date it was found on, so it can be reproduced.
/// </remarks>
/// <param name="Kind">Whether the closure has a pair the links do not, or the other way round.</param>
public sealed record ClosureDivergence(
    DateOnly AsAt,
    string AncestorId,
    string DescendantId,
    ClosureDivergenceKind Kind,
    int? ClosureDepth = null,
    int? LinkDepth = null);

/// <summary>What kind of disagreement a <see cref="ClosureDivergence"/> records.</summary>
public enum ClosureDivergenceKind
{
    /// <summary>The closure claims a pair the links do not produce on that date.</summary>
    InClosureButNotInLinks,

    /// <summary>The links produce a pair the closure is missing on that date.</summary>
    InLinksButNotInClosure,

    /// <summary>Both have the pair, at different depths.</summary>
    DepthDiffers,
}

/// <summary>
/// What a verification run found. Changes nothing: the rebuild is a separate, explicit command.
/// </summary>
public sealed record ClosureVerificationReport(
    string StructureId,
    IReadOnlyList<DateOnly> DatesChecked,
    IReadOnlyList<ClosureDivergence> Divergences)
{
    /// <summary>True when the index and the links agree on every date checked.</summary>
    public bool IsConsistent => Divergences.Count == 0;
}

/// <summary>
/// What a merge would do, or what it did.
/// </summary>
/// <remarks>
/// Architecture section 6 requires a merge to run "in dry-run first and produce a report of
/// exactly what will change". The same type carries both the plan and the outcome so that the
/// two can be compared directly rather than by eye — and
/// <c>DimensionMergeTests.TheAppliedResultMatchesTheDryRunExactly</c> does compare them.
/// </remarks>
public sealed record MergePlan(
    string StructureId,
    string SourceRecordId,
    string TargetRecordId,
    DateOnly EffectiveFrom,
    IReadOnlyList<string> ChildrenReparented,
    IReadOnlyList<string> EmployeesReassigned,
    bool SourceRetired)
{
    /// <summary>
    /// Whether the plan would change anything at all. A merge of an empty unit is legitimate —
    /// it still retires the source — so this is informational, not a refusal.
    /// </summary>
    public bool IsEmpty => ChildrenReparented.Count == 0 && EmployeesReassigned.Count == 0;
}

/// <summary>
/// What cancelling a move would restore: the low-level link-document fact, before anything is
/// written and before the record aggregate's richer <see cref="CancelMovePlan"/> is built around
/// it.
/// </summary>
/// <param name="CancelledParentId">The parent the move being cancelled had placed the record under.</param>
/// <param name="RestoredParentId">
/// What the record reverts to: the parent of the placement the move displaced, or null when it
/// reverts to being a root of this axis.
/// </param>
/// <param name="RestoredFrom">The date the cancelled move was effective from.</param>
/// <param name="RestoredUntil">How far the restored placement now runs, or null when open-ended.</param>
public sealed record CancelledMoveRestoration(
    string? CancelledParentId,
    string? RestoredParentId,
    DateOnly RestoredFrom,
    DateOnly? RestoredUntil);

/// <summary>
/// What cancelling a move would do, or what it did.
/// </summary>
/// <remarks>
/// Built on the same dry-run-then-apply shape as <see cref="MergePlan"/>, for the same reason:
/// cancelling a move is never a silent delete, and the report a human signs off and the reason
/// they gave for it must describe exactly what happened, not an approximation of it.
/// </remarks>
/// <summary>
/// What retiring a record would do, and what stops it: the deletion assessment's verdict, the
/// blockers behind it, and what is still hanging off the record on the day it would close.
/// </summary>
/// <param name="Outcome">
/// What <see cref="IDimensionValidator.AssessDeletionAsync"/> says about <em>deleting</em> this
/// record. Retirement is not refused on the strength of it: architecture section 6 makes
/// retirement precisely the answer for a record that cannot be deleted because something refers
/// to it — "the record is closed with an end date, disappears from pickers, and continues to
/// resolve for historical queries". The outcome is here so the screen can say what is still
/// attached, not so it can say no.
/// </param>
/// <param name="Blockers">
/// What refers to the record, each one naming it. Shown as a warning before the user confirms.
/// </param>
/// <param name="DescendantCount">
/// Units still sitting under this one on the retirement date, across the structure being viewed.
/// Not a blocker — a customer may legitimately close a branch from the top — but it is the number
/// that most often means somebody picked the wrong card, so the screen says it before committing.
/// </param>
/// <param name="EmployeesAffected">People still placed at the record on that date.</param>
/// <param name="ChildrenByStructure">
/// Every structure on which this unit is a parent on the day it would close, one entry each, with
/// the children it would strand there. Empty when it is a parent nowhere.
/// </param>
/// <remarks>
/// Retirement closes the record itself, so it takes effect on every structure the record sits on
/// at once — which means the children question has to be asked of all of them, not only of the one
/// somebody happens to be looking at. A unit that is a department on the organisation chart and a
/// cost centre on the finance structure strands children on both when it closes.
/// </remarks>
public sealed record RetirePlan(
    string RecordId,
    DateOnly EffectiveDate,
    DeletionOutcome Outcome,
    IReadOnlyList<DimensionError> Blockers,
    int DescendantCount,
    int EmployeesAffected,
    IReadOnlyList<StructureChildren> ChildrenByStructure)
{
    /// <summary>
    /// Whether something live still refers to the record. Not a refusal — a reason to say so
    /// plainly before the user commits.
    /// </summary>
    public bool HasLiveReferences => Outcome == DeletionOutcome.Blocked;

    /// <summary>Whether anything is still attached, which is what the confirmation warns about.</summary>
    public bool LeavesSomethingBehind => DescendantCount > 0 || EmployeesAffected > 0 || Blockers.Count > 0;

    /// <summary>
    /// Whether retiring this unit would leave units with no parent anywhere, which is the question
    /// the person doing it has to answer before it can go ahead.
    /// </summary>
    public bool HasChildrenToDecide => ChildrenByStructure.Count > 0;

    /// <summary>What this unit would strand on one named structure, or null if nothing.</summary>
    public StructureChildren? On(string structureId) =>
        ChildrenByStructure.FirstOrDefault(entry => entry.StructureId == structureId);
}

/// <summary>
/// What a retirement would strand on one structure, and where it could go instead.
/// </summary>
/// <param name="DirectChildren">
/// The units sitting directly under this one on the day it closes, which is what the person
/// retiring it has to decide about. Deeper descendants are not listed: they keep the parent they
/// have, and move or close with it.
/// </param>
/// <param name="Subtree">
/// Everything under the unit on that date, at every depth, nearest first — what a cascade would
/// close.
/// </param>
/// <param name="ValidMoveTargets">
/// The units the children could be moved to instead, by this structure's own level rules. Excludes
/// the unit being retired and everything already under it, because moving a child into its own
/// branch is either a cycle or a placement that is about to close with the rest of it.
/// </param>
public sealed record StructureChildren(
    string StructureId,
    string StructureCode,
    string StructureNameEn,
    string StructureNameAr,
    IReadOnlyList<DimensionNodeRef> DirectChildren,
    IReadOnlyList<DimensionNodeRef> Subtree,
    IReadOnlyList<DimensionNodeRef> ValidMoveTargets);

/// <summary>What happens to the units under a unit that is being retired.</summary>
public enum ChildrenDispositionKind
{
    /// <summary>They move to another parent on the same date, keeping their own subtrees.</summary>
    MoveToParent,

    /// <summary>They are retired too, with everything under them, on the same date.</summary>
    RetireCascade,

    /// <summary>
    /// They are deliberately left with no parent, and say so until somebody places or retires them.
    /// </summary>
    LeaveUnplaced,
}

/// <summary>
/// The decision about a retiring unit's children, made before the retirement is committed.
/// </summary>
/// <remarks>
/// There is no default and no null-means-this. Leaving children with no parent is a legitimate
/// answer, but it is an answer somebody has to give: the whole defect this type exists for was a
/// retirement quietly choosing it on the user's behalf and leaving units stranded with nothing on
/// screen to say why.
/// </remarks>
public sealed record ChildrenDisposition(ChildrenDispositionKind Kind, string? NewParentRecordId = null)
{
    /// <summary>Move every child under <paramref name="newParentRecordId"/>, or to the top when null.</summary>
    public static ChildrenDisposition MoveTo(string? newParentRecordId) =>
        new(ChildrenDispositionKind.MoveToParent, newParentRecordId);

    public static ChildrenDisposition Cascade { get; } = new(ChildrenDispositionKind.RetireCascade);

    public static ChildrenDisposition Unplaced { get; } = new(ChildrenDispositionKind.LeaveUnplaced);
}

/// <summary>
/// Why a record has no parent: it had one and that one retired. Absent for a record that was
/// simply never placed.
/// </summary>
/// <param name="FormerParentNameEn">
/// The parent's name, carried here rather than looked up by the caller. A retired record does not
/// resolve on any date the caller would naturally ask for — and one retired on the day it opened
/// does not resolve on any date at all — so a caller doing its own lookup ends up showing a
/// content item id to the person reading the badge.
/// </param>
public sealed record OrphanedByParentRetirement(
    string FormerParentId,
    string FormerParentNameEn,
    string FormerParentNameAr,
    DateOnly RetiredOn);

/// <summary>One placement on record: a parent, and the date somebody made it effective from.</summary>
public sealed record RecordedMove(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string ParentRecordId,
    string ParentNameEn,
    string ParentNameAr,
    string ParentCode);

/// <summary>
/// What moving a unit would do, before any of it is done: where it sits now, where it would sit,
/// how much travels with it, and anything the validator objects to.
/// </summary>
/// <param name="CurrentPath">Its ancestors on the effective date as things stand, root first.</param>
/// <param name="NewPath">
/// The ancestors it would have, root first, ending at the new parent. Empty when it would become a
/// root of the structure or leave the tree for the unplaced panel.
/// </param>
/// <param name="DescendantsMoving">
/// How many units travel with it. They keep their place relative to it; what changes is what the
/// whole branch resolves under.
/// </param>
/// <param name="EmployeesAffected">
/// How many people are placed in the branch on that date, and so resolve to a different chain of
/// approvers and cost centres from it.
/// </param>
/// <param name="ClaimedUntil">
/// The last day this move would own, when a later move already on record bounds it — null when it
/// runs open-ended. Per ADR-0005's addendum the engine splits rather than overwrites: a backdated
/// move claims only up to the day before whatever was recorded after it.
/// </param>
/// <param name="SupersededByParentName">The parent the later move goes to, for the warning text.</param>
/// <param name="SupersededByParentNameAr">
/// The same parent's Arabic name. Carried beside the English one because the warning is a sentence
/// and a sentence takes the reader's language throughout, not a name in whichever half was handy.
/// </param>
public sealed record MovePlan(
    string StructureId,
    string RecordId,
    string? NewParentRecordId,
    DateOnly EffectiveFrom,
    IReadOnlyList<DimensionNodeRef> CurrentPath,
    IReadOnlyList<DimensionNodeRef> NewPath,
    int DescendantsMoving,
    int EmployeesAffected,
    IReadOnlyList<DimensionError> Violations,
    DateOnly? ClaimedUntil,
    string? SupersededByParentName,
    string? SupersededByParentNameAr = null)
{
    /// <summary>Whether the move would be refused as things stand.</summary>
    public bool HasViolations => Violations.Any(violation => !violation.IsAdvisory);

    /// <summary>Advisories: reported, never blocking. Architecture section 6.</summary>
    public IEnumerable<DimensionError> Advisories => Violations.Where(violation => violation.IsAdvisory);

    /// <summary>
    /// Whether this move lands inside a period a later move already claimed, and so stops short
    /// rather than running on. The one case ADR-0005 requires the designer to warn about.
    /// </summary>
    public bool SplitsHistory => ClaimedUntil is not null;

    /// <summary>Whether the unit is being taken off the tree rather than reparented.</summary>
    public bool LeavesTheTree => NewParentRecordId is null;
}

public sealed record CancelMovePlan(
    string StructureId,
    string RecordId,
    DateOnly EffectiveFrom,
    string? CancelledParentId,
    string? RestoredParentId,
    DateOnly? RestoredUntil,
    IReadOnlyList<string> EmployeesAffected)
{
    /// <summary>Whether anyone's assignment resolution would change as a result.</summary>
    public bool IsEmpty => EmployeesAffected.Count == 0;
}

/// <summary>
/// What dropping one dimension type from a structure's levels would do to the records already on
/// that level, today.
/// </summary>
/// <remarks>
/// Scoped to the direct impact, not a full transitive walk of every descendant a record under the
/// dropped level might carry: <see cref="RecordCount"/> is every record of this type currently on
/// the axis, and <see cref="EmployeesAffected"/> is every employee assigned today at one of them.
/// Nothing is deleted either way — <c>IDimensionGraphService.OnStructureLevelsChangedAsync</c>
/// closes the affected self pairs rather than removing them, so prior-period reporting is
/// unaffected — but a human approving the change still needs to know how much of today's structure
/// it touches before confirming.
/// </remarks>
public sealed record LevelRemovalImpact(
    string DimensionTypeId,
    int RecordCount,
    int EmployeesAffected);

/// <summary>
/// What changing a structure's levels would do, or what it did: the dry run
/// <c>IStructureService.PlanLevelChangeAsync</c> returns, and what <c>UpdateAsync</c> checked
/// before saving anything.
/// </summary>
/// <remarks>
/// Adding or removing a level is always safe at the engine level — <c>OnStructureLevelsChangedAsync</c>
/// closes rather than deletes — so <see cref="RemovalImpacts"/> is informational, not a blocker.
/// <see cref="Violations"/> is the other half: reordering existing levels can put an
/// <em>already-placed</em> record's real parent on the wrong side of the new order, which is not
/// safe and is not merely informational. A plan with any violation is refused outright, the same
/// as a move or a merge that fails validation — nothing is saved, and the reason names the rule
/// and the record, not a generic failure.
/// </remarks>
public sealed record StructureLevelChangePlan(
    string StructureId,
    IReadOnlyList<string> AddedDimensionTypeIds,
    IReadOnlyList<string> RemovedDimensionTypeIds,
    IReadOnlyList<LevelRemovalImpact> RemovalImpacts,
    IReadOnlyList<DimensionError> Violations)
{
    /// <summary>Whether the levels are actually changing at all.</summary>
    public bool IsEmpty => AddedDimensionTypeIds.Count == 0 && RemovedDimensionTypeIds.Count == 0;

    /// <summary>Whether this change would leave an existing placement invalid and must be refused.</summary>
    public bool HasViolations => Violations.Count > 0;
}
