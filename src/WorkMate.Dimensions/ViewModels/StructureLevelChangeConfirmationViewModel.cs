using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// What a structure's level change will do, shown once before it is applied. The form on this
/// screen re-posts exactly the values it was shown — nothing here is editable — because a value
/// changed here would describe a different change than the one the impact above it was computed
/// for; going back and changing something starts a fresh preview instead.
/// </summary>
public sealed class StructureLevelChangeConfirmationViewModel
{
    public string StructureId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; }

    public bool IsPrimaryOrganisation { get; set; }

    public List<string> LevelDimensionTypeIds { get; set; } = [];

    /// <summary>
    /// The containment grid exactly as it was posted, carried through unchanged.
    /// </summary>
    /// <remarks>
    /// Re-posted rather than recomputed from the levels, because since ADR-0010 the grid is not
    /// derivable from the level order: the whole point of it is that it says something the order
    /// cannot. Rebuilding it here would quietly confirm a different change from the one the impact
    /// above was measured for.
    /// </remarks>
    public List<string> RootDimensionTypeIds { get; set; } = [];

    /// <inheritdoc cref="RootDimensionTypeIds"/>
    public List<string> ContainmentPairs { get; set; } = [];

    /// <summary>The dimension types being added, by name, for display only.</summary>
    [BindNever]
    public List<string> AddedLevelLabels { get; set; } = [];

    /// <summary>The dimension types being dropped, and what it costs to drop each one.</summary>
    [BindNever]
    public List<LevelRemovalImpactViewModel> RemovalImpacts { get; set; } = [];
}

/// <summary>What dropping one level would do, for display on the confirmation screen.</summary>
public sealed class LevelRemovalImpactViewModel
{
    public string Label { get; set; } = string.Empty;

    public int RecordCount { get; set; }

    public int EmployeesAffected { get; set; }
}
