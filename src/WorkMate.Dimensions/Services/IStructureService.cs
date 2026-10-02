using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The only way the rest of the product touches structures: the named axes and their ordered
/// levels.
/// </summary>
/// <remarks>
/// Tenant-scoped by construction, permission checked here rather than only in a controller, for
/// the same reasons as <see cref="IDimensionTypeService"/>.
/// </remarks>
public interface IStructureService
{
    /// <summary>
    /// Defines an axis. <paramref name="levelDimensionTypeIds"/> is root first; ordinals are
    /// assigned from that order, so a caller reorders levels by passing them in the order it
    /// wants rather than by computing ordinals.
    /// </summary>
    Task<DimensionResult<StructureDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        bool isPrimaryOrganisation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an axis's name, levels and rules. The code is not changeable: links, closure rows
    /// and assignments are scoped by the structure's id, and recipes reference it by code.
    /// </summary>
    Task<DimensionResult<StructureDocument>> UpdateAsync(
        string structureId,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        bool isPrimaryOrganisation,
        CancellationToken cancellationToken = default);

    /// <summary>The axis with this id, or null.</summary>
    Task<StructureDocument?> GetAsync(string structureId, CancellationToken cancellationToken = default);

    /// <summary>The axis with this code, or null. Recipes reference structures this way.</summary>
    Task<StructureDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The primary organisation axis, or null if the tenant has not nominated one. Approvals and
    /// data visibility default to this.
    /// </summary>
    Task<StructureDocument?> GetPrimaryOrganisationAsync(CancellationToken cancellationToken = default);

    /// <summary>Every axis in the tenant.</summary>
    Task<IReadOnlyList<StructureDocument>> ListAsync(CancellationToken cancellationToken = default);
}
