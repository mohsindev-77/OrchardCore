using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// One row of the attribute schema builder on the dimension type editor.
/// </summary>
/// <remarks>
/// A flat, mutable shape rather than <see cref="DimensionAttributeDefinition"/> itself, because a
/// posted form gives model binding a list of these to fill before anything is validated, and the
/// bilingual label needs two separate bound properties the way every other bilingual field on this
/// platform does.
/// </remarks>
public sealed class DimensionAttributeRowViewModel
{
    public string Name { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;

    public string LabelAr { get; set; } = string.Empty;

    public DimensionAttributeKind Kind { get; set; }

    public bool IsRequired { get; set; }

    public static DimensionAttributeRowViewModel Of(DimensionAttributeDefinition attribute) => new()
    {
        Name = attribute.Name,
        LabelEn = attribute.Label.En,
        LabelAr = attribute.Label.Ar,
        Kind = attribute.Kind,
        IsRequired = attribute.IsRequired,
    };

    public DimensionAttributeDefinition ToDefinition() =>
        new(Name.Trim(), new BilingualText(LabelEn.Trim(), LabelAr.Trim()), Kind, IsRequired);
}
