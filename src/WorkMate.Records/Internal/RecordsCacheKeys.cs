namespace WorkMate.Records.Internal;

/// <summary>
/// The cache keys and signal names for the per-tenant lookup list cache.
/// </summary>
/// <remarks>
/// Pure string construction, deliberately: it is unit-tested directly with no shell and no
/// <c>IMemoryCache</c> involved, and it is the one place a tenant-qualified key is built, so every
/// cached read and every invalidation call agree on the same shape.
///
/// <b><c>IMemoryCache</c> in this host is a singleton shared across every tenant in the process</b>
/// — Orchard Core does not re-register it per shell — so the tenant name is part of every key, not
/// an afterthought. Two tenants whose grade lists happen to share the code <c>grade</c> must never
/// see each other's entries, and a lookup list is exactly the kind of small, frequently read
/// document where that mistake would be invisible until somebody noticed one customer's grades on
/// another customer's screen. <c>RecordsCacheKeyTests</c> asserts the tenant is in every key.
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
