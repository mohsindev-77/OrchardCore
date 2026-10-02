using WorkMate.Dimensions.Models;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Indexes;

/// <summary>
/// One row per name a record has had, so "what was this called in March" is an indexed read
/// rather than a document load and a scan.
/// </summary>
/// <remarks>
/// A report rendering a prior period asks this for every node it shows. Loading the name
/// document per node to find one string would make a historical report load the whole history
/// of the organisation to print a page of it.
/// </remarks>
public sealed class DimensionNameIndex : MapIndex
{
    public string RecordId { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>A midnight <c>DateTime</c>; <see cref="EffectiveDates"/> converts. See ADR-0005.</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>The inclusive end, carrying <see cref="EffectiveDates.OpenEnded"/> when open.</summary>
    public DateTime EffectiveToInclusive { get; set; }
}

/// <summary>Maps a record's name history onto one row per period.</summary>
public sealed class DimensionNameIndexProvider : IndexProvider<DimensionNameDocument>
{
    public override void Describe(DescribeContext<DimensionNameDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<DimensionNameIndex>()
            .Map(document => document.Periods.Select(period => new DimensionNameIndex
            {
                RecordId = document.RecordId,
                NameEn = period.Name.En,
                NameAr = period.Name.Ar,
                EffectiveFrom = EffectiveDates.ToColumn(period.Range.From),
                EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(period.Range.To),
            }));
    }
}
