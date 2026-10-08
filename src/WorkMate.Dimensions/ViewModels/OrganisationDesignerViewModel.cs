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

    /// <summary>
    /// Whether the viewer may add, rename or retire a unit — specification section 4's
    /// <c>ManageDimensionRecords</c>. Without it the chart is the same chart, drawn from the same
    /// markup, with no action menu on any card: a reader is not shown doors that are locked.
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// Whether the viewer may reparent units — <c>MoveDimensionRecords</c>. Its own permission
    /// because a move rewrites what every historical report under the unit resolves to.
    /// </summary>
    public bool CanMove { get; set; }

    /// <summary>Whether the viewer may fold one unit into another — <c>MergeDimensionRecords</c>.</summary>
    public bool CanMerge { get; set; }

    /// <summary>Whether this viewer may appoint or clear a unit's head. <c>AssignEmployees</c>.</summary>
    public bool CanAssignEmployees { get; set; }

    /// <summary>
    /// Every action a card's menu can carry, with this viewer's permission for each.
    /// </summary>
    /// <remarks>
    /// <b>The single list, and the reason there is one.</b> A card's menu used to be described in
    /// three places that had to agree: the markup in <c>_DesignerNode</c>, a <c>data-…-url</c>
    /// attribute per action on the surface, and an <c>actionUrls</c> object literal in
    /// <c>organisation-designer.js</c>. A server-rendered card got its menu from the first; a card
    /// fetched when a branch is opened got its links from the second and third, and the script
    /// <em>deletes</em> any action it has no URL for.
    ///
    /// So adding an action and forgetting either of the other two places produced a menu that was
    /// complete on the roots and silently missing an item on every card below them — no error, no
    /// console warning, and only visible to somebody who expanded a branch and looked. That is
    /// exactly what happened to <c>sethead</c> in session A2.
    ///
    /// Now the three read one list: <see cref="DesignerCardActions.All"/> names them,
    /// <see cref="PermittedActions"/> applies this viewer's permissions, and the view emits the
    /// whole map as one attribute. Adding an action is one entry here and one link in the partial,
    /// and a missing URL is impossible rather than unlikely.
    /// </remarks>
    public IReadOnlyList<DesignerCardAction> PermittedActions =>
        [.. DesignerCardActions.All.Where(action => action.IsPermitted(this))];

    /// <summary>
    /// A unit whose branch should already be open when the page loads, so that the result of an
    /// action is on screen instead of hidden inside a collapsed parent.
    /// </summary>
    /// <remarks>
    /// Opened by the script, not rendered open by the server: the branch may be several levels
    /// down and its children are fetched on demand, which is the same walk the search already
    /// does. Without script the branch is simply closed, and the unit is one click away rather
    /// than invisible.
    /// </remarks>
    public string? ExpandRecordId { get; set; }

    /// <summary>
    /// <see cref="ExpandRecordId"/>'s ancestors, root first, with the unit itself last — the chain
    /// the browser walks down to open the branch, each step fetched before the next is looked for.
    /// </summary>
    public IReadOnlyList<string> ExpandPath { get; set; } = [];

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
        StructureId = SelectedStructureId ?? string.Empty,
        AsAtIso = AsAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),

        // The structure's own card carries one action: adding a top-level unit. Without it an
        // empty structure could never be filled in from the designer at all.
        CanEdit = CanEdit,
    };

    /// <summary>
    /// The empty node the script clones for every lazily loaded child. It carries the page's
    /// context — structure, date, whether to draw an action menu — because a cloned card has to
    /// be indistinguishable from a server-rendered one, menu included.
    /// </summary>
    public DesignerNodeViewModel NodeTemplate => new()
    {
        StructureId = SelectedStructureId ?? string.Empty,
        AsAtIso = AsAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        CanEdit = CanEdit,
        CanMove = CanMove,
        CanMerge = CanMerge,

        // Every permission the partial branches on has to be here, or the template is rendered
        // without that part of the menu and every fetched card is missing it. This one was the
        // defect: CanAssignEmployees defaulted to false, so the template carried no "sethead" link
        // for the script to point anywhere — and the roots, rendered with the real value, had one.
        CanAssignEmployees = CanAssignEmployees,
    };
}
