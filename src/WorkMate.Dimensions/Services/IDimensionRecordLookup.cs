namespace WorkMate.Dimensions.Services;

/// <summary>
/// Read-only access to dimension records: does this reference exist, and what does it look like.
/// </summary>
/// <remarks>
/// The read side of the record aggregate, for the same reason as
/// <see cref="IDimensionTypeLookup"/> and <see cref="IStructureLookup"/>. <see cref="IDimensionValidator"/>
/// depends on this instead of reading <c>DimensionRecordPartIndex</c> itself or depending on
/// <see cref="IDimensionService"/>, and <see cref="IDimensionService"/>'s own read methods are
/// built on it too, so there is one implementation of "resolve a record" rather than two that
/// could drift. See the module README for the pattern.
///
/// Internal: it bypasses both permissions and effective-dating by design — every method here is
/// undated, where <see cref="IDimensionService"/>'s public read path defaults to today and filters
/// accordingly.
/// </remarks>
internal interface IDimensionRecordLookup
{
    /// <summary>The record with this id, or null. Undated: present regardless of its effective range.</summary>
    Task<DimensionNodeRef?> GetAsync(string recordId, CancellationToken cancellationToken = default);

    /// <summary>The record with this code, or null. Undated.</summary>
    Task<DimensionNodeRef?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a record other than <paramref name="excludingRecordId"/> already holds this code.
    /// </summary>
    /// <remarks>
    /// Across every dimension type and including retired records: a code is unique within the
    /// tenant, not within a type, and reusing a retired unit's code makes every historical report
    /// ambiguous about which one it means. <paramref name="excludingRecordId"/> is null when
    /// checking a new record, and the id of the record being edited otherwise — without it,
    /// saving a record a second time would report it as a duplicate of itself.
    /// </remarks>
    Task<bool> CodeExistsAsync(
        string code,
        string? excludingRecordId,
        CancellationToken cancellationToken = default);
}
