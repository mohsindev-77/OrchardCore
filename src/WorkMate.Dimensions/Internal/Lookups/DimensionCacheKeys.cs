namespace WorkMate.Dimensions.Internal.Lookups;

/// <summary>
/// The cache keys and signal names for the per-tenant dimension-type and structure cache.
/// </summary>
/// <remarks>
/// Pure string construction, deliberately: it is unit-tested directly with no shell and no
/// <c>IMemoryCache</c> involved, and it is the one place a tenant-qualified key is built, so every
/// cached read and every invalidation call agree on the same shape.
///
/// <c>IMemoryCache</c> in this host is a singleton shared across every tenant in the process —
/// Orchard Core does not re-register it per shell — so the tenant name is part of every key, not
/// an afterthought. Two tenants whose dimension types happen to share a code must never see each
/// other's cached document.
///
/// Invalidation is coarse on purpose: one signal per aggregate kind per tenant, not one per id.
/// Dimension types and structures are read constantly and written rarely, per the module README,
/// so evicting every cached type (or every cached structure) on any one write to that aggregate
/// costs nothing worth avoiding, and it is far simpler to get right than tracking which individual
/// cached entries a given write could have affected.
/// </remarks>
internal static class DimensionCacheKeys
{
    private const string Prefix = "WorkMate.Dimensions";

    /// <summary>The change-token signal every cached dimension-type read is tagged with.</summary>
    public static string TypesSignal(string tenant) => $"{Prefix}:{tenant}:DimensionTypes";

    /// <summary>The change-token signal every cached structure read is tagged with.</summary>
    public static string StructuresSignal(string tenant) => $"{Prefix}:{tenant}:Structures";

    public static string TypeById(string tenant, string dimensionTypeId) =>
        $"{Prefix}:{tenant}:DimensionType:Id:{dimensionTypeId}";

    public static string TypeByCode(string tenant, string code) =>
        $"{Prefix}:{tenant}:DimensionType:Code:{code}";

    public static string TypeByContentType(string tenant, string contentTypeName) =>
        $"{Prefix}:{tenant}:DimensionType:ContentType:{contentTypeName}";

    public static string TypeList(string tenant) => $"{Prefix}:{tenant}:DimensionType:List";

    public static string StructureById(string tenant, string structureId) =>
        $"{Prefix}:{tenant}:Structure:Id:{structureId}";

    public static string StructureByCode(string tenant, string code) =>
        $"{Prefix}:{tenant}:Structure:Code:{code}";

    public static string StructurePrimaryOrganisation(string tenant) =>
        $"{Prefix}:{tenant}:Structure:Primary";

    public static string StructureList(string tenant) => $"{Prefix}:{tenant}:Structure:List";
}
