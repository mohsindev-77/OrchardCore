namespace WorkMate.Dimensions.Internal.References;

/// <summary>
/// One record's references of one kind — today, a Position's allowed grades (D26 C).
/// </summary>
/// <remarks>
/// <b>One document per (record, kind).</b> That grain is both the unit of maintenance — setting a
/// position's grades rewrites one document — and the unit of concurrency, so two administrators
/// re-grading the same position collide rather than one losing. The same reasoning
/// <c>DimensionLinkDocument</c> gives for its own grain, and the same optimistic-concurrency rule
/// (ADR-0005): every write goes through <c>SaveCheckedAsync</c>.
///
/// <b>Undated, and deliberately so.</b> A reference is a fact about how a post is configured, not
/// about where anything sat on a date. "Which grades did Software Engineer allow last March" is the
/// same kind of question as "what was this branch's cost centre last March", which the Dimensions
/// README already records as not answerable: an attribute, and now a reference, has no effective
/// range of its own. The <em>targets</em> are dated records, so a reader asking for a position's
/// allowed grades as at a date gets the ones effective then — the dating is in the grade, not in
/// the pointer. D26 G's "removing a grade warns and never breaks that employee's record" relies on
/// exactly that: the employee's own dated grade assignment is untouched by a change here.
///
/// Internal, in an internal namespace, for the reason the link and closure documents are: nothing
/// outside this module reads these tables, and keeping the type unreachable makes that a compiler
/// error rather than a convention.
/// </remarks>
internal sealed class DimensionReferenceDocument
{
    /// <summary>YesSql's document id. Assigned on first save; never meaningful to the business.</summary>
    public long Id { get; set; }

    /// <summary>YesSql's optimistic concurrency token, per ADR-0005.</summary>
    public long Version { get; set; }

    /// <summary>The record doing the referring — a Position, for the only kind that ships.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>Which kind of reference this is. One of <c>DimensionReferenceKinds</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// What it points at, in the order a screen should list them. May be empty: a position whose
    /// grades have all been removed is a legitimate state and not the absence of a document.
    /// </summary>
    /// <remarks>
    /// Order is stored rather than derived, because the one thing a reader scanning SE-1, SE-2, SE-3
    /// must not meet is a different order each time the page is drawn. It is the order the picker
    /// posted, which is the order somebody chose.
    /// </remarks>
    public IReadOnlyList<string> TargetRecordIds { get; set; } = [];
}
