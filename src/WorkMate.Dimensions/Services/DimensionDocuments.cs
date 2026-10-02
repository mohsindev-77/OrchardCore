using YesSql;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The only way this module writes a document that is under optimistic concurrency control.
/// </summary>
/// <remarks>
/// ADR-0005 makes optimistic concurrency part of the storage decision. The weakness of the
/// mechanism it has to use is that the check is opt-in <em>per call</em>: YesSql's store-level
/// registration, <c>IConfiguration.CheckConcurrentUpdates</c>, is not reachable from a module
/// because Orchard builds the store's configuration itself and <c>YesSqlOptions</c> exposes no
/// hook for it. So a single <c>session.SaveAsync(document)</c> written anywhere in this module,
/// at any point in the next five years, silently turns the guarantee off for that path — with no
/// error, no warning and no way to tell afterwards which change was lost.
///
/// Funnelling every such write through one method is what makes that reviewable instead of
/// hopeful. <c>ConcurrencyCheckedDocuments</c> lists the types it applies to, and
/// <c>ConcurrencyCheckedSaveTests</c> fails the build if any other code in the module saves one
/// of them directly.
/// </remarks>
public static class DimensionDocuments
{
    /// <summary>
    /// The document types whose writes must be concurrency checked: the configuration documents
    /// two administrators can edit at once, and the graph documents a move or a merge rewrites.
    /// </summary>
    /// <remarks>
    /// Named by full name rather than by <c>Type</c> so that the graph documents can be listed
    /// here before they exist, and so that the guard test does not need them to be public — the
    /// link, closure and assignment documents live in an internal namespace by design.
    /// </remarks>
    public static readonly IReadOnlySet<string> ConcurrencyCheckedDocuments =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "WorkMate.Dimensions.Models.DimensionTypeDocument",
            "WorkMate.Dimensions.Models.StructureDocument",
            "WorkMate.Dimensions.Models.DimensionNameDocument",
        };

    /// <summary>
    /// Saves a document with the concurrency check on, so that the second of two concurrent
    /// writes raises <see cref="ConcurrencyException"/> instead of erasing the first.
    /// </summary>
    public static Task SaveCheckedAsync<TDocument>(
        this ISession session,
        TDocument document,
        CancellationToken cancellationToken = default)
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);

        return session.SaveAsync(document, checkConcurrency: true, collection: null, cancellationToken);
    }
}
