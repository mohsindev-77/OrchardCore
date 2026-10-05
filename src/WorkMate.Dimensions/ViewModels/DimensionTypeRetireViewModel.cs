using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// The confirmation form for retiring a dimension type. <see cref="Code"/> and
/// <see cref="NameEn"/> are shown so the confirmation message names what is being retired, but
/// <c>RetirePost</c> never reads them — only <see cref="DimensionTypeId"/> and
/// <see cref="EffectiveDate"/> are operative — so both carry <c>[BindNever]</c> and are re-set
/// from the loaded document on every render, including a redisplay after a validation failure.
/// </summary>
public sealed class DimensionTypeRetireViewModel
{
    public string DimensionTypeId { get; set; } = string.Empty;

    [BindNever]
    public string? Code { get; set; }

    [BindNever]
    public string? NameEn { get; set; }

    public DateOnly EffectiveDate { get; set; }
}
