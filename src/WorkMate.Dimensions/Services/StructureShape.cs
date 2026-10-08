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
/// <param name="EmployeeAttachableDimensionTypeIds">
/// The types that may hold employees on this axis, or null to say nothing about it.
/// </param>
/// <remarks>
/// <paramref name="EmployeeAttachableDimensionTypeIds"/> is nullable where the other two are not,
/// and the difference is load-bearing. Null means "this caller is not describing employee
/// attachment", so an update leaves whatever the structure already declares; an empty list means
/// "this axis constrains nothing", which is a statement. A caller that could only say the latter
/// would silently clear a tenant's rule every time a screen that predates it posted a structure.
/// </remarks>
public sealed record StructureShape(
    IReadOnlyList<string> RootDimensionTypeIds,
    IReadOnlyList<StructureContainment> Containment,
    IReadOnlyList<string>? EmployeeAttachableDimensionTypeIds = null)
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
    ///
    /// It deliberately derives <em>nothing</em> about employee attachment, and leaves it null. A
    /// chain says which types this axis uses and in what order; it does not say which of them hold
    /// people, and guessing "the last level" would quietly impose a constraint on every structure
    /// that has ever been described as a chain — including every one the v5 migration upgraded.
    /// Unconstrained is the only answer a chain actually gives.
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
