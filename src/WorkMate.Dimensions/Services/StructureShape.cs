using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// What a structure permits: which types may be roots, and which may contain which.
/// </summary>
/// <remarks>
/// Passed as one value rather than two parameters because the two are only meaningful together —
/// roots with no containment is a list of orphans, containment with no roots is a graph with no
/// way in — and because a caller that has neither, because it is still describing a structure the
/// old chain-and-skip-flag way, should be able to say so by passing nothing at all. See ADR-0010.
/// </remarks>
/// <param name="RootDimensionTypeIds">The types a record may be a root of this axis as.</param>
/// <param name="Containment">Every permitted parent-child pairing.</param>
public sealed record StructureShape(
    IReadOnlyList<string> RootDimensionTypeIds,
    IReadOnlyList<StructureContainment> Containment)
{
    /// <summary>
    /// The shape the old chain-and-skip-flag description permitted: adjacent pairs always, deeper
    /// pairs only when skipping was on, and a self-pair for each type that declares self-nesting.
    /// </summary>
    /// <remarks>
    /// The one derivation, shared by the migration that upgrades a tenant, the recipe step that
    /// reads a row still written the old way, and this service's own default. Two derivations
    /// would mean an upgraded tenant and a freshly seeded one could end up with different rules
    /// from the same description.
    /// </remarks>
    public static StructureShape FromChain(
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        IReadOnlySet<string> selfNestingDimensionTypeIds)
    {
        var (roots, containment) = Internal.StructureContainmentDerivation.FromChain(
            levelDimensionTypeIds, allowSkipLevel, selfNestingDimensionTypeIds);

        return new StructureShape(roots, containment);
    }
}
