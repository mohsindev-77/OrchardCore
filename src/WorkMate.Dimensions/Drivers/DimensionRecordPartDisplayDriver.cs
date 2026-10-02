using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.ContentManagement.Display.Models;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.DisplayManagement.Views;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Drivers;

/// <summary>
/// Shapes a dimension record for display and for editing.
/// </summary>
/// <remarks>
/// A driver shapes data and nothing else. Every rule about whether these values are allowed —
/// the code's format, its uniqueness, the dates — is enforced in the handler and the services,
/// so that a record arriving through the API, a recipe or an import is held to the same
/// standard as one typed into this editor. CLAUDE.md rule 1.
///
/// The dimension type is shown, not offered: it is fixed when the record is created, because
/// the content type the record lives in is the dimension type. Changing it would mean moving the
/// item between content types, which is a different operation entirely.
/// </remarks>
public sealed class DimensionRecordPartDisplayDriver : ContentPartDisplayDriver<DimensionRecordPart>
{
    public override IDisplayResult Display(DimensionRecordPart part, BuildPartDisplayContext context)
    {
        ArgumentNullException.ThrowIfNull(part);

        return Initialize<DimensionRecordPartViewModel>(
                GetDisplayShapeType(context),
                model => Bind(model, part))
            .Location("Detail", "Content:5")
            .Location("Summary", "Meta:5");
    }

    public override IDisplayResult Edit(DimensionRecordPart part, BuildPartEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(part);

        return Initialize<DimensionRecordPartViewModel>(
            GetEditorShapeType(context),
            model => Bind(model, part));
    }

    public override async Task<IDisplayResult> UpdateAsync(
        DimensionRecordPart part,
        UpdatePartEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(context);

        var model = new DimensionRecordPartViewModel();

        await context.Updater.TryUpdateModelAsync(model, Prefix);

        part.Code = model.Code?.Trim() ?? string.Empty;
        part.NameEn = model.NameEn?.Trim() ?? string.Empty;
        part.NameAr = model.NameAr?.Trim() ?? string.Empty;
        part.IsActive = model.IsActive;
        part.SortOrder = model.SortOrder;
        part.CostCentreCode = model.CostCentreCode?.Trim() ?? string.Empty;
        part.GlAccountRef = model.GlAccountRef?.Trim() ?? string.Empty;
        part.HeadEmployeeId = model.HeadEmployeeId?.Trim() ?? string.Empty;

        // An unset effective-from on an existing record keeps what it had; on a new one the
        // handler rejects it rather than defaulting, because specification section 2 rule 3
        // forbids defaulting a date on a write.
        if (model.EffectiveFrom is { } from)
        {
            part.EffectiveFrom = DateOnly.FromDateTime(from);
        }

        part.EffectiveTo = model.EffectiveTo is { } to ? DateOnly.FromDateTime(to) : null;

        return Edit(part, context);
    }

    private static void Bind(DimensionRecordPartViewModel model, DimensionRecordPart part)
    {
        model.Code = part.Code;
        model.NameEn = part.NameEn;
        model.NameAr = part.NameAr;
        model.DimensionTypeId = part.DimensionTypeId;
        model.IsActive = part.IsActive;
        model.SortOrder = part.SortOrder;
        model.CostCentreCode = part.CostCentreCode;
        model.GlAccountRef = part.GlAccountRef;
        model.HeadEmployeeId = part.HeadEmployeeId;
        model.Part = part;

        // default(DateOnly) is 0001-01-01, which a date input renders as a nonsense value. A new
        // record has no effective date until someone sets one, and the editor should say so by
        // being empty.
        model.EffectiveFrom = part.EffectiveFrom == default
            ? null
            : part.EffectiveFrom.ToDateTime(TimeOnly.MinValue);

        model.EffectiveTo = part.EffectiveTo?.ToDateTime(TimeOnly.MinValue);
    }
}
