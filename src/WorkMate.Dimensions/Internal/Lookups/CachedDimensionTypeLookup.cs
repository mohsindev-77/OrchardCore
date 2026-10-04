using OrchardCore.Environment.Cache;
using OrchardCore.Environment.Shell;
using Microsoft.Extensions.Caching.Memory;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <summary>
/// Caches dimension-type reads: read constantly, changed rarely, per the module README's caching
/// section.
/// </summary>
/// <remarks>
/// Wraps <see cref="DimensionTypeLookup"/> rather than replacing it, so the query logic stays in
/// one place and caching is purely a decorator in front of it — every caller of
/// <see cref="IDimensionTypeLookup"/> gets it for free, including <see cref="IDimensionValidator"/>
/// and <c>DimensionTypeService</c>'s own read-only passthrough methods.
///
/// What this hands back is the shared cached instance itself, not a copy, and that is only safe
/// because nothing that resolves <see cref="IDimensionTypeLookup"/> ever mutates the result.
/// <c>DimensionTypeService.UpdateAsync</c> and <c>RetireAsync</c> — the two places that load a
/// document in order to change and save it — deliberately depend on the concrete
/// <see cref="DimensionTypeLookup"/> (uncached) for that load instead, for a reason specific to
/// YesSql: a document already tracked by this session's identity map must be mutated through the
/// exact instance the session is tracking, not a different object with the same id, or
/// <c>ISession.SaveAsync</c> throws "an object with the same identity is already part of this
/// transaction". A cached instance can easily be a different object than whatever a prior read or
/// write already registered with this session, so a write path cannot safely use the cache for
/// its own load-before-mutate step.
///
/// Invalidated through <see cref="ISignal"/>, never by reaching into <see cref="IMemoryCache"/>
/// directly: every entry here is tagged with the change token for this tenant's
/// <see cref="DimensionCacheKeys.TypesSignal"/>, and <c>DimensionTypeService</c> calls
/// <see cref="ISignal.SignalTokenAsync"/> on that same key after every write. <c>ISignal</c>
/// resolves to Orchard's local, single-process <c>Signal</c> today; if a distributed cache is ever
/// configured for this host (specification section 11, open decision 6), it resolves to
/// <c>OrchardCore.Caching.Distributed.DistributedSignal</c> instead, which publishes the same call
/// over <c>IMessageBus</c> so every server's cache invalidates together. Nothing here needs to
/// change for that to happen — see the module README.
/// </remarks>
internal sealed class CachedDimensionTypeLookup : IDimensionTypeLookup
{
    private readonly DimensionTypeLookup _inner;
    private readonly IMemoryCache _cache;
    private readonly ISignal _signal;
    private readonly ShellSettings _shellSettings;

    public CachedDimensionTypeLookup(
        DimensionTypeLookup inner,
        IMemoryCache cache,
        ISignal signal,
        ShellSettings shellSettings)
    {
        _inner = inner;
        _cache = cache;
        _signal = signal;
        _shellSettings = shellSettings;
    }

    public Task<DimensionTypeDocument?> GetAsync(
        string dimensionTypeId, CancellationToken cancellationToken = default) =>
        Cached(
            DimensionCacheKeys.TypeById(_shellSettings.Name, dimensionTypeId),
            () => _inner.GetAsync(dimensionTypeId, cancellationToken));

    public Task<DimensionTypeDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Cached(
            DimensionCacheKeys.TypeByCode(_shellSettings.Name, code),
            () => _inner.GetByCodeAsync(code, cancellationToken));

    public Task<DimensionTypeDocument?> GetByContentTypeAsync(
        string contentTypeName, CancellationToken cancellationToken = default) =>
        Cached(
            DimensionCacheKeys.TypeByContentType(_shellSettings.Name, contentTypeName),
            () => _inner.GetByContentTypeAsync(contentTypeName, cancellationToken));

    public Task<IReadOnlyList<DimensionTypeDocument>> ListAsync(CancellationToken cancellationToken = default) =>
        CacheAside.GetOrSetAsync(
            _cache,
            _signal,
            DimensionCacheKeys.TypeList(_shellSettings.Name),
            DimensionCacheKeys.TypesSignal(_shellSettings.Name),
            () => _inner.ListAsync(cancellationToken));

    private Task<DimensionTypeDocument?> Cached(string key, Func<Task<DimensionTypeDocument?>> factory) =>
        CacheAside.GetOrSetAsync(
            _cache, _signal, key, DimensionCacheKeys.TypesSignal(_shellSettings.Name), factory);
}
