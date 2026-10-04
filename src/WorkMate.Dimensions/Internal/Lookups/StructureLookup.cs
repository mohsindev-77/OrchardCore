using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using YesSql;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <inheritdoc cref="IStructureLookup" />
/// <remarks>
/// Internal, with only the interface public. An <see cref="ISession"/> is its only dependency,
/// which is what lets <c>IDimensionGraphService</c> and <c>IDimensionValidator</c> depend on
/// this instead of <c>IStructureService</c> without cycling back through it.
/// </remarks>
internal sealed class StructureLookup : IStructureLookup
{
    private readonly ISession _session;

    public StructureLookup(ISession session) => _session = session;

    public async Task<StructureDocument?> GetAsync(
        string structureId,
        CancellationToken cancellationToken = default) =>
        await _session
            .Query<StructureDocument, StructureIndex>(index => index.StructureId == structureId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<StructureDocument?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default) =>
        await _session
            .Query<StructureDocument, StructureIndex>(index => index.Code == code)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<StructureDocument?> GetPrimaryOrganisationAsync(
        CancellationToken cancellationToken = default) =>
        await _session
            .Query<StructureDocument, StructureIndex>(index => index.IsPrimaryOrganisation)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StructureDocument>> ListAsync(
        CancellationToken cancellationToken = default) =>
        [
            .. await _session
                .Query<StructureDocument, StructureIndex>()
                .OrderBy(index => index.Code)
                .ListAsync(cancellationToken),
        ];
}
