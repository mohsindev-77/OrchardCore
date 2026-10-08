namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// One entry on a designer card's action menu: what the markup calls it, where it goes, and who may
/// use it.
/// </summary>
/// <param name="Name">
/// The <c>data-designer-action</c> value. The one string the markup, the URL map and the script all
/// agree on, which is what makes a card fetched from the server indistinguishable from one rendered
/// by it.
/// </param>
/// <param name="Action">The controller action.</param>
/// <param name="Controller">
/// The controller, when it is not the designer's own. <c>sethead</c> lives on
/// <c>UnitHeadAdminController</c>, which is the first action that did — and discovering that the
/// map had no way to express it is how the missing-URL defect was found.
/// </param>
/// <param name="TargetsParent">
/// Whether the unit's id goes on the query string as <c>parentId</c> rather than <c>recordId</c>.
/// True for <c>add</c> alone: adding a unit puts the new one <em>under</em> the card it was started
/// from, where every other action is about the card itself.
/// </param>
/// <param name="IsPermitted">Whether this viewer may use it, given their permissions.</param>
public sealed record DesignerCardAction(
    string Name,
    string Action,
    string? Controller,
    bool TargetsParent,
    Func<OrganisationDesignerViewModel, bool> IsPermitted);

/// <summary>
/// Every action a designer card can offer, declared once.
/// </summary>
/// <remarks>
/// Read by three things that previously each held their own copy and had to be kept in step by
/// hand: the surface's URL map, the card partial's menu, and the script that points a fetched
/// card's links at the right record. <see cref="OrganisationDesignerViewModel.PermittedActions"/>
/// explains the defect that cost.
///
/// The order is the order a menu shows them, which is roughly how often they are used, with the
/// one that closes a unit last.
/// </remarks>
public static class DesignerCardActions
{
    public static readonly IReadOnlyList<DesignerCardAction> All =
    [
        new("add", "AddUnit", Controller: null, TargetsParent: true, model => model.CanEdit),
        new("rename", "Rename", Controller: null, TargetsParent: false, model => model.CanEdit),
        new("move", "Move", Controller: null, TargetsParent: false, model => model.CanMove),
        new("cancelmove", "CancelMove", Controller: null, TargetsParent: false, model => model.CanMove),
        new("merge", "Merge", Controller: null, TargetsParent: false, model => model.CanMerge),
        new("sethead", "Set", "UnitHeadAdmin", TargetsParent: false, model => model.CanAssignEmployees),
        new("retire", "Retire", Controller: null, TargetsParent: false, model => model.CanEdit),
    ];
}
