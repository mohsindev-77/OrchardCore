using WorkMate.Core;

namespace WorkMate.Dimensions.Internal.Graph;

/// <summary>
/// A node's dated parent history on one structure.
/// </summary>
/// <remarks>
/// One document per structure and record, per ADR-0005: that grain is both the unit of
/// maintenance — a move rewrites one document per node in the subtree — and the unit of
/// concurrency, so two administrators reparenting the same node collide rather than one losing.
///
/// Internal, and in an internal namespace, because the specification is explicit that nothing
/// outside this module reads the link, closure or assignment tables. The services are the only
/// entry, which is what makes the storage replaceable and the tenant boundary testable in one
/// place.
/// </remarks>
internal sealed class DimensionLinkDocument
{
    public long Id { get; set; }

    /// <summary>YesSql's optimistic concurrency token, per ADR-0005.</summary>
    public long Version { get; set; }

    public string StructureId { get; set; } = string.Empty;

    /// <summary>The child: the record whose parentage this document records.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// The parents over time, earliest first. May be empty: a root has no parent, and so does a
    /// record created outside the designer and not yet placed.
    /// </summary>
    public IReadOnlyList<ParentLink> Parents { get; set; } = [];

    /// <summary>
    /// The last day this record was part of this axis at all, or null while it still is.
    /// </summary>
    /// <remarks>
    /// Set when a structure loses a level: the records of that dimension type stop being on the
    /// axis from that date, but everything about them before it must keep resolving. Capping
    /// here rather than deleting is what makes "who was under this node last March" still
    /// answerable after the level has gone.
    /// </remarks>
    public DateOnly? OnAxisUntil { get; set; }

    /// <summary>
    /// The parent whose retirement left this record with nowhere to sit, and the date it happened,
    /// when somebody chose deliberately to leave it that way.
    /// </summary>
    /// <remarks>
    /// A record with no parent on a date looks the same from the closure whether it was never
    /// placed or whether its parent closed underneath it, and those are not the same thing to a
    /// person reading the unplaced panel: one is a record that arrived from an import and still
    /// needs a home, the other is a unit that had one until last Tuesday. This is the only way to
    /// tell them apart after the fact, because the link itself is not what changed — the parent's
    /// own effective range is.
    ///
    /// Two plain fields rather than an index: nothing queries by them, the unplaced panel reads
    /// them for the handful of records it is already loading, and adding a column to
    /// <c>DimensionLinkIndex</c> would need a migration for something no query filters on.
    ///
    /// Cleared the moment the record is placed or moved again. Not cleared when the record itself
    /// retires, because a retired record is not on the unplaced panel at all — it fails the "is it
    /// effective on this date" test long before anything asks why it has no parent.
    /// </remarks>
    public DateOnly? OrphanedByParentRetirementOn { get; set; }

    /// <inheritdoc cref="OrphanedByParentRetirementOn"/>
    public string? OrphanedFromParentId { get; set; }

    /// <summary>
    /// The parent in effect on <paramref name="asAt"/>, or null if the node has none then.
    /// </summary>
    /// <remarks>
    /// Null covers three cases that are all "no parent" to a caller and are not the same to a
    /// reader: off the axis entirely, never placed, and placed-then-taken-off. The third now has
    /// an entry of its own with a null parent, which this reads exactly like the absence of one —
    /// callers asking "who is above this today" need no new case, and the ones that care about the
    /// difference ask <see cref="Parents"/> directly.
    /// </remarks>
    public string? ParentOn(DateOnly asAt) =>
        OnAxisUntil is not null && asAt > OnAxisUntil
            ? null
            : Parents.FirstOrDefault(link => link.Range.Contains(asAt))?.ParentRecordId;

    /// <summary>
    /// The dated entry covering <paramref name="asAt"/>, whether or not it names a parent.
    /// </summary>
    public ParentLink? EntryOn(DateOnly asAt) =>
        OnAxisUntil is not null && asAt > OnAxisUntil
            ? null
            : Parents.FirstOrDefault(link => link.Range.Contains(asAt));
}

/// <summary>
/// One period of a record's parentage on one axis. A null <paramref name="ParentRecordId"/> is a
/// period with no parent at all, recorded deliberately.
/// </summary>
/// <remarks>
/// Nullable since stage D2. Moving a unit to the top of a structure used to be recorded by
/// <em>removing</em> the link that covered that date, which left no trace of the decision: the
/// history before it still resolved correctly, but "when did this leave the tree, and under what"
/// was answerable only from the audit trail. That cost three things — cancel-move could not see a
/// move it had no entry for, the unplaced panel could not tell a unit taken off the tree from one
/// never placed, and nothing on the record said a decision had been made at all.
///
/// So leaving the tree is an entry like any other move, and the only thing that distinguishes it
/// is where it points. Readers must treat a null parent as "no ancestors from here": the closure
/// skips these rows rather than trying to resolve them.
/// </remarks>
internal sealed record ParentLink(string? ParentRecordId, EffectiveRange Range)
{
    /// <summary>Whether this period places the record somewhere, as opposed to nowhere.</summary>
    public bool IsPlacement => !string.IsNullOrEmpty(ParentRecordId);
}

/// <summary>
/// A node's ancestor chain on one structure, dated.
/// </summary>
/// <remarks>
/// Derived from the links and maintained on write, never computed on read — decision 5 of the
/// architecture. The grain is one document per structure and descendant, so recomputing a node
/// rewrites exactly one document and YesSql regenerates exactly that node's index rows. An
/// index row therefore cannot drift from its own document, which narrows the verification
/// command's job to comparing closure against links.
///
/// ADR-0005 departs from the architecture's four-column closure here: a row carries an
/// effective range as well as a depth, because an undated closure describes only today and
/// every historical descendant query would otherwise have to walk the link table instead.
/// </remarks>
internal sealed class DimensionClosureDocument
{
    public long Id { get; set; }

