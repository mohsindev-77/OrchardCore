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
