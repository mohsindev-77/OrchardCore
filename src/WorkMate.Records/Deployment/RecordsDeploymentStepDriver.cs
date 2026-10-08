using OrchardCore.Deployment;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Deployment;

/// <summary>
/// The switch on the step, on the deployment plan screen.
/// </summary>
/// <remarks>
/// A <c>DisplayDriver&lt;DeploymentStep, RecordsDeploymentStep&gt;</c> rather than a controller:
/// Orchard renders a plan's steps through the display system, so a step with a controller of its
/// own would be a second way into the same screen.
/// </remarks>
public sealed class RecordsDeploymentStepDriver : DisplayDriver<DeploymentStep, RecordsDeploymentStep>
{
    public override Task<IDisplayResult> DisplayAsync(RecordsDeploymentStep step, BuildDisplayContext context) =>
        CombineAsync(
            View("RecordsDeploymentStep_Fields_Summary", step).Location("Summary", "Content"),
            View("RecordsDeploymentStep_Fields_Thumbnail", step).Location("Thumbnail", "Content"));

    public override IDisplayResult Edit(RecordsDeploymentStep step, BuildEditorContext context) =>
        Initialize<RecordsDeploymentStepViewModel>("RecordsDeploymentStep_Fields_Edit", model =>
        {
            model.IncludeEmployees = step.IncludeEmployees;
        }).Location("Content");

    public override async Task<IDisplayResult> UpdateAsync(
        RecordsDeploymentStep step, UpdateEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var model = new RecordsDeploymentStepViewModel();

        await context.Updater.TryUpdateModelAsync(model, Prefix, m => m.IncludeEmployees);

        step.IncludeEmployees = model.IncludeEmployees;

        return Edit(step, context);
    }
}
