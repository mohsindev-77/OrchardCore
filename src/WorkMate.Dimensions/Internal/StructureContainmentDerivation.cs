using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Internal;

/// <summary>
/// Turns the old chain-of-levels form — an ordered list plus a skip-level flag — into the explicit
/// containment map ADR-0010 replaced it with.
/// </summary>
/// <remarks>
/// One implementation with two callers on purpose. <c>Migrations.UpdateFrom4Async</c> runs it over
/// every structure a tenant already has, and the <c>structures</c> recipe step runs it over a row
/// that still states <c>levelTypeCodes</c> and <c>allowSkipLevel</c> rather than an explicit map.
/// If those two derivations could differ, an upgraded tenant and a freshly seeded one would end up
/// with different rules from the same description, and the upgrade test would be proving something
/// about the migration that was not true of the recipe.
///
/// The derivation is exactly the set the old arithmetic permitted — adjacent pairs always, the
/// transitive pairs only when skipping was on, and a self-pair for each type that declares
/// self-nesting. Nothing is widened and nothing is narrowed, which is what lets a tenant upgrade
/// without a single live placement becoming invalid.
/// </remarks>
internal static class StructureContainmentDerivation
{
    /// <summary>The map the given chain permitted, and the types that were its roots.</summary>
    public static (IReadOnlyList<string> RootDimensionTypeIds, IReadOnlyList<StructureContainment> Containment)
        FromChain(
            IReadOnlyList<string> levelDimensionTypeIds,
            bool allowSkipLevel,
            IReadOnlySet<string> selfNestingDimensionTypeIds)
    {
        ArgumentNullException.ThrowIfNull(levelDimensionTypeIds);
        ArgumentNullException.ThrowIfNull(selfNestingDimensionTypeIds);

        if (levelDimensionTypeIds.Count == 0)
        {
            return ([], []);
        }

        var pairs = new List<StructureContainment>();

        for (var parent = 0; parent < levelDimensionTypeIds.Count; parent++)
        {
            // Adjacent always; everything deeper only where skipping was switched on, which is
            // precisely what the old "childLevel - parentLevel > 1 && !AllowSkipLevel" refused.
            var deepest = allowSkipLevel
                ? levelDimensionTypeIds.Count - 1
                : Math.Min(parent + 1, levelDimensionTypeIds.Count - 1);

            for (var child = parent + 1; child <= deepest; child++)
            {
                pairs.Add(new StructureContainment(levelDimensionTypeIds[parent], levelDimensionTypeIds[child]));
            }
        }

        // The type's own flag was the whole of the old rule, so every type carrying it had the
        // self-pair implicitly on every axis it appeared on. Making it explicit here keeps the
        // upgrade lossless; from now on a customer can remove it from one axis and not another,
        // which is the second thing ADR-0010 set out to fix.
        foreach (var dimensionTypeId in levelDimensionTypeIds.Where(selfNestingDimensionTypeIds.Contains))
        {
            pairs.Add(new StructureContainment(dimensionTypeId, dimensionTypeId));
        }

        return ([levelDimensionTypeIds[0]], Deduplicate(pairs));
    }

    /// <summary>
    /// The same list with repeats and blanks gone, in a stable order. Applied to anything on its
    /// way onto a document, so that two descriptions of the same map compare equal — which is what
    /// the recipe's "identical, so skip" comparison in ADR-0008 rests on.
    /// </summary>
    public static IReadOnlyList<StructureContainment> Deduplicate(IEnumerable<StructureContainment> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        return
        [
            .. pairs
                .Where(pair =>
                    !string.IsNullOrWhiteSpace(pair.ParentDimensionTypeId) &&
                    !string.IsNullOrWhiteSpace(pair.ChildDimensionTypeId))
                .DistinctBy(pair => (pair.ParentDimensionTypeId, pair.ChildDimensionTypeId))
                .OrderBy(pair => pair.ParentDimensionTypeId, StringComparer.Ordinal)
                .ThenBy(pair => pair.ChildDimensionTypeId, StringComparer.Ordinal),
        ];
    }

    /// <summary>The same, for the root list.</summary>
    public static IReadOnlyList<string> DeduplicateRoots(IEnumerable<string> dimensionTypeIds)
    {
        ArgumentNullException.ThrowIfNull(dimensionTypeIds);

        return
        [
            .. dimensionTypeIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal),
        ];
    }
}