    public long Version { get; set; }

    public string StructureId { get; set; } = string.Empty;

    /// <summary>The node these ancestors belong to.</summary>
    public string DescendantId { get; set; } = string.Empty;

    /// <summary>
    /// Every ancestor-descendant pair for this node, including the self pair at depth zero. One
    /// ancestor can appear more than once with different depths or ranges, because the path to
    /// it can change over time.
    /// </summary>
    public IReadOnlyList<ClosureAncestor> Ancestors { get; set; } = [];

    /// <summary>The ancestors in effect on <paramref name="asAt"/>, nearest first.</summary>
    public IEnumerable<ClosureAncestor> AncestorsOn(DateOnly asAt) =>
        Ancestors.Where(ancestor => ancestor.Range.Contains(asAt)).OrderBy(ancestor => ancestor.Depth);
}

/// <summary>
/// One ancestor of one node, with the distance between them and the period the relationship
/// held for.
/// </summary>
/// <param name="Depth">Zero for the node itself, one for its parent, and so on.</param>
/// <param name="Range">
/// The intersection of the effective ranges of every link along the path. A section only sits
/// under a division for the period that both the section's link to its department and the
/// department's link to the division were effective.
/// </param>
internal sealed record ClosureAncestor(string AncestorId, int Depth, EffectiveRange Range);

/// <summary>
/// An employee's dated placements on one structure.
/// </summary>
/// <remarks>
/// The grain is deliberate, per ADR-0005: every rule about assignments in architecture section 6
/// — ranges must not overlap, allocations must total 100 percent on a date, exactly one must be
/// primary — is a rule about one employee on one structure, so all three are decidable inside a
/// single document without reading anything else.
/// </remarks>
internal sealed class EmployeeAssignmentDocument
{
    public long Id { get; set; }

    public long Version { get; set; }

    public string EmployeeId { get; set; } = string.Empty;

    public string StructureId { get; set; } = string.Empty;

    /// <summary>The placements over time. More than one may be effective at once: that is a split allocation.</summary>
    public IReadOnlyList<AssignmentRow> Rows { get; set; } = [];

    /// <summary>Every placement effective on <paramref name="asAt"/>, primary first.</summary>
    public IEnumerable<AssignmentRow> RowsOn(DateOnly asAt) =>
        Rows.Where(row => row.Range.Contains(asAt)).OrderByDescending(row => row.IsPrimary);

    /// <summary>
    /// The one placement that resolves an employee to a single node on <paramref name="asAt"/>:
    /// the primary one. Null when the employee is not placed on this structure then.
    /// </summary>
    public AssignmentRow? PrimaryOn(DateOnly asAt) =>
        Rows.FirstOrDefault(row => row.IsPrimary && row.Range.Contains(asAt));
}

/// <summary>
/// One placement of an employee at a node.
/// </summary>
/// <param name="AllocationPercent">
/// How much of the employee this placement accounts for. Split cost centres are the reason this
/// exists; the allocations effective on any one date total 100.
/// </param>
/// <param name="IsPrimary">
/// Whether this is the placement that answers "where does this person work". Exactly one of an
/// employee's concurrent placements on a structure is primary, so matrix cases stay unambiguous.
/// </param>
internal sealed record AssignmentRow(
    string RecordId,
    EffectiveRange Range,
    decimal AllocationPercent,
    bool IsPrimary);

/// <summary>
/// Who has led one unit on one axis, over time.
/// </summary>
/// <remarks>
/// <b>A record of its own, not a flag on an assignment row.</b> ADR-0012 is the decision and the
/// argument; the short form is that a head need not be a member of the unit they head. Architecture
/// section 8's seed data requires "a unit head who is not a member of the unit", and an acting head
/// borrowed from another department is ordinary rather than exotic. Expressing that as an
/// assignment would mean inventing an allocation for somebody who has none there — and
/// <c>ValidateAssignmentAsync</c> refuses an allocation of zero — so the only way to carry it on an
/// assignment row is to fake a split, which would then be counted by headcount and charged by cost.
/// A head appointment is counted by neither, because it is neither.
///
/// The grain is one document per structure and record, for the reason ADR-0005 gives for the other
/// three: the rule that matters — at most one head per unit per date — is a rule about one unit on
/// one axis, so it is decidable inside a single document without reading anything else.
/// </remarks>
internal sealed class HeadAppointmentDocument
{
    public long Id { get; set; }

    /// <summary>YesSql's optimistic concurrency token, per ADR-0005.</summary>
    public long Version { get; set; }

    public string StructureId { get; set; } = string.Empty;

    /// <summary>The unit being led.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>The terms over time, earliest first. May be empty: a unit's post can be vacant.</summary>
    public IReadOnlyList<HeadTerm> Terms { get; set; } = [];

    /// <summary>Who led the unit on <paramref name="asAt"/>, or null when the post was vacant.</summary>
    public HeadTerm? TermOn(DateOnly asAt) => Terms.FirstOrDefault(term => term.Range.Contains(asAt));
}

/// <summary>One period during which one employee led one unit on one axis.</summary>
/// <remarks>
/// Dated like everything else in this engine, so "who led this unit last March" is answerable from
/// the live data. Prompt 5's approval routing reads the same terms the designer's card does, which
/// is what stops what the chart shows and what an approval routes to from ever disagreeing.
/// </remarks>
internal sealed record HeadTerm(string EmployeeId, EffectiveRange Range);
