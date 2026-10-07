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

    /// <summary>The two halves of the label, as posted. Nullable for the reason given on
    /// <see cref="DimensionTypeEditViewModel.NameEn"/>: an empty box binds to null, and an implicit
    /// required on the Arabic half would refuse a label the tenant has not asked for.</summary>
    public string? LabelEn { get; set; }

    /// <inheritdoc cref="LabelEn"/>
    public string? LabelAr { get; set; }

    public DimensionAttributeKind Kind { get; set; }

    public bool IsRequired { get; set; }

    /// <remarks>
    /// The stored halves may be null as well as empty — a label whose Arabic was never filled in
    /// is one or the other depending on how it was written — and a view model holding null where
    /// it promises a string is the shape that produced six 500s. Normalised on the way in.
    /// </remarks>
    public static DimensionAttributeRowViewModel Of(DimensionAttributeDefinition attribute) => new()
    {
        Name = attribute.Name ?? string.Empty,
        LabelEn = attribute.Label?.En ?? string.Empty,
        LabelAr = attribute.Label?.Ar ?? string.Empty,
        Kind = attribute.Kind,
        IsRequired = attribute.IsRequired,
    };

    /// <remarks>
    /// Null-guarded on the way out for the same reason: an empty box binds to null, and an Arabic
    /// label may legitimately be left empty since ADR-0003's addendum.
    /// </remarks>
    public DimensionAttributeDefinition ToDefinition() =>
        new(
            Name?.Trim() ?? string.Empty,
            new BilingualText(LabelEn?.Trim() ?? string.Empty, LabelAr?.Trim() ?? string.Empty),
            Kind,
            IsRequired);
}
