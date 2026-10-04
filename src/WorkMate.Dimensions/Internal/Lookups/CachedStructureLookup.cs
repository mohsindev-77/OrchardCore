using OrchardCore.Environment.Cache;
using OrchardCore.Environment.Shell;
using Microsoft.Extensions.Caching.Memory;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <summary>
/// Caches structure reads: read constantly, changed rarely, per the module README's caching
/// section. See <see cref="CachedDimensionTypeLookup"/> for the invalidation mechanism and for why
/// this hands back the shared cached instance rather than a copy, both identical here with
/// structures in place of dimension types.
/// </summary>
internal sealed class CachedStructureLookup : IStructureLookup
{
    private readonly StructureLookup _inner;
    private readonly IMemoryCache _cache;
    private readonly ISignal _signal;
    private readonly ShellSettings _shellSettings;

    public CachedStructureLookup(
        StructureLookup inner,
        IMemoryCache cache,
        ISignal signal,
        ShellSettings shellSettings)
    {
        _inner = inner;
        _cache = cache;
        _signal = signal;
        _shellSettings = shellSettings;
    }

    public Task<StructureDocument?> GetAsync(string structureId, CancellationToken cancellationToken = default) =>
        Cached(
            DimensionCacheKeys.StructureById(_shellSettings.Name, structureId),
            () => _inner.GetAsync(structureId, cancellationToken));

    public Task<StructureDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Cached(
            DimensionCacheKeys.StructureByCode(_shellSettings.Name, code),
            () => _inner.GetByCodeAsync(code, cancellationToken));

    public Task<StructureDocument?> GetPrimaryOrganisationAsync(CancellationToken cancellationToken = default) =>
        Cached(
            DimensionCacheKeys.StructurePrimaryOrganisation(_shellSettings.Name),
            () => _inner.GetPrimaryOrganisationAsync(cancellationToken));

    public Task<IReadOnlyList<StructureDocument>> ListAsync(CancellationToken cancellationToken = default) =>
        CacheAside.GetOrSetAsync(
            _cache,
            _signal,
            DimensionCacheKeys.StructureList(_shellSettings.Name),
            DimensionCacheKeys.StructuresSignal(_shellSettings.Name),
            () => _inner.ListAsync(cancellationToken));

    private Task<StructureDocument?> Cached(string key, Func<Task<StructureDocument?>> factory) =>
        CacheAside.GetOrSetAsync(
            _cache, _signal, key, DimensionCacheKeys.StructuresSignal(_shellSettings.Name), factory);
}
