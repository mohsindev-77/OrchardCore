namespace WorkMate.Dimensions.ViewModels;

/// <summary>The three switches on the dimensions deployment step.</summary>
public sealed class DimensionsDeploymentStepViewModel
{
    public bool IncludeTypes { get; set; } = true;

    public bool IncludeStructures { get; set; } = true;

    public bool IncludeRecords { get; set; } = true;
}
