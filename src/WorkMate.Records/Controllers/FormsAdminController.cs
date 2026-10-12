using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
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
    private readonly IStandardLookupListsLoader _standardLists;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IHtmlLocalizer H;

    public FormsAdminController(
        ILookupListService lookupLists,
        IStandardLookupListsLoader standardLists,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IHtmlLocalizer<FormsAdminController> htmlLocalizer)
    {
        _lookupLists = lookupLists;
        _standardLists = standardLists;
        _authorizationService = authorizationService;
        _notifier = notifier;
        H = htmlLocalizer;
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
            MissingStandardLists = await _standardLists.MissingAsync(cancellationToken),
        });
    }

    /// <summary>
    /// Applies the standard lookup lists recipe to this tenant.
    /// </summary>
    /// <remarks>
    /// <b>Why this is here and not only on Configuration → Recipes.</b> An HR administrator
    /// configuring their own forms should not have to go to an operator's screen that lists every
    /// recipe in the installation to finish the job. The work and every check belong to
    /// <see cref="IStandardLookupListsLoader"/>; this does what a controller is for — take the post,
    /// say what happened, and redirect.
    ///
    /// Redirect rather than render, because the page it returns to is the page whose answer has just
    /// changed: the count goes up, the card goes away, and a refresh must not re-apply anything.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadStandardLists(CancellationToken cancellationToken)
    {
        var result = await _standardLists.LoadAsync(cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                // Passed through rather than put through the localiser again: the service raised it
                // in the reader's language, and a second lookup on a sentence that is already a
                // translation would find nothing and show the English. The same reason
                // LookupListsAdminController hands error.Message.Value to ModelState as it is; this
                // screen redirects after the post, where ModelState does not survive.
                await _notifier.ErrorAsync(
                    new LocalizedHtmlString(error.Message.Value, error.Message.Value));
            }

            return RedirectToAction(nameof(Index));
        }

        // Named rather than counted. Somebody who has just pressed a button wants to know what it
        // did, and "nothing was missing" is a real and useful answer on a tenant that already has
        // them — a second press, or two people pressing at once.
        await _notifier.SuccessAsync(
            result.Value!.Count == 0
                ? H["The standard lists were already loaded. Nothing changed."]
                : H["The standard lists are loaded: {0}.", string.Join(", ", result.Value!)]);

        return RedirectToAction(nameof(Index));
    }
}
