namespace WorkMate.Core;

/// <summary>
/// One page of a result set, with the total so a caller can render a pager without asking twice.
/// </summary>
/// <remarks>
/// Services return this rather than a bare collection wherever the specification says a query is
/// paged and never unbounded — descendant and employee-under-a-node queries most of all, where a
/// structure with a thousand records under one node must not load a thousand items to answer a
/// count.
/// </remarks>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take)
{
    /// <summary>True when there is at least one more item after this page.</summary>
    public bool HasMore => Skip + Items.Count < Total;
}
