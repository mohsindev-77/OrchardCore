using Microsoft.Extensions.Localization;
using OrchardCore.Deployment;

namespace WorkMate.Dimensions.Deployment;

/// <summary>
/// The deployment step that exports this tenant's dimension configuration and data as the three
/// recipe steps the importers already read.
/// </summary>
/// <remarks>
/// One step with three switches rather than three steps, because the three are not independent:
/// a structure names its types and a record names both, so exporting records without types gives
/// a recipe that cannot apply. The switches exist for the case where a tenant's types and
/// structures are the thing being promoted and its records are not — a configuration change moving
/// from test to production, with the production organisation left alone — and the source refuses
/// the combinations that could not import, rather than producing a file that fails on arrival.
///
/// See ADR-0011 for why this is a deployment step rather than a controller action or a CLI command.
/// </remarks>
public sealed class DimensionsDeploymentStep : DeploymentStep
{
    public DimensionsDeploymentStep() => Name = nameof(DimensionsDeploymentStep);

    public DimensionsDeploymentStep(IStringLocalizer<DimensionsDeploymentStep> localizer)
        : this() => Category = localizer["Content"];

    /// <summary>The dimension types, with their attribute schemas.</summary>
    public bool IncludeTypes { get; set; } = true;

    /// <summary>The structures: their types in order, root types, containment map and flags.</summary>
    public bool IncludeStructures { get; set; } = true;

    /// <summary>
    /// The records, with their full dated placement history and any dated name history.
    /// </summary>
    public bool IncludeRecords { get; set; } = true;
}
