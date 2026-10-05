using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>One node of the organisation designer's tree, or one row of its unplaced panel.</summary>
/// <remarks>
/// The same shape serves the chart and the list: both views render the same nested markup and
/// differ only in the stylesheet applied to it, so that switching views is a class swap rather
/// than a second rendering path that can drift from the first.
/// </remarks>
public sealed class DesignerNodeViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string DimensionTypeId { get; set; } = string.Empty;

    public string DimensionTypeNameEn { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    /// <summary>
    /// How many employees sit at this unit on the date being shown, or null when nobody does.
    /// </summary>
    /// <remarks>
    /// Null rather than zero so the card can leave the line out entirely: on a tenant with no
    /// employee records yet — which is every tenant until <c>WorkMate.Records</c> ships — a card
    /// reading "0 employees" on every unit is noise, not information.
    /// </remarks>
    public int? EmployeeCount { get; set; }

    /// <summary>
    /// The unit's head, once employees exist. Always null today, and rendered as an empty line on
    /// the card so the layout does not move when it starts being filled.
    /// </summary>
    public string? HeadDisplayName { get; set; }

    public static DesignerNodeViewModel Of(
        DimensionNodeRef node,
        IReadOnlyDictionary<string, DimensionTypeDocument> typesById,
        IReadOnlyDictionary<string, int>? employeeCounts = null) => new()
    {
        RecordId = node.RecordId,
        Code = node.Code,
        NameEn = node.NameEn,
        NameAr = node.NameAr,
        DimensionTypeId = node.DimensionTypeId,
        DimensionTypeNameEn = typesById.TryGetValue(node.DimensionTypeId, out var type)
            ? type.Name.En
            : node.DimensionTypeId,
        IsActive = node.IsActive,
        EmployeeCount = employeeCounts is not null && employeeCounts.TryGetValue(node.RecordId, out var count)
            ? count
            : null,
    };
}

/// <summary>One search hit, with the path the designer's tree must expand to reveal it.</summary>
/// <param name="AncestorRecordIds">The hit's ancestors, root first, excluding the hit itself.</param>
public sealed record DesignerSearchHitViewModel(
    string RecordId,
    string Code,
    string NameEn,
    string NameAr,
    string DimensionTypeNameEn,
    IReadOnlyList<string> AncestorRecordIds);
