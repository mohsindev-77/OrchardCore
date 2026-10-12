namespace WorkMate.Records.Internal;

/// <summary>
/// The cache keys and signal names for the per-tenant lookup list cache.
/// </summary>
/// <remarks>
/// Pure string construction, deliberately: it is unit-tested directly with no shell and no
/// <c>IMemoryCache</c> involved, and it is the one place a tenant-qualified key is built, so every
/// cached read and every invalidation call agree on the same shape.
///
/// <b>The tenant is in every key, and the reason is not the one this comment used to give.</b> It
/// said <c>IMemoryCache</c> was a process-wide singleton shared by every tenant. That is wrong, and
/// it was checked rather than assumed when the invalidation defect behind ADR-0019 was being traced:
/// Orchard registers <c>IMemoryCache</c> in the tenant container (<c>OrchardCore.AddCaching</c>, and
/// the comment there says so), and a tenant that is released gets a new one — measured on a live
/// tenant, the instance changes when a recipe releases the shell. So two tenants cannot collide
/// today. The tenant stays in the key anyway, because a key that is correct only while a
/// registration in another codebase stays as it is, is not a key worth relying on, and because the
/// specification's section 9 rule says every cache key begins with the tenant name.
/// <c>RecordsCacheKeyTests</c> asserts it is there.
///
/// Invalidation is coarse on purpose: one signal for every lookup list in a tenant, not one per
/// list. Lists are read on every employee editor render and written a handful of times in a
/// tenant's life, so evicting all of them on any one write costs nothing worth avoiding and is far
/// simpler to get right than working out which cached entries a given write could have touched.
/// The same judgement <c>DimensionCacheKeys</c> made, for the same reasons.
/// </remarks>
internal static class RecordsCacheKeys
{
    private const string Prefix = "WorkMate.Records";

    /// <summary>The change-token signal every cached lookup list read is tagged with.</summary>
    public static string LookupListsSignal(string tenant) => $"{Prefix}:{tenant}:LookupLists";

    public static string LookupListByCode(string tenant, string code) =>
        $"{Prefix}:{tenant}:LookupList:Code:{code}";

    public static string LookupListList(string tenant) => $"{Prefix}:{tenant}:LookupList:List";
}
