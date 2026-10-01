using Microsoft.Extensions.Localization;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.ContentManagement.Display.Models;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.DisplayManagement.Views;
using WorkMate.Platform.Fields;
using WorkMate.Platform.ViewModels;

namespace WorkMate.Platform.Drivers;

/// <summary>
/// Edit and display for <see cref="BilingualTextField"/>. Validation lives here rather than in a
/// service because it is field-shape validation — required and length — with no aggregate behind
/// it; anything that depends on other data belongs in the owning module's service.
/// </summary>
public sealed class BilingualTextFieldDisplayDriver : ContentFieldDisplayDriver<BilingualTextField>
{
    private readonly IStringLocalizer S;

    public BilingualTextFieldDisplayDriver(IStringLocalizer<BilingualTextFieldDisplayDriver> stringLocalizer) =>
        S = stringLocalizer;

    public override IDisplayResult Display(BilingualTextField field, BuildFieldDisplayContext fieldDisplayContext) =>
        Initialize<DisplayBilingualTextFieldViewModel>(GetDisplayShapeType(fieldDisplayContext), model =>
            {
                model.En = field.En;
                model.Ar = field.Ar;
                model.Field = field;
                model.PartFieldDefinition = fieldDisplayContext.PartFieldDefinition;
            })
            .Location("Detail", "Content")
            .Location("Summary", "Content");

    public override IDisplayResult Edit(BilingualTextField field, BuildFieldEditorContext context) =>
        Initialize<EditBilingualTextFieldViewModel>(GetEditorShapeType(context), model =>
        {
            model.En = field.En;
            model.Ar = field.Ar;
            model.Field = field;
            model.PartFieldDefinition = context.PartFieldDefinition;
            model.Settings = context.PartFieldDefinition.GetSettings<BilingualTextFieldSettings>();
        });

    public override async Task<IDisplayResult> UpdateAsync(
        BilingualTextField field,
        UpdateFieldEditorContext context)
    {
        var model = new EditBilingualTextFieldViewModel();

        await context.Updater.TryUpdateModelAsync(model, Prefix, m => m.En, m => m.Ar);

        field.En = (model.En ?? string.Empty).Trim();
        field.Ar = (model.Ar ?? string.Empty).Trim();

        var settings = context.PartFieldDefinition.GetSettings<BilingualTextFieldSettings>();
        var label = context.PartFieldDefinition.DisplayName();

        if (settings.RequireEnglish && string.IsNullOrWhiteSpace(field.En))
        {
            context.Updater.ModelState.AddModelError(
                $"{Prefix}.{nameof(EditBilingualTextFieldViewModel.En)}",
                S["{0} is required in English.", label]);
        }

        if (settings.RequireArabic && string.IsNullOrWhiteSpace(field.Ar))
        {
            context.Updater.ModelState.AddModelError(
                $"{Prefix}.{nameof(EditBilingualTextFieldViewModel.Ar)}",
                S["{0} is required in Arabic.", label]);
        }

        if (settings.MaxLength > 0)
        {
            if (field.En.Length > settings.MaxLength)
            {
                context.Updater.ModelState.AddModelError(
                    $"{Prefix}.{nameof(EditBilingualTextFieldViewModel.En)}",
                    S["{0} in English must be {1} characters or fewer.", label, settings.MaxLength]);
            }

            if (field.Ar.Length > settings.MaxLength)
            {
                context.Updater.ModelState.AddModelError(
                    $"{Prefix}.{nameof(EditBilingualTextFieldViewModel.Ar)}",
                    S["{0} in Arabic must be {1} characters or fewer.", label, settings.MaxLength]);
            }
        }

        return Edit(field, context);
    }
}
