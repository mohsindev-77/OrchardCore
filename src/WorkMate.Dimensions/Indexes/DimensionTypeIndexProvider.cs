using WorkMate.Dimensions.Models;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Indexes;

/// <summary>Maps each dimension type document onto its one index row.</summary>
public sealed class DimensionTypeIndexProvider : IndexProvider<DimensionTypeDocument>
{
    public override void Describe(DescribeContext<DimensionTypeDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<DimensionTypeIndex>()
            .Map(document => new DimensionTypeIndex
            {
                DimensionTypeId = document.DimensionTypeId,
                Code = document.Code,
                ContentTypeName = document.ContentTypeName,
                IsSystemDefined = document.IsSystemDefined,
                AllowsSelfNesting = document.AllowsSelfNesting,
                RetiredOn = EffectiveDates.ToColumn(document.RetiredOn),
            });
    }
}
