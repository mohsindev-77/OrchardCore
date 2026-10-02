using OrchardCore.ContentManagement;
using WorkMate.Dimensions.Models;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Indexes;

/// <summary>
/// The queryable shape of a dimension record: code lookup, type filtering and dated resolution
/// without loading the content item.
/// </summary>
/// <remarks>
/// Specification section 7 is the reason this matters: "a structure with a thousand records
/// under one node must not load a thousand content items to answer a count". Everything the
/// graph layer needs to answer a question about a record, short of showing it to someone, is on
/// this row.
///
/// The dates are <c>DateTime</c> columns, and <see cref="EffectiveDates"/> is the only code that
/// converts them. See ADR-0005.
/// </remarks>
public sealed class DimensionRecordPartIndex : MapIndex
{
    /// <summary>The content item this row describes. The identity the graph tables refer to.</summary>
    public string ContentItemId { get; set; } = string.Empty;

    /// <summary>The generated content type, so a query can stay within one kind of unit cheaply.</summary>
    public string ContentType { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string DimensionTypeId { get; set; } = string.Empty;

    /// <summary>Indexed so a picker can sort and filter by name without loading the items.</summary>
    public string NameEn { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    /// <summary>A midnight <c>DateTime</c>; see the remark on the class.</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// The inclusive end, carrying <see cref="EffectiveDates.OpenEnded"/> rather than null for an
    /// open-ended record, so the dated query is a plain two-column range scan.
    /// </summary>
    public DateTime EffectiveToInclusive { get; set; }

    /// <summary>
    /// Whether this row describes the latest version of the item.
    /// </summary>
    /// <remarks>
    /// Generated dimension types are not draftable, so latest and published are the same thing
    /// today. The column is here because the index provider maps every version Orchard hands it,
    /// and a query that forgot to filter would see a retired item's superseded rows as live.
    /// </remarks>
    public bool Latest { get; set; }

    public bool Published { get; set; }
}

/// <summary>
/// Maps every content item carrying <see cref="DimensionRecordPart"/> onto one index row, and
/// everything else onto none.
/// </summary>
public sealed class DimensionRecordPartIndexProvider : IndexProvider<ContentItem>
{
    public override void Describe(DescribeContext<ContentItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<DimensionRecordPartIndex>()
            // Every content item on the tenant passes through here, including those of modules
            // that know nothing about this one. Filtering first keeps the map total, so it never
            // has to return a null row.
            //
            // TryGet, not As: in 3.0.1 As<TPart> is obsolete in favour of TryGet, GetOrCreate
            // and Get. GetOrCreate would be wrong here — it welds an empty part onto every
            // content item on the tenant just to ask whether it has one.
            .When(item => item.TryGet<DimensionRecordPart>(out _))
            .Map(item =>
            {
                var part = item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!;

                return new DimensionRecordPartIndex
                {
                    ContentItemId = item.ContentItemId,
                    ContentType = item.ContentType,
                    Code = part.Code,
                    DimensionTypeId = part.DimensionTypeId,
                    NameEn = part.NameEn,
                    IsActive = part.IsActive,
                    SortOrder = part.SortOrder,
                    EffectiveFrom = EffectiveDates.ToColumn(part.EffectiveFrom),
                    EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(part.EffectiveTo),
                    Latest = item.Latest,
                    Published = item.Published,
                };
            });
    }
}
