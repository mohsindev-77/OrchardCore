using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// The editor and display shape for <see cref="DimensionRecordPart"/>.
/// </summary>
/// <remarks>
/// Not sealed: Orchard builds a shape's view model through Castle DynamicProxy, which subclasses
/// it. WorkMate.Platform's README records the same constraint, found the same way.
///
/// The dates are <c>DateTime?</c> rather than <c>DateOnly</c> because the ASP.NET Core date
/// input and its model binder work in <c>DateTime</c>; the driver converts at the boundary so
/// that nothing but the view ever sees one.
/// </remarks>
public class DimensionRecordPartViewModel
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public DateTime? EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public string CostCentreCode { get; set; } = string.Empty;

    public string GlAccountRef { get; set; } = string.Empty;

    public string HeadEmployeeId { get; set; } = string.Empty;

    /// <summary>
    /// Set by the driver for display, never posted. The dimension type is fixed when the record
    /// is created, so the editor shows it rather than offering it.
    /// </summary>
    public string DimensionTypeId { get; set; } = string.Empty;

    /// <summary>The part itself, for a display template that wants more than these fields.</summary>
    public DimensionRecordPart? Part { get; set; }
}
