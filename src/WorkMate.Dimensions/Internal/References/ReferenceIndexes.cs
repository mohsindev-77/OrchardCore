using YesSql.Indexes;

namespace WorkMate.Dimensions.Internal.References;

/// <summary>One record pointing at another, of one kind. A Position's allowed grade.</summary>
/// <remarks>
/// <b>One row per reference, not per document</b>, because the table has to be read from both ends
/// and the two readers are different screens. The Position's own page asks *"what does this record
/// point at"*; the Grade's page asks *"which positions use this grade"* (D26 C's **Used by
/// positions**), and that second question is a scan of every position unless the target is indexed.
///
/// Internal by design, like every other index in this module.
/// </remarks>
internal sealed class DimensionReferenceIndex : MapIndex
{
    /// <summary>The referring record — the Position.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>Which kind of reference. One of <c>DimensionReferenceKinds</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The referenced record — the Grade.</summary>
    public string TargetRecordId { get; set; } = string.Empty;

    /// <summary>Where this one sits in the referring record's own order. Contiguous from zero.</summary>
    public int SortOrder { get; set; }
}

internal sealed class DimensionReferenceIndexProvider : IndexProvider<DimensionReferenceDocument>
{
    public override void Describe(DescribeContext<DimensionReferenceDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<DimensionReferenceIndex>()
            .Map(document => document.TargetRecordIds.Select((target, ordinal) => new DimensionReferenceIndex
            {
                RecordId = document.RecordId,
                Kind = document.Kind,
                TargetRecordId = target,
                SortOrder = ordinal,
            }));
    }
}
