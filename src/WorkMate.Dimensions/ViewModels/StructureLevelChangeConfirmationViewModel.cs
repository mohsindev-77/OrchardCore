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
