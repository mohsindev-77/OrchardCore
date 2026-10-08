namespace WorkMate.Dimensions.ViewModels;

/// <summary>The switches on the dimensions deployment step.</summary>
public sealed class DimensionsDeploymentStepViewModel
{
    public bool IncludeTypes { get; set; } = true;

    public bool IncludeStructures { get; set; } = true;

    public bool IncludeRecords { get; set; } = true;

    /// <summary>Off by default: the organisation is configuration, the people in it are not.</summary>
    public bool IncludeAssignments { get; set; }

    /// <summary>Off by default, for the same reason, and separate because a head is not a placement.</summary>
    public bool IncludeHeads { get; set; }
}
