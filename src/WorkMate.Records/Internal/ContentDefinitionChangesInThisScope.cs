namespace WorkMate.Records.Internal;

/// <summary>
/// Which content types this shell scope has altered the definition of. ADR-0006, slice B3.
/// </summary>
/// <remarks>
/// <b>The half of ADR-0006 that the null check does not cover.</b> The ADR's repair —
/// <c>DimensionRecordHandler.ActivatingAsync</c>, and now <c>EmployeeHandler</c>'s — asks whether
/// the <em>cached</em> type definition is missing, which is the right question for a type
/// <b>created</b> in this scope: the cache has never heard of it and <c>NewAsync</c> welds nothing.
///
/// A type <b>altered</b> in this scope fails differently and more quietly. <c>Employee</c> has
/// existed since the migration, so the cached read returns it — the <em>old</em> one, without the
/// bag a publish has just welded on. <c>NewAsync</c> then returns an item that is correct in every
/// respect except the one that changed, and every value written into the new section is discarded
/// with nothing thrown. A null check cannot see that, because nothing is null.
///
/// <b>Why a marker rather than always loading.</b> Comparing the cached definition with the loaded
/// one would answer the question, and would cost a document read on every employee <em>activated</em>
/// — which an import does in bulk. The scope knows the answer for free: the only thing that alters
/// <c>Employee</c>'s definition at run time is <c>IFormDefinitionService.PublishAsync</c>, so it
/// says so, and the handler pays nothing on every other path.
///
/// <b>Scoped, and that is the whole mechanism.</b> One instance per shell scope means the flag
/// lives exactly as long as the window it describes: a later request gets a fresh instance and a
/// cache that has been invalidated by the commit, so there is nothing to repair and nothing to
/// remember.
/// </remarks>
/// <remarks>
/// <b>Public only because <c>EmployeeHandler</c> is.</b> Orchard activates a content handler by
/// reflection, so its constructor is public and every parameter type has to be reachable. The
/// namespace is the honest statement of intent: nothing outside this module should take a
/// dependency on it, and nothing does.
/// </remarks>
public sealed class ContentDefinitionChangesInThisScope
{
    private readonly HashSet<string> _altered = new(StringComparer.Ordinal);

    /// <summary>Records that this scope has altered <paramref name="contentType"/>'s definition.</summary>
    public void MarkAltered(string contentType)
    {
        if (!string.IsNullOrEmpty(contentType))
        {
            _altered.Add(contentType);
        }
    }

    /// <summary>
    /// Whether this scope altered that definition, and the cached read is therefore stale.
    /// </summary>
    public bool WasAltered(string contentType) =>
        !string.IsNullOrEmpty(contentType) && _altered.Contains(contentType);
}
