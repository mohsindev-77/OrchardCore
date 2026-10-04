using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using YesSql;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <inheritdoc cref="IDimensionTypeLookup" />
/// <remarks>
/// Internal, like the other read-side implementations in this folder. Only the interface is
/// public. This is the whole point of it: an <see cref="ISession"/> is its only dependency, so
/// nothing that depends on <see cref="IDimensionTypeLookup"/> — <c>IDimensionValidator</c> most
/// of all — can be drawn into a cycle through it.
/// </remarks>
internal sealed class DimensionTypeLookup : IDimensionTypeLookup
{
    private readonly ISession _session;

    public DimensionTypeLookup(ISession session) => _session = session;

    public async Task<DimensionTypeDocument?> GetAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken = default) =>
        await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>(index => index.DimensionTypeId == dimensionTypeId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DimensionTypeDocument?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default) =>
        await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>(index => index.Code == code)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DimensionTypeDocument?> GetByContentTypeAsync(
        string contentTypeName,
        CancellationToken cancellationToken = default) =>
        await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>(index => index.ContentTypeName == contentTypeName)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DimensionTypeDocument>> ListAsync(
        CancellationToken cancellationToken = default) =>
        [
            .. await _session
                .Query<DimensionTypeDocument, DimensionTypeIndex>()
                .OrderBy(index => index.Code)
                .ListAsync(cancellationToken),
        ];
}
