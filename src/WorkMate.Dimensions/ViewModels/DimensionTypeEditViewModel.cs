using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// The dimension type editor: shared between create and edit. <see cref="Code"/> is editable only
/// on create — the code names the backing content type and cannot change once created.
/// <see cref="IsSystemDefined"/> and <see cref="ContentTypeName"/> are never editable at all; they
/// are set by the controller from the loaded document for display and carry <see cref="BindNeverAttribute"/>
/// so a posted value for either can never reach the model, whatever the form happens to render.
/// </summary>
/// <remarks>
/// <see cref="ContentTypeName"/> is nullable as well as <c>[BindNever]</c>, not one or the other:
/// nullable is what stops it being implicitly required — a non-nullable <c>string</c> property is
/// required by nullable-reference-type inference regardless of binding source, and the Create
/// form's hidden field for it posted an empty string before this fix, which failed validation with
/// "The ContentTypeName field is required." every time, in the browser, never in a hand-built HTTP
/// test that simply omitted the field. <c>[BindNever]</c> is the other half: it is what makes the
/// property un-postable in the first place, rather than merely tolerant of being posted empty.
/// </remarks>
public sealed class DimensionTypeEditViewModel
{
    public string? DimensionTypeId { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>The two halves of the name, as posted.</summary>
    /// <remarks>
    /// Nullable because that is what the binder produces — an empty box binds to null, since
    /// <c>ConvertEmptyStringToNull</c> defaults to true — and because a non-nullable reference type
    /// property gets an implicit <c>required</c> in model state, which would refuse an empty Arabic
    /// name before the controller called anything. Whether Arabic is required is
    /// <c>IDimensionValidator</c>'s question, and only it knows this tenant's answer.
    /// </remarks>
    public string? NameEn { get; set; }

    /// <inheritdoc cref="NameEn"/>
    public string? NameAr { get; set; }

    public bool AllowsSelfNesting { get; set; }

    [BindNever]
    public bool IsSystemDefined { get; set; }

    [BindNever]
    public string? ContentTypeName { get; set; }

    public List<DimensionAttributeRowViewModel> AttributeSchema { get; set; } = [];

    public bool IsNew => string.IsNullOrEmpty(DimensionTypeId);

    /// <summary>
    /// The two halves as the value object services take.
    /// </summary>
    /// <remarks>
    /// Null-guarded for the reason spelled out on
    /// <see cref="StructureEditViewModel.Name"/>: the model binder reads this getter while it is
    /// binding, and an empty text box binds to <see langword="null"/> rather than to an empty
    /// string, so an unguarded <c>Trim</c> turns a blank name into a 500 before validation runs.
    /// </remarks>
    public BilingualText Name => new(NameEn?.Trim() ?? string.Empty, NameAr?.Trim() ?? string.Empty);

    public static DimensionTypeEditViewModel Of(DimensionTypeDocument document) => new()
    {
        DimensionTypeId = document.DimensionTypeId,
        Code = document.Code,
        NameEn = document.Name.En,
        NameAr = document.Name.Ar,
        AllowsSelfNesting = document.AllowsSelfNesting,
        IsSystemDefined = document.IsSystemDefined,
        ContentTypeName = document.ContentTypeName,
        AttributeSchema = [.. document.AttributeSchema.Select(DimensionAttributeRowViewModel.Of)],
    };

    /// <summary>
    /// The posted rows as the service's attribute schema, blank rows dropped. A blank row is how
    /// the "add a row" control leaves a spare slot the user never filled in; it is not a row with
    /// an empty name, which the service would reject.
    /// </summary>
    public IReadOnlyList<DimensionAttributeDefinition> ToAttributeSchema() =>
        [.. AttributeSchema
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => row.ToDefinition())];
}
