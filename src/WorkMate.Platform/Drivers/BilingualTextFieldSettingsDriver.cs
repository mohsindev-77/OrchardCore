using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentTypes.Editors;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using WorkMate.Platform.Fields;
using WorkMate.Platform.ViewModels;

namespace WorkMate.Platform.Drivers;

/// <summary>
/// The settings an administrator sets when attaching a bilingual field to a content type.
/// </summary>
public sealed class BilingualTextFieldSettingsDriver : ContentPartFieldDefinitionDisplayDriver<BilingualTextField>
{
    public override IDisplayResult Edit(ContentPartFieldDefinition partFieldDefinition, BuildEditorContext context) =>
        Initialize<BilingualTextFieldSettingsViewModel>(
            "BilingualTextFieldSettings_Edit",
            model =>
            {
                var settings = partFieldDefinition.GetSettings<BilingualTextFieldSettings>();

                model.Hint = settings.Hint;
                model.HintAr = settings.HintAr;
                model.RequireEnglish = settings.RequireEnglish;
                model.RequireArabic = settings.RequireArabic;
                model.MaxLength = settings.MaxLength;
            })
            // Without an explicit location the shape is built but never placed, so the settings
            // editor renders without it and the field looks as though it has no settings.
            .Location("Content");

    public override async Task<IDisplayResult> UpdateAsync(
        ContentPartFieldDefinition partFieldDefinition,
        UpdatePartFieldEditorContext context)
    {
        var model = new BilingualTextFieldSettingsViewModel();

        await context.Updater.TryUpdateModelAsync(model, Prefix);

        context.Builder.WithSettings(new BilingualTextFieldSettings
        {
            Hint = (model.Hint ?? string.Empty).Trim(),
            HintAr = (model.HintAr ?? string.Empty).Trim(),
            RequireEnglish = model.RequireEnglish,
            RequireArabic = model.RequireArabic,
            MaxLength = Math.Max(0, model.MaxLength),
        });

        return Edit(partFieldDefinition, context);
    }
}
