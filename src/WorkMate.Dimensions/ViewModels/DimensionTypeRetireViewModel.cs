namespace WorkMate.Dimensions.ViewModels;

/// <summary>The confirmation form for retiring a dimension type.</summary>
public sealed class DimensionTypeRetireViewModel
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public DateOnly EffectiveDate { get; set; }
}
