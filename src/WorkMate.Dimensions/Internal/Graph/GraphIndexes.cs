using WorkMate.Dimensions.Indexes;
using YesSql.Indexes;

namespace WorkMate.Dimensions.Internal.Graph;

/// <summary>One dated parent-child edge.</summary>
/// <remarks>
/// Internal by design. Specification section 4 and architecture section 7 both say nothing
/// outside this module reads these tables; keeping the types unreachable is what turns that from
/// a convention into a compiler error.
/// </remarks>
internal sealed class DimensionLinkIndex : MapIndex
{
    public string StructureId { get; set; } = string.Empty;

    public string ChildId { get; set; } = string.Empty;

    public string ParentId { get; set; } = string.Empty;

    public DateTime EffectiveFrom { get; set; }

    public DateTime EffectiveToInclusive { get; set; }
}

internal sealed class DimensionLinkIndexProvider : IndexProvider<DimensionLinkDocument>
{
    /// <summary>
    /// The parent id on the row that records axis membership rather than an edge.
    /// </summary>
    /// <remarks>
    /// Empty, which no record id ever is, so the queries that look for children by parent id
    /// cannot match it by accident.
    /// </remarks>
    public const string NoParent = "";

    public override void Describe(DescribeContext<DimensionLinkDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<DimensionLinkIndex>()
            .Map(document =>
            {
                var rows = document.Parents.Select(link => new DimensionLinkIndex
                {
                    StructureId = document.StructureId,
                    ChildId = document.RecordId,
                    // A dated "no parent" entry maps onto the same empty-string sentinel the
                    // axis-membership row below already uses, so stage D2 needed no schema change
                    // and no migration: the column has always been a string that is sometimes
                    // empty, and the queries that look for children by parent id have never
                    // matched an empty one.
                    ParentId = link.ParentRecordId ?? NoParent,
                    EffectiveFrom = EffectiveDates.ToColumn(link.Range.From),
                    EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(link.Range.To),
                }).ToList();

                // A document with no parents would otherwise produce no rows at all and be
                // invisible to every query — which is exactly the case that matters, because a
                // record whose links were closed when its level was dropped has no parents left
                // and is precisely the record verification needs to still know about. Without
                // this row it silently stops being part of the axis's history.
                if (rows.Count == 0)
                {
                    rows.Add(new DimensionLinkIndex
                    {
                        StructureId = document.StructureId,
                        ChildId = document.RecordId,
                        ParentId = NoParent,
                        EffectiveFrom = EffectiveDates.ToColumn(DateOnly.MinValue),
                        EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(document.OnAxisUntil),
                    });
                }

                return rows;
            });
    }
}

/// <summary>
/// One ancestor-descendant pair, dated. The hot table: payroll, reporting, approvals and data
/// visibility all read it, often several times per request.
/// </summary>
internal sealed class DimensionClosureIndex : MapIndex
{
    public string StructureId { get; set; } = string.Empty;

    public string AncestorId { get; set; } = string.Empty;

    public string DescendantId { get; set; } = string.Empty;

    public int Depth { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime EffectiveToInclusive { get; set; }
}

internal sealed class DimensionClosureIndexProvider : IndexProvider<DimensionClosureDocument>
{
    public override void Describe(DescribeContext<DimensionClosureDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<DimensionClosureIndex>()
            .Map(document => document.Ancestors.Select(ancestor => new DimensionClosureIndex
            {
                StructureId = document.StructureId,
                AncestorId = ancestor.AncestorId,
                DescendantId = document.DescendantId,
                Depth = ancestor.Depth,
                EffectiveFrom = EffectiveDates.ToColumn(ancestor.Range.From),
                EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(ancestor.Range.To),
            }));
    }
}

/// <summary>One dated placement of an employee at a node.</summary>
internal sealed class EmployeeAssignmentIndex : MapIndex
{
    public string EmployeeId { get; set; } = string.Empty;

    public string StructureId { get; set; } = string.Empty;

    /// <summary>
    /// The node the employee sits at. Named for what it is rather than RecordId, because the
    /// query that matters joins it to the closure index's DescendantId.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    public decimal AllocationPercent { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime EffectiveToInclusive { get; set; }
}

internal sealed class EmployeeAssignmentIndexProvider : IndexProvider<EmployeeAssignmentDocument>
{
    public override void Describe(DescribeContext<EmployeeAssignmentDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<EmployeeAssignmentIndex>()
            .Map(document => document.Rows.Select(row => new EmployeeAssignmentIndex
            {
                EmployeeId = document.EmployeeId,
                StructureId = document.StructureId,
                NodeId = row.RecordId,
                AllocationPercent = row.AllocationPercent,
                IsPrimary = row.IsPrimary,
                EffectiveFrom = EffectiveDates.ToColumn(row.Range.From),
                EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(row.Range.To),
            }));
    }
}
