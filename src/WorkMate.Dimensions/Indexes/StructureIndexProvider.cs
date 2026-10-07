using WorkMate.Dimensions.Models;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Indexes;

/// <summary>Maps each structure document onto its one index row.</summary>
public sealed class StructureIndexProvider : IndexProvider<StructureDocument>
{
    public override void Describe(DescribeContext<StructureDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<StructureIndex>()
            .Map(document => new StructureIndex
            {
                StructureId = document.StructureId,
                Code = document.Code,
                IsPrimaryOrganisation = document.IsPrimaryOrganisation,
                AllowSkipLevel = document.AllowSkipLevel,
                IsStrict = document.IsStrict,
                LevelCount = document.Levels.Count,
                ContainmentRuleCount = document.Containment.Count,
            });
    }
}
