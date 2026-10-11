using Microsoft.Extensions.Localization;
using OrchardCore.Deployment;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Deployment;

/// <summary>
/// The three switches on the step, on the deployment plan screen.
/// </summary>
/// <remarks>
/// A <c>DisplayDriver&lt;DeploymentStep, DimensionsDeploymentStep&gt;</c> rather than a controller:
/// Orchard renders a plan's steps through the display system, so a step with a controller of its
/// own would be a second way into the same screen.
/// </remarks>
public sealed class DimensionsDeploymentStepDriver : DisplayDriver<DeploymentStep, DimensionsDeploymentStep>
{
    private readonly IStringLocalizer S;

    public DimensionsDeploymentStepDriver(IStringLocalizer<DimensionsDeploymentStepDriver> localizer) =>
        S = localizer;

    /// <summary>
    /// What to tell an operator about a combination the step cannot export.
    /// </summary>
    /// <remarks>
    /// Here rather than on the step, so the sentences are where the localisation checks look for
    /// them — a literal handed to the step would be invisible to <c>LocalisationResourceTests</c>,
    /// and its Arabic translation would quietly stop being used the next time the wording changed.
    /// Public and static so that a test can assert each problem has something to say without
    /// needing an HTTP context, a theme and a shape factory to build a display driver.
    /// </remarks>
    public static string Describe(DimensionsExportProblem problem, IStringLocalizer S)
    {
        ArgumentNullException.ThrowIfNull(S);

        return problem switch
        {
            DimensionsExportProblem.RecordsNeedTypesAndStructures =>
                S["Records reference their dimension type and their structure by code, so exporting them needs both to be included too."].Value,
            DimensionsExportProblem.AssignmentsNeedRecords =>
                S["Placements reference the unit they are at by code, so exporting them needs the units to be included too."].Value,
            DimensionsExportProblem.HeadsNeedRecords =>
                S["Head appointments reference the unit they lead by code, so exporting them needs the units to be included too."].Value,
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };
    }

    public override Task<IDisplayResult> DisplayAsync(DimensionsDeploymentStep step, BuildDisplayContext context) =>
        CombineAsync(
            View("DimensionsDeploymentStep_Fields_Summary", step).Location("Summary", "Content"),
            View("DimensionsDeploymentStep_Fields_Thumbnail", step).Location("Thumbnail", "Content"));

    public override IDisplayResult Edit(DimensionsDeploymentStep step, BuildEditorContext context) =>
        Initialize<DimensionsDeploymentStepViewModel>("DimensionsDeploymentStep_Fields_Edit", model =>
        {
            model.IncludeTypes = step.IncludeTypes;
            model.IncludeStructures = step.IncludeStructures;
            model.IncludeRecords = step.IncludeRecords;
            model.IncludeAssignments = step.IncludeAssignments;
            model.IncludeHeads = step.IncludeHeads;
        }).Location("Content");

    public override async Task<IDisplayResult> UpdateAsync(
        DimensionsDeploymentStep step, UpdateEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var model = new DimensionsDeploymentStepViewModel();

        await context.Updater.TryUpdateModelAsync(
            model, Prefix,
            m => m.IncludeTypes, m => m.IncludeStructures, m => m.IncludeRecords,
            m => m.IncludeAssignments, m => m.IncludeHeads);

        step.IncludeTypes = model.IncludeTypes;
        step.IncludeStructures = model.IncludeStructures;
        step.IncludeRecords = model.IncludeRecords;
        step.IncludeAssignments = model.IncludeAssignments;
        step.IncludeHeads = model.IncludeHeads;

        // Records name their type and their structure by code and the import resolves them against
        // what already exists, so a file carrying records without both is a file that cannot apply
        // to a tenant that does not already hold them. Refused here, where the person choosing can
        // see why, rather than at import time on somebody else's machine.
        // The rules themselves are on the step — see DimensionsDeploymentStep.Problems, which
        // explains why. All that is left here is saying what each one means and putting it beside
        // the switch it is about, so the message appears under the checkbox somebody just ticked
        // rather than at the top of a form with five of them on it.
        foreach (var (property, problem) in step.Problems())
        {
            context.Updater.ModelState.AddModelError($"{Prefix}.{property}", Describe(problem, S));
        }

        return Edit(step, context);
    }
}
