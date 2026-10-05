namespace WorkMate.Dimensions.ViewModels;

/// <summary>Which of the designer's two views of the same tree is showing.</summary>
public enum DesignerViewMode
{
    /// <summary>A top-down org chart of cards. The default.</summary>
    Chart,

    /// <summary>
    /// The indented tree. Kept for a large organisation, where a chart is unreadable, and for
    /// keyboard use, where an indented list is what a screen reader can navigate.
    /// </summary>
    List,
}

/// <summary>
/// The organisation designer's page: a structure picker, the date the tree is shown as at, the
/// tree's roots (children load lazily from the browser) and the unplaced-records panel.
/// </summary>
public sealed class OrganisationDesignerViewModel
{
    /// <summary>The cookie the chosen view is remembered in, so it survives a reload and a restart.</summary>
    public const string ViewModeCookieName = "workmate_designer_view";

    public IReadOnlyList<StructureListItemViewModel> Structures { get; set; } = [];

    public string? SelectedStructureId { get; set; }

    public string SelectedStructureCode { get; set; } = string.Empty;

    public string SelectedStructureNameEn { get; set; } = string.Empty;

    public string SelectedStructureNameAr { get; set; } = string.Empty;

    /// <summary>The date the tree is resolved as at. Today unless the viewer asked for another.</summary>
    public DateOnly AsAt { get; set; }

    public DateOnly Today { get; set; }

    /// <summary>Whether <see cref="AsAt"/> is today, which is what the "as at" notice keys off.</summary>
    public bool IsAsAtToday => AsAt == Today;

    /// <summary>
    /// Whether the viewer may resolve the axis as at a past date at all — specification section
    /// 4's <c>ViewDimensionHistory</c>. The date control is not rendered without it, and the
    /// controller refuses a date other than today for the same caller.
    /// </summary>
    public bool CanViewHistory { get; set; }

    public DesignerViewMode ViewMode { get; set; } = DesignerViewMode.Chart;

    public List<DesignerNodeViewModel> Roots { get; set; } = [];

    public List<DesignerNodeViewModel> Unplaced { get; set; } = [];

    /// <summary>
    /// The structure as the chart's top card, with <see cref="Roots"/> drawn beneath it.
    /// </summary>
    /// <remarks>
    /// A chart that starts at the roots has nothing holding it together: two divisions appear side
    /// by side with connectors leading up to nothing. The structure is what they are both part of,
    /// so it is what the connectors lead to.
    /// </remarks>
    public DesignerNodeViewModel StructureCard => new()
    {
        IsStructure = true,
        NameEn = SelectedStructureNameEn,
        NameAr = SelectedStructureNameAr,
        Code = SelectedStructureCode,
        ChildCount = Roots.Count,
        Children = Roots,
    };
}
