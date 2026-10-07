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
        }).Location("Content");

    public override async Task<IDisplayResult> UpdateAsync(
        DimensionsDeploymentStep step, UpdateEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var model = new DimensionsDeploymentStepViewModel();

        await context.Updater.TryUpdateModelAsync(
            model, Prefix, m => m.IncludeTypes, m => m.IncludeStructures, m => m.IncludeRecords);

        step.IncludeTypes = model.IncludeTypes;
        step.IncludeStructures = model.IncludeStructures;
        step.IncludeRecords = model.IncludeRecords;

        // Records name their type and their structure by code and the import resolves them against
        // what already exists, so a file carrying records without both is a file that cannot apply
        // to a tenant that does not already hold them. Refused here, where the person choosing can
        // see why, rather than at import time on somebody else's machine.
        if (step.IncludeRecords && (!step.IncludeTypes || !step.IncludeStructures))
        {
            context.Updater.ModelState.AddModelError(
                $"{Prefix}.{nameof(DimensionsDeploymentStepViewModel.IncludeRecords)}",
                S["Records reference their dimension type and their structure by code, so exporting them needs both to be included too."].Value);
        }

        return Edit(step, context);
    }
}
