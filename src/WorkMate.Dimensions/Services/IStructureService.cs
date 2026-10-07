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
    /// Defines an axis. <paramref name="levelDimensionTypeIds"/> is the axis's vocabulary in
    /// reading order, root first; ordinals are assigned from that order.
    /// </summary>
    /// <remarks>
    /// Containment is separate from the level order since ADR-0010. A caller that already knows
    /// which pairings it wants passes <paramref name="shape"/>; one that only has the old
    /// chain-and-skip-flag description passes <see cref="StructureShape.FromChain"/>'s result, or
    /// leaves it null to have it derived here. Both go through one derivation, so the same
    /// description always produces the same map.
    /// </remarks>
    Task<DimensionResult<StructureDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        bool isPrimaryOrganisation,
        StructureShape? shape = null,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an axis's name, vocabulary and rules. The code is not changeable: links, closure
    /// rows and assignments are scoped by the structure's id, and recipes reference it by code.
    /// </summary>
    /// <remarks>
    /// Refused, with nothing saved, if the new rules would leave an existing placement invalid —
    /// see <see cref="PlanLevelChangeAsync"/>, which this calls before writing anything. Adding or
    /// removing a level never refuses: the graph service closes rather than deletes, so that half
    /// of a level change is always safe.
    /// </remarks>
    Task<DimensionResult<StructureDocument>> UpdateAsync(
        string structureId,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        bool isPrimaryOrganisation,
        StructureShape? shape = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What changing this structure's vocabulary and containment would do to the records already
    /// placed on it, without changing anything. The dry run a screen shows before a human confirms
    /// the change, the same way <c>IDimensionService</c> shows one before a move or a merge.
    /// </summary>
    Task<DimensionResult<StructureLevelChangePlan>> PlanLevelChangeAsync(
        string structureId,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        StructureShape? shape = null,
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
