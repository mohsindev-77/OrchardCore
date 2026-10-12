using Microsoft.Extensions.Localization;
using OrchardCore.Deployment;

namespace WorkMate.Records.Deployment;

/// <summary>
/// The deployment step that exports this module's data as the recipe steps the importers read.
/// </summary>
/// <remarks>
/// A step of its own rather than a flag on the dimension engine's step, because the two modules own
/// different things: the dimension engine owns where people sit, this module owns who they are and
/// how their record is configured. A plan that promotes an organisation chart without its people is
/// an ordinary thing to want; so is the reverse, when the chart is already there.
///
/// Ordering is the plan's business, not this step's — but it matters, so it is worth stating twice,
/// here and on the step's own screen. <c>lookup-lists</c> is applied before anything that names a
/// lookup value, and the <c>employees</c> step is applied before <c>employee-assignments</c> and
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

    /// <summary>
    /// The managed lookup lists and their entries.
    /// </summary>
    /// <remarks>
    /// <b>On by default, unlike the operational switches on the dimension engine's step.</b> A
    /// lookup list is configuration: it is the kind of thing that travels from a configuration
    /// tenant to a production one as a matter of course, which is the distinction the A3 amendment
    /// drew when it turned <c>IncludeAssignments</c> and <c>IncludeHeads</c> off by default because
    /// they are operational data about real people. A grade list is not about anybody.
    /// </remarks>
    public bool IncludeLookupLists { get; set; } = true;
}
