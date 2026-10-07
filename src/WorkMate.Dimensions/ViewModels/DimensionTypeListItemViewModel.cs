using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>One row of the dimension type list.</summary>
public sealed class DimensionTypeListItemViewModel
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool IsSystemDefined { get; set; }

    public string ContentTypeName { get; set; } = string.Empty;

    public int AttributeCount { get; set; }

    public DateOnly? RetiredOn { get; set; }

    public static DimensionTypeListItemViewModel Of(DimensionTypeDocument document) => new()
    {
        DimensionTypeId = document.DimensionTypeId,
        Code = document.Code,
        NameEn = document.Name.En,
        NameAr = document.Name.Ar,
        IsSystemDefined = document.IsSystemDefined,
        ContentTypeName = document.ContentTypeName,
        AttributeCount = document.AttributeSchema.Count,
        RetiredOn = document.RetiredOn,
    };
}
