using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.ContentManagement;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Filters;

/// <summary>
/// Sends anybody who reaches a dimension record through Orchard's generic content screens to the
/// screen that knows what a dimension record is.
/// </summary>
/// <remarks>
/// <b>Creating is closed outright</b> — <c>Migrations.UpdateFrom8Async</c> and
/// <c>DimensionTypeService</c> write <c>Creatable = false</c>, so there is no "New Department"
/// button on Orchard's own screens any more, the same treatment <c>Employee</c> already has. That
/// setting removes the button; it does not refuse the URL, so this catches the URL too.
///
/// <b>Editing cannot be closed the same way.</b> A content type that cannot be edited cannot be
/// used, and <c>Listable</c> deliberately stays on so a dimension record still appears in Orchard's
/// content list and still obeys its per-type permissions. So the generic editor is <em>redirected</em>
/// rather than refused: the reader lands on the screen that understands dated names, placements and
/// the attribute schema, with their own record already open, instead of on a form that would let
/// them change a name with no effective date and leave every historical report resolving to the new
/// one.
///
/// <b>Why a filter and not a route.</b> The routes belong to <c>OrchardCore.Contents</c>, which this
/// module does not reference and must not: the coupling would be to a module that may be absent on a
/// tenant. A filter keys on the controller's type name and the route values, both of which are
/// strings, so this works when that module is present and costs one type-name comparison per admin
/// request when it is not.
/// </remarks>
internal sealed class DimensionRecordAdminRedirectFilter : IAsyncActionFilter
{
    private readonly IDimensionTypeLookup _types;
    private readonly IContentManager _contentManager;
    private readonly INotifier _notifier;
    private readonly IHtmlLocalizer H;

    public DimensionRecordAdminRedirectFilter(
        IDimensionTypeLookup types,
        IContentManager contentManager,
        INotifier notifier,
        IHtmlLocalizer<DimensionRecordAdminRedirectFilter> htmlLocalizer)
    {
        _types = types;
        _contentManager = contentManager;
        _notifier = notifier;
        H = htmlLocalizer;
    }

    /// <summary>The controller whose screens this intercepts, by name rather than by reference.</summary>
    private const string ContentsAdminController = "OrchardCore.Contents.Controllers.AdminController";

    /// <summary>
    /// The actions that open the generic create form. <c>id</c> is the content type's name.
    /// </summary>
    private static readonly string[] CreateActions =
        ["Create", "CreatePOST", "CreateAndPublishPOST"];

    /// <summary>
    /// The actions that open or save the generic editor. <c>contentItemId</c> names the item.
    /// </summary>
    private static readonly string[] EditActions =
        ["Edit", "EditPOST", "EditAndPublishPOST", "EditAndUnpublishPOST"];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (await RedirectForAsync(context) is { } redirect)
        {
            context.Result = redirect;

            return;
        }

        await next();
    }

    /// <summary>Where this request should go instead, or null to let it run.</summary>
    private async Task<IActionResult?> RedirectForAsync(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor ||
            !string.Equals(descriptor.ControllerTypeInfo.FullName, ContentsAdminController, StringComparison.Ordinal))
        {
            return null;
        }

        var action = descriptor.ActionName;

        if (CreateActions.Contains(action, StringComparer.Ordinal))
        {
            var contentType = context.RouteData.Values["id"]?.ToString();

            return await _types.GetByContentTypeAsync(contentType ?? string.Empty) is { } type
                ? await CreateInsteadAsync(type.ContentTypeName, type.DimensionTypeId)
                : null;
        }

        if (!EditActions.Contains(action, StringComparer.Ordinal))
        {
            return null;
        }

        var contentItemId = context.RouteData.Values["contentItemId"]?.ToString();

        if (string.IsNullOrEmpty(contentItemId))
        {
            return null;
        }

        // Loaded only on this route and only for this handful of actions, so the cost is paid by
        // somebody opening the generic editor and by nobody else.
        var item = await _contentManager.GetAsync(contentItemId);

        if (item is null || await _types.GetByContentTypeAsync(item.ContentType) is null)
        {
            return null;
        }

        await _notifier.InformationAsync(H[
            "'{0}' is part of the organisation, so it is edited here rather than on the generic content screen — a name is dated, and where it sits is a placement.",
            item.DisplayText ?? item.ContentType]);

        return RedirectTo("Edit", new { area = Area, id = contentItemId });
    }

    /// <summary>
    /// The create screen for this type, with a note saying why the reader is not where they asked
    /// to be.
    /// </summary>
    /// <remarks>
    /// A reader who typed the URL deserves an explanation rather than a screen that is simply
    /// somewhere else. The reason is the whole argument for closing this route: the generic form
    /// creates a content item and nothing else, so the record would be on no structure, under no
    /// parent, and invisible to the closure index and every roll-up built on it.
    /// </remarks>
    private async Task<IActionResult> CreateInsteadAsync(string contentTypeName, string dimensionTypeId)
    {
        await _notifier.InformationAsync(H[
            "A '{0}' is created here rather than on the generic content screen, because it needs a structure and a place on it. One created the other way would be on no chart at all.",
            contentTypeName]);

        return RedirectTo("Create", new { area = Area, dimensionTypeId });
    }

    private const string Area = "WorkMate.Dimensions";

    private static RedirectToActionResult RedirectTo(string action, object routeValues) =>
        new(action, "DimensionRecordsAdmin", routeValues);
}
