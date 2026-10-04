using FluentAssertions;
using WorkMate.Dimensions.Internal.Lookups;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The cache keys and signal names dimension-type and structure caching builds on.
/// </summary>
/// <remarks>
/// <c>IMemoryCache</c> in this host is a singleton shared across every tenant in the process —
/// Orchard Core does not re-register it per shell — so every key and every signal name this module
/// builds must carry the tenant, or two tenants whose dimension types happen to share a code could
/// see each other's cached document. This is pinned directly against the pure key-building
/// functions rather than only through an integration test, because a key that silently lost the
/// tenant would still look correct in every other respect.
/// </remarks>
public sealed class DimensionCacheKeysTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    [Theory]
    [InlineData("tenant-a", "id-1")]
    [InlineData("tenant-b", "id-2")]
    public void TypeByIdContainsTheTenant(string tenant, string id) =>
        DimensionCacheKeys.TypeById(tenant, id).Should().Contain(tenant);

    [Fact]
    public void EveryTypeKeyContainsTheTenant()
    {
        DimensionCacheKeys.TypeById(TenantA, "id").Should().Contain(TenantA);
        DimensionCacheKeys.TypeByCode(TenantA, "code").Should().Contain(TenantA);
        DimensionCacheKeys.TypeByContentType(TenantA, "ContentType").Should().Contain(TenantA);
        DimensionCacheKeys.TypeList(TenantA).Should().Contain(TenantA);
        DimensionCacheKeys.TypesSignal(TenantA).Should().Contain(TenantA);
    }

    [Fact]
    public void EveryStructureKeyContainsTheTenant()
    {
        DimensionCacheKeys.StructureById(TenantA, "id").Should().Contain(TenantA);
        DimensionCacheKeys.StructureByCode(TenantA, "code").Should().Contain(TenantA);
        DimensionCacheKeys.StructurePrimaryOrganisation(TenantA).Should().Contain(TenantA);
        DimensionCacheKeys.StructureList(TenantA).Should().Contain(TenantA);
        DimensionCacheKeys.StructuresSignal(TenantA).Should().Contain(TenantA);
    }

    [Fact]
    public void TheSameReferenceProducesDifferentKeysForDifferentTenants()
    {
        // The actual risk the tenant-in-key rule guards against: two tenants asking about the
        // same dimension-type id must not collide on one cache entry.
        DimensionCacheKeys.TypeById(TenantA, "shared-id").Should().NotBe(
            DimensionCacheKeys.TypeById(TenantB, "shared-id"));

        DimensionCacheKeys.StructureByCode(TenantA, "shared-code").Should().NotBe(
            DimensionCacheKeys.StructureByCode(TenantB, "shared-code"));
    }
}
