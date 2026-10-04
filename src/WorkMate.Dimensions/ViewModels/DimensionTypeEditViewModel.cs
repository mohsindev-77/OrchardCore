using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// The dimension type editor: shared between create and edit. <see cref="Code"/> and
/// <see cref="IsSystemDefined"/> are shown but never posted back as editable on an existing type —
/// the code names the backing content type and cannot change once created, and a system-defined
/// type is something only a recipe declares, never this screen.
/// </summary>
public sealed class DimensionTypeEditViewModel
{
    public string? DimensionTypeId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool AllowsSelfNesting { get; set; }

    public bool IsSystemDefined { get; set; }

    public string ContentTypeName { get; set; } = string.Empty;

    public List<DimensionAttributeRowViewModel> AttributeSchema { get; set; } = [];

    public bool IsNew => string.IsNullOrEmpty(DimensionTypeId);

    public BilingualText Name => new(NameEn.Trim(), NameAr.Trim());

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
