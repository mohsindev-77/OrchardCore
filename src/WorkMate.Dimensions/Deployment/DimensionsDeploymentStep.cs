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

    /// <summary>
    /// Where people work: every placement of every employee on every axis, over all time.
    /// </summary>
    /// <remarks>
    /// Off by default, unlike the three above. The organisation is configuration and travels from
    /// one environment to the next as a matter of course; who works in it is operational data, and
    /// carrying it by accident into a tenant that has its own people is a worse outcome than
    /// having to tick a box.
    /// </remarks>
    public bool IncludeAssignments { get; set; }

    /// <summary>
    /// Who leads each unit, with every term on record.
    /// </summary>
    /// <remarks>
    /// Its own switch rather than part of <see cref="IncludeAssignments"/>, because a head is not a
    /// placement — ADR-0012. A tenant can have appointments and no placements or the reverse, and
    /// one switch covering both would make the export claim otherwise. They share a requirement
    /// rather than a meaning: both name people by code, so both need the employees to exist.
    /// </remarks>
    public bool IncludeHeads { get; set; }

    /// <summary>
    /// The combinations this step cannot produce a usable file from, and why.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than in the driver.</b> These are rules about what an export means, not about
    /// how a screen is drawn, and a display driver can only be exercised with an HTTP context, a
    /// theme and a shape factory around it — which is why they had no tests at all until the gap was
    /// noticed. As a function of the step they are a function anybody can call, and the driver's job
    /// shrinks to putting what it returns into model state.
    ///
    /// Every one of them is a reference the import resolves by code: a record names its type and its
    /// structure, a placement names its unit, a head names its unit. Carrying the referrer without
    /// the referent produces a file that cannot apply to a tenant that does not already hold it, so
    /// it is refused where the person choosing can see why rather than on arrival somewhere else.
    /// </remarks>
    /// <remarks>
    /// <b>Returns what is wrong, not what to say about it.</b> The sentences stay in the driver,
    /// where <c>LocalisationResourceTests</c> can see them: a message built here would be a literal
    /// string passed through a delegate, invisible to the source-level localisation checks, and the
    /// day somebody edited the wording its Arabic translation would silently stop being used. The
    /// rule and the sentence are different things and they belong in different places.
    /// </remarks>
    /// <returns>One entry per problem: the switch it belongs beside, and which problem it is.</returns>
    public IReadOnlyList<(string Property, DimensionsExportProblem Problem)> Problems()
    {
        var problems = new List<(string, DimensionsExportProblem)>();

        if (IncludeRecords && (!IncludeTypes || !IncludeStructures))
        {
            problems.Add((nameof(IncludeRecords), DimensionsExportProblem.RecordsNeedTypesAndStructures));
        }

        if (IncludeAssignments && !IncludeRecords)
        {
            problems.Add((nameof(IncludeAssignments), DimensionsExportProblem.AssignmentsNeedRecords));
        }

        if (IncludeHeads && !IncludeRecords)
        {
            problems.Add((nameof(IncludeHeads), DimensionsExportProblem.HeadsNeedRecords));
        }

        return problems;
    }
}

/// <summary>
/// A combination of switches that would produce an export nothing could import.
/// </summary>
/// <remarks>
/// Each is a reference the import resolves by code. The wording lives on
/// <see cref="DimensionsDeploymentStepDriver"/>; this is the fact, and that is how it is put.
/// </remarks>
public enum DimensionsExportProblem
{
    /// <summary>A record names its dimension type and its structure.</summary>
    RecordsNeedTypesAndStructures,

    /// <summary>A placement names the unit it is at.</summary>
    AssignmentsNeedRecords,

    /// <summary>A head names the unit they lead.</summary>
    HeadsNeedRecords,
}
