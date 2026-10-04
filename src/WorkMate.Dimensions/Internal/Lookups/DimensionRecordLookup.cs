using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
using YesSql;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <inheritdoc cref="IDimensionRecordLookup" />
/// <remarks>
/// Internal, with only the interface public. An <see cref="ISession"/> is its only dependency,
/// which is what lets <c>IDimensionValidator</c> depend on this instead of reading
/// <c>DimensionRecordPartIndex</c> directly or depending on <c>IDimensionService</c>.
/// </remarks>
internal sealed class DimensionRecordLookup : IDimensionRecordLookup
{
    private readonly ISession _session;

    public DimensionRecordLookup(ISession session) => _session = session;

    public async Task<DimensionNodeRef?> GetAsync(string recordId, CancellationToken cancellationToken = default)
    {
        var row = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.ContentItemId == recordId && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : DimensionNodeRef.FromIndex(row);
    }

    public async Task<DimensionNodeRef?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var row = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.Code == code && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : DimensionNodeRef.FromIndex(row);
    }

    public async Task<bool> CodeExistsAsync(
        string code,
        string? excludingRecordId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.Code == code)
            .ListAsync(cancellationToken);

        // A row with no content item id is the item under validation, before its first save
        // assigned one. Counting it would make every new record a duplicate of itself.
        return rows.Any(row =>
            !string.IsNullOrEmpty(row.ContentItemId) &&
            !string.Equals(row.ContentItemId, excludingRecordId, StringComparison.Ordinal));
    }
}
