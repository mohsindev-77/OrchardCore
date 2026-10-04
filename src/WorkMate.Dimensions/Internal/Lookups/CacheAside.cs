using Microsoft.Extensions.Caching.Memory;
using OrchardCore.Environment.Cache;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <summary>
/// The one cache-aside implementation behind both cached lookups.
/// </summary>
/// <remarks>
/// A plain <c>TryGetValue</c>/<c>CreateEntry</c> pair rather than
/// <c>IMemoryCache.GetOrCreateAsync</c>: that extension's generic signature is awkward to match
/// exactly for a type argument that is itself a nullable reference type (a cached "this id does
/// not exist" has to be a real, storable null), and getting that wrong is exactly the kind of
/// thing that compiles with a nullability warning and misbehaves at run time. Writing the two
/// steps out keeps the nullability honest.
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
