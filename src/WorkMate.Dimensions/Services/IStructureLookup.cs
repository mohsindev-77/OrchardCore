using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// Read-only access to structures: does this axis exist, and what does it look like.
/// </summary>
/// <remarks>
/// The read side of the structure aggregate, for the same reason as
/// <see cref="IDimensionTypeLookup"/>: it is what <see cref="IDimensionValidator"/> and
/// <see cref="IDimensionGraphService"/> depend on instead of <see cref="IStructureService"/>,
/// so that neither of them cycles back through the write service that depends on them. See the
/// module README for the pattern.
///
/// Internal: it bypasses permissions by design, which is what a validator or the graph service
/// checking whether an axis exists needs. <see cref="IStructureService"/> is the public read path.
/// </remarks>
internal interface IStructureLookup
{
    /// <summary>The axis with this id, or null.</summary>
    Task<StructureDocument?> GetAsync(string structureId, CancellationToken cancellationToken = default);

    /// <summary>The axis with this code, or null.</summary>
    Task<StructureDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>The primary organisation axis, or null if the tenant has not nominated one.</summary>
    Task<StructureDocument?> GetPrimaryOrganisationAsync(CancellationToken cancellationToken = default);

    /// <summary>Every axis in the tenant.</summary>
    Task<IReadOnlyList<StructureDocument>> ListAsync(CancellationToken cancellationToken = default);
}
