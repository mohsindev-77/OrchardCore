using Microsoft.Extensions.Localization;
using OrchardCore.Deployment;

namespace WorkMate.Records.Deployment;

/// <summary>
/// The deployment step that exports this tenant's employees as the recipe step the importer reads.
/// </summary>
/// <remarks>
/// One switch today, and a step of its own rather than a flag on the dimension engine's step,
/// because the two modules own different things: the dimension engine owns where people sit, this
/// module owns who they are. A plan that promotes an organisation chart without its people is an
/// ordinary thing to want; so is the reverse, when the chart is already there.
///
/// Ordering is the plan's business, not this step's — but it matters, so it is worth stating: the
/// <c>employees</c> step this produces has to be applied before <c>employee-assignments</c> and
/// <c>unit-heads</c>, which name people by code and resolve them against what already exists.
///
/// See ADR-0011 for why this is a deployment step rather than a controller action or a CLI command.
/// </remarks>
public sealed class RecordsDeploymentStep : DeploymentStep
{
    public RecordsDeploymentStep() => Name = nameof(RecordsDeploymentStep);

    public RecordsDeploymentStep(IStringLocalizer<RecordsDeploymentStep> localizer)
        : this() => Category = localizer["Content"];

    /// <summary>
    /// The employees: code, both halves of the name, join date, the status they are in and the
    /// date it took effect, and the personal fields the record carries.
    /// </summary>
    public bool IncludeEmployees { get; set; } = true;
}
