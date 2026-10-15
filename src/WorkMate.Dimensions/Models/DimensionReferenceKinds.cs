namespace WorkMate.Dimensions.Models;

/// <summary>
/// The kinds of reference one dimension record may hold to another. Decision D21, as amended by
/// D26 C.
/// </summary>
/// <remarks>
/// <b>A reference is not a parent, and that is the whole distinction this exists to keep.</b> A
/// parent is a dated link scoped to a structure: it enters the closure index, it is what a headcount
/// rolls up through, and a record has exactly one of them per axis (D19, ADR-0005). A reference is
/// none of those things. It never reaches the closure index, nothing rolls up through it, no tree
/// draws it, and a record may hold as many as it likes.
///
/// <b>Why it cannot be a second axis, which is the first thing a reader will suggest.</b> A link
/// gives a record one parent per structure, in one direction or the other. Put the grade under the
/// position and SE-1 may be allowed by exactly one position; put the position under the grade and a
/// position may allow exactly one grade. Neither is what D26 C asks for — <em>Software Engineer
/// allows SE-1, SE-2 and SE-3, and so does nothing else about SE-1</em> — so this is a many-to-many
/// fact, and it is the one thing the graph genuinely cannot carry. ADR-0028 records that as the
/// reason the reference table exists at all.
///
/// <b>The values are persisted</b>, in <c>DimensionReferenceDocument.Kind</c> and in its index row,
/// so renaming one is a data migration and not a refactor — the same contract
/// <see cref="DimensionAttributeKind"/>'s member names carry.
/// </remarks>
public static class DimensionReferenceKinds
{
    /// <summary>
    /// The grades a Position allows: its levels, from which an employee holding it takes one.
    /// </summary>
    /// <remarks>
    /// D26 C. A set rather than the range D21 originally proposed, because a range needs grades to
    /// be ordered and cannot say "SE-1 and SE-3 but not SE-2", which a set says without needing an
    /// order at all.
    /// </remarks>
    public const string AllowedGrade = "allowed-grade";

    /// <summary>Every kind this product ships, for the validator and for the tests.</summary>
    public static readonly IReadOnlyList<string> All = [AllowedGrade];
}
