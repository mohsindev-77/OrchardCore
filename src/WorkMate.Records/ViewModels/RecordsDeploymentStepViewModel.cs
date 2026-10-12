namespace WorkMate.Records.ViewModels;

/// <summary>The switches on the employee records deployment step.</summary>
public sealed class RecordsDeploymentStepViewModel
{
    public bool IncludeEmployees { get; set; } = true;

    /// <summary>
    /// The managed lookup lists. On by default because they are configuration — see
    /// <c>RecordsDeploymentStep.IncludeLookupLists</c>.
    /// </summary>
    public bool IncludeLookupLists { get; set; } = true;
}
