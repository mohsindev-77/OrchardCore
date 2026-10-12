using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.Admin;
using WorkMate.Records.Services;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Controllers;

/// <summary>
/// The Forms screen: the one admin menu entry the form designer gets, and the way in to everything
/// behind it.
/// </summary>
/// <remarks>
/// <b>Why this screen exists before there is a form designer.</b> The product owner's rule is that
/// the designer adds exactly one menu entry, <c>People → Forms</c>, and that every other screen is
/// reached by clicking from there or from the employee editor. Slice B1 ships the managed lookup
/// lists, which are one of those screens — so either this page exists now and the lists hang off
/// it, or the lists get a menu entry of their own that has to be taken away again in slice B4.
///
/// The second option is worse than it sounds. A menu entry is the thing customers learn, write
/// training material about and link to; moving one later is a change every customer notices for no
/// benefit they can see. This page is cheap, it is where the designer is going to live anyway, and
/// it means the navigation a customer learns in B1 is the navigation they keep.
///
/// It says plainly what is not here yet, in the manner <c>PlaceholderPickerTagHelper</c> already
/// sets for this product: a control or a screen that is waiting on something says so, rather than
/// pretending to be complete or being silently absent.
/// </remarks>
[Admin("Forms/{action}/{id?}", "Forms{action}")]
public sealed class FormsAdminController : Controller
{
    private readonly ILookupListService _lookupLists;
    private readonly IAuthorizationService _authorizationService;

    public FormsAdminController(
        ILookupListService lookupLists,
        IAuthorizationService authorizationService)
    {
        _lookupLists = lookupLists;
        _authorizationService = authorizationService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var mayManage = await _authorizationService.AuthorizeAsync(User, Permissions.ManageLookupLists);

        if (!mayManage)
        {
            // Nothing on this page is reachable without it today, so a reader who lacks it is
            // forbidden rather than shown an empty page. Slice B4 adds form definitions behind a
            // permission of their own and this becomes "may reach at least one of the two".
            return Forbid();
        }

        return View(new FormsIndexViewModel
        {
            LookupListCount = (await _lookupLists.ListAsync(cancellationToken)).Count,
            MayManageLookupLists = mayManage,
        });
    }
}
