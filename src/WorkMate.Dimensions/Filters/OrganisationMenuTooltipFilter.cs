using Microsoft.AspNetCore.Mvc.Filters;
using OrchardCore.Admin;
using OrchardCore.ResourceManagement;

namespace WorkMate.Dimensions.Filters;

/// <summary>
/// Puts the Organisation menu's tooltip script on every admin page. Decision D24.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> Since D23 the Organisation menu expands to each dimension's own
/// values, and a value's caption can carry a parent and an organisation after it — <em>Credit –
/// Lahore Main Branch (Crescent)</em>. The sidebar is narrow, the theme truncates with an ellipsis,
/// and the part that gets cut is the part that disambiguates. D24 asks for the full text as a
/// tooltip.
///
/// <b>Why it is not markup.</b> Orchard Core 3.0.1's <c>MenuItem</c> has nowhere to put one —
/// verified against the pinned assemblies with <c>tools/api-probe</c>: <c>Text</c>, <c>Href</c>,
/// <c>Id</c>, <c>Classes</c>, <c>Permissions</c>, <c>RouteValues</c>, and no title and no attribute
/// bag. The anchor is rendered by the admin theme's own templates, which are compiled into
/// <c>TheAdmin.dll</c>; a module cannot override a theme's shape, and editing Orchard is what
/// CLAUDE.md forbids outright. Decorating the rendered page from a script this module ships is an
/// extension of Orchard rather than a change to it.
///
/// <b>Why a filter rather than a <c>&lt;script&gt;</c> tag in a view.</b> The menu is on every admin
/// page, including every screen owned by every other module, and this module has no view for any of
/// them. A result filter that requires the resource is the narrowest hook that reaches them all,
/// and it is the same mechanism <see cref="DimensionRecordAdminRedirectFilter"/> uses for the same
/// reason: the thing being reached belongs to somebody else.
///
/// <b>Admin pages only, and nothing on a non-admin request.</b> <c>AdminAttribute.IsApplied</c> is
/// Orchard's own answer to "is this the admin", so there is no second rule to keep in step.
///
/// The script is an enhancement. Every caption is complete in the page and every link works without
/// it; what a reader without JavaScript loses is a hover hint, not a destination.
/// </remarks>
internal sealed class OrganisationMenuTooltipFilter : IAsyncResultFilter
{
    private readonly IResourceManager _resourceManager;

    public OrganisationMenuTooltipFilter(IResourceManager resourceManager) =>
        _resourceManager = resourceManager;

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (AdminAttribute.IsApplied(context.HttpContext))
        {
            // At the foot, because it reads the rendered menu: at the head it would run before the
            // sidebar exists and decorate nothing.
            _resourceManager.RegisterResource("script", "workmate-organisation-menu").AtFoot();
        }

        return next();
    }
}
