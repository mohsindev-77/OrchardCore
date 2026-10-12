using Microsoft.Extensions.Caching.Memory;
using OrchardCore.Environment.Cache;
using OrchardCore.Environment.Shell;
using WorkMate.Records.Indexes;
using WorkMate.Records.Internal;
using WorkMate.Records.Models;
using YesSql;

namespace WorkMate.Records.Services;

/// <summary>
/// The read side of the lookup lists: resolving a list in order to render something.
/// </summary>
/// <remarks>
/// <b>Separate from <see cref="ILookupListService"/>, and the separation is load-bearing.</b> This
/// is the cached path, and a cached read cannot be used by a write path: YesSql requires a document
/// already tracked by the session's identity map to be mutated through the exact instance the
/// session is tracking, and a cached instance can easily be a different object with the same id.
/// <c>ILookupListService</c>'s write paths therefore keep reading the session directly.
/// <c>CachedDimensionTypeLookup</c> records the same split at length for dimension types; this is
/// the same shape, reached for the same reason.
///
/// Every control that offers a list to a reader resolves it through here — the
/// <c>&lt;workmate-lookup&gt;</c> tag helper on the employee screens, the alternate editor view for
/// a bound content field, and the reconciliation screen's option set — because that is the path
/// taken on every employee editor render and the one worth not querying.
/// </remarks>
public interface ILookupListLookup
{
    /// <summary>The list with this code, or null. Case-insensitive.</summary>
    Task<LookupListDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Every list in the tenant, ordered by code.</summary>
    Task<IReadOnlyList<LookupListDocument>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads lookup lists straight from the session. The uncached half of the pair.</summary>
internal sealed class LookupListLookup : ILookupListLookup
{
    private readonly ISession _session;

    public LookupListLookup(ISession session) => _session = session;

    public async Task<LookupListDocument?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var upper = LookupCodes.Normalise(code).ToUpperInvariant();

        if (upper.Length == 0)
        {
            return null;
        }

        // On the folded column, because that is what the comparison actually uses — see
        // LookupListIndex.CodeUpper for why the database's own collation is not trusted with it.
        return await _session
            .Query<LookupListDocument, LookupListIndex>(index => index.CodeUpper == upper)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupListDocument>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var lists = await _session.Query<LookupListDocument, LookupListIndex>().ListAsync(cancellationToken);

        // Ordered here rather than in the query because the comparison has to be ordinal and
        // case-insensitive — the rule LookupCodes.Comparer states — and the database's collation is
        // not ours to assume.
        return [.. lists.OrderBy(list => list.Code, LookupCodes.Comparer)];
    }
}

/// <summary>
/// Caches lookup list reads per tenant: read on every employee editor render, written a handful of
/// times in a tenant's life.
/// </summary>
/// <remarks>
/// Wraps <see cref="LookupListLookup"/> rather than replacing it, so the query logic stays in one
/// place and caching is purely a decorator in front of it.
///
/// What this hands back is the shared cached instance itself, not a copy, and that is only safe
/// because nothing resolving <see cref="ILookupListLookup"/> ever mutates the result — every caller
/// is rendering. The write paths depend on <c>ILookupListService</c>, which reads the session
/// directly for exactly this reason.
///
/// Invalidated through <see cref="ISignal"/>, never by reaching into <see cref="IMemoryCache"/>:
/// every entry is tagged with the change token for this tenant's
/// <see cref="RecordsCacheKeys.LookupListsSignal"/>, and <c>LookupListService</c> signals that key
/// after every write. <c>ISignal</c> resolves to Orchard's local <c>Signal</c> today; if a
/// distributed cache is ever configured for this host (specification section 11, open decision 6)
/// it resolves to a distributed implementation that publishes the same call over the message bus,
/// and nothing here changes.
/// </remarks>
internal sealed class CachedLookupListLookup : ILookupListLookup
{
    private readonly LookupListLookup _inner;
    private readonly IMemoryCache _cache;
    private readonly ISignal _signal;
    private readonly ShellSettings _shellSettings;

    public CachedLookupListLookup(
        LookupListLookup inner,
        IMemoryCache cache,
        ISignal signal,
        ShellSettings shellSettings)
    {
        _inner = inner;
        _cache = cache;
        _signal = signal;
        _shellSettings = shellSettings;
    }

    public Task<LookupListDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        CacheAside.GetOrSetAsync(
            _cache,
            _signal,
            // Folded into the key as well as into the query: two spellings of one code are one
            // list, so they must not be two cache entries that can disagree after a write.
            RecordsCacheKeys.LookupListByCode(
                _shellSettings.Name,
                LookupCodes.Normalise(code).ToUpperInvariant()),
            RecordsCacheKeys.LookupListsSignal(_shellSettings.Name),
            () => _inner.GetByCodeAsync(code, cancellationToken));

    public Task<IReadOnlyList<LookupListDocument>> ListAsync(CancellationToken cancellationToken = default) =>
        CacheAside.GetOrSetAsync(
            _cache,
            _signal,
            RecordsCacheKeys.LookupListList(_shellSettings.Name),
            RecordsCacheKeys.LookupListsSignal(_shellSettings.Name),
            () => _inner.ListAsync(cancellationToken));
}

/// <summary>
/// The one cache-aside implementation behind the cached lookup.
/// </summary>
/// <remarks>
/// A plain <c>TryGetValue</c>/<c>CreateEntry</c> pair rather than
/// <c>IMemoryCache.GetOrCreateAsync</c>, for the reason <c>WorkMate.Dimensions</c>' own
/// <c>CacheAside</c> gives: that extension's generic signature is awkward to match for a type
/// argument that is itself a nullable reference type — a cached "this code does not exist" has to
/// be a real, storable null — and getting it wrong compiles with a nullability warning and
/// misbehaves at run time. Writing the two steps out keeps the nullability honest.
/// </remarks>
internal static class CacheAside
{
    public static async Task<T> GetOrSetAsync<T>(
        IMemoryCache cache,
        ISignal signal,
        string key,
        string signalKey,
        Func<Task<T>> factory)
    {
        if (cache.TryGetValue(key, out T? cached))
        {
            return cached!;
        }

        var value = await factory();

        using (var entry = cache.CreateEntry(key))
        {
            entry.Value = value;
            entry.AddExpirationToken(signal.GetToken(signalKey));
        }

        return value;
    }
}
