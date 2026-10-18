using YesSql;

namespace WorkMate.Records.Services;

/// <summary>
/// The only way this module writes a document that is under optimistic concurrency control.
/// </summary>
/// <remarks>
/// A deliberate near-copy of <c>WorkMate.Dimensions.Services.DimensionDocuments</c>, for the reason
/// the module README already records about <c>RecordResult</c> and <c>IRecordsAuthorisation</c>:
/// this module's guard test has to scan this module's own source, and a helper owned by the
/// dimension engine would make the rule — and any future change to it — that module's to make.
///
/// The weakness being guarded is YesSql's, and it is worth restating because it is the reason a
/// source-level test exists at all. The concurrency check is opt-in <em>per call</em>: the
/// store-level registration <c>IConfiguration.CheckConcurrentUpdates</c> is not reachable from a
/// module, because Orchard builds the store's configuration itself and <c>YesSqlOptions</c> exposes
/// no hook for it. So a single <c>session.SaveAsync(document)</c> written anywhere in this module,
/// at any point in the next five years, silently turns the guarantee off for that path — with no
/// error, no warning, and no way to tell afterwards which administrator's entries were lost.
///
/// Funnelling every such write through one method is what makes that reviewable instead of hopeful.
/// <see cref="ConcurrencyCheckedDocuments"/> lists the types it applies to, and
/// <c>ConcurrencyCheckedSaveTests</c> in <c>WorkMate.Records.Tests</c> fails the build if any other
/// code in this module saves one of them directly.
///
/// <b>Employees are not on the list, and that is not an oversight.</b> An employee is a content
/// item, written through <c>IContentManager</c>, which owns its own write path and its own
/// versioning; this helper is for the plain documents this module stores beside them.
/// </remarks>
public static class RecordsDocuments
{
    /// <summary>
    /// The document types whose writes must be concurrency checked.
    /// </summary>
    /// <remarks>
    /// Named by full name rather than by <c>Type</c>, matching the dimension engine's list, so that
    /// a document can be listed here before it exists and without being made public.
    /// </remarks>
    public static readonly IReadOnlySet<string> ConcurrencyCheckedDocuments =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "WorkMate.Records.Models.LookupListDocument",

            // Slice B3. The definition is the working copy and two designers saving it at once is
            // an ordinary thing to happen; the version is immutable once written, and is listed
            // for the same reason the guard finds it — a document whose writes are not checked is
            // a document that can lose one silently.
            "WorkMate.Records.Models.FormDefinitionDocument",
            "WorkMate.Records.Models.FormVersionDocument",
        };

    /// <summary>
    /// Saves a document with the concurrency check on, so that the second of two concurrent writes
    /// raises <see cref="ConcurrencyException"/> instead of erasing the first.
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
