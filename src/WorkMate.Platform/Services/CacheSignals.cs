using OrchardCore.Environment.Cache;
using OrchardCore.Environment.Shell.Scope;

namespace WorkMate.Platform.Services;

/// <summary>
/// The one way a WorkMate module invalidates a cached read after a write. ADR-0019.
/// </summary>
/// <remarks>
/// <b>An invalidation is not finished until the write has committed, and
/// <see cref="ISignal.SignalTokenAsync"/> on its own finishes it far too early.</b> That is the
/// whole of this helper, and it exists because the difference is invisible until it is expensive.
///
/// A module writes through YesSql's <c>ISession</c>, which stages the write and commits it when the
/// shell scope is disposed: Orchard registers <c>IDocumentStore.CommitAsync</c> as a before-dispose
/// callback on the scope. So between a write and its commit there is a window — the rest of a
/// request, the rest of a recipe step — in which the database still answers with the old state.
/// A signal sent inside that window cancels the change token at once, and any read arriving before
/// the commit then refills the cache from the state the write is about to replace, under a token
/// that has already been used and will never be signalled again. The cache is wrong for the life of
/// the shell, with no error anywhere: the symptom is a screen showing the world as it was until the
/// process restarts.
///
/// <b>So the signal is sent three times, at the three moments the cached answer can become wrong.</b>
///
/// <i>Now</i>, because a reader in this same scope reads through this same session, which already
/// sees the staged write — so evicting immediately is what makes a read-after-write inside one
/// scope correct. The dimension engine depends on this and says so loudly when it is taken away:
/// its recipe steps, its designer actions and its scenario builders all write and then read in one
/// scope, and deferring the only signal fails some thirty of its tests.
///
/// <i>After the commit</i>, through <see cref="ShellScope.AddDeferredSignal"/>, which Orchard sends
/// in <c>BeforeDisposeAsync</c> once every before-dispose callback — the commit among them — has
/// run. This is the one that was missing, and the only one that can evict an entry a reader in
/// <em>another</em> scope put there while the write was in flight.
///
/// <i>On the way out of a failure</i>, through <see cref="ShellScope.AddExceptionHandler"/>. If the
/// scope fails, YesSql cancels the document store and nothing is written — but a reader in this
/// scope may already have cached the staged state, and the deferred signal is never reached. Then
/// the cache holds a list, or a dimension type, that does not exist. This is the mirror image of
/// the defect above and was equally present before; a recipe step that writes five rows and fails
/// on the sixth is the reachable case.
///
/// Verified against the pinned Orchard Core 3.0.1 with <c>tools/api-probe</c>:
/// <c>SignalExtensions.DeferredSignalToken</c>, <c>ShellScope.AddDeferredSignal</c> and
/// <c>ShellScope.AddExceptionHandler</c> all exist, and <c>OrchardCore.Data.YesSql</c> registers the
/// commit through <c>RegisterBeforeDispose</c>, which runs before the deferred signals.
///
/// <b>The one case this cannot cover</b> is a caller with no ambient shell scope, where
/// <see cref="ShellScope.AddDeferredSignal"/> is a silent no-op. Every write path in this product
/// runs inside one. There, the immediate signal is all there is — which is no worse than the
/// behaviour this replaces, and "silently does nothing" is the wrong failure for a cache.
/// </remarks>
public static class CacheSignals
{
    /// <summary>
    /// Evicts everything tagged with <paramref name="key"/> now, again once this scope's writes have
    /// committed, and again if the scope fails instead.
    /// </summary>
    public static async Task SignalNowAndAfterCommitAsync(this ISignal signal, string key)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await signal.SignalTokenAsync(key);

        var scope = ShellScope.Current;

        if (scope is null)
        {
            return;
        }

        // Idempotent in Orchard: deferred signals are held in a set, so the same key asked for
        // twice in one scope is sent once.
        scope.AddDeferredSignal(key);

        var handlerKey = ExceptionHandlerFor(key);

        // Registered once per key per scope. A recipe step writing ten lists calls this ten times,
        // and ten handlers all cancelling one token would be ten copies of the same work on a path
        // that is already failing.
        if (ShellScope.Get<object>(handlerKey) is not null)
        {
            return;
        }

        ShellScope.Set(handlerKey, HandlerRegistered);

        // The signal is captured rather than resolved from the failing scope: ISignal is a singleton
        // in the tenant container, so this is the same instance the entries were tagged through, and
        // resolving a service while unwinding from a failure is a way to fail twice.
        ShellScope.AddExceptionHandler((_, _) => signal.SignalTokenAsync(key));
    }

    /// <summary>
    /// The scope-state key under which the exception handler for one signal key is recorded.
    /// </summary>
    /// <remarks>
    /// Prefixed, because <see cref="ShellScope"/>'s state is one dictionary shared by everything in
    /// the scope, and a bare signal key would be ours only by luck.
    /// </remarks>
    private static string ExceptionHandlerFor(string key) =>
        $"WorkMate.Platform:CacheSignals:ExceptionHandler:{key}";

    /// <summary>A marker meaning "the handler for this key is already registered on this scope".</summary>
    private static readonly object HandlerRegistered = new();
}
