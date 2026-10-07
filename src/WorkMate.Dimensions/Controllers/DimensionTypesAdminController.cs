using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// The dimension type list and editor. Every action calls <see cref="IDimensionTypeService"/> —
/// the same service the recipe step and any future API use — so nothing here decides a rule the
/// service does not also enforce; the permission check below is for the screen's own sake (what to
/// show, what to forbid before a POST), not a second source of authority.
/// </summary>
[Admin("Dimensions/Types/{action}/{id?}", "DimensionTypes{action}")]
public sealed class DimensionTypesAdminController : Controller
{
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    /// <summary>The view both Create and Edit render, named for the action whose file it is.</summary>
    private const string EditorViewName = "Edit";

    public DimensionTypesAdminController(
        IDimensionTypeService dimensionTypeService,
        IDimensionAuthorisation authorisation,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IStringLocalizer<DimensionTypesAdminController> stringLocalizer,
        IHtmlLocalizer<DimensionTypesAdminController> htmlLocalizer)
    {
        _dimensionTypeService = dimensionTypeService;
        _authorisation = authorisation;
        _authorizationService = authorizationService;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var types = await _dimensionTypeService.ListAsync(includeRetired: true, cancellationToken: cancellationToken);

        return View(types
            .OrderBy(type => type.Code, StringComparer.Ordinal)
            .Select(DimensionTypeListItemViewModel.Of)
            .ToList());
    }

    public async Task<IActionResult> Create()
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        // Create and Edit share one view, EditorViewName: the editor looks identical either way
        // bar the code field's editability, which the view itself decides from IsNew.
        return View(EditorViewName, new DimensionTypeEditViewModel());
    }

    [HttpPost]
    [ActionName(nameof(Create))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(DimensionTypeEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(EditorViewName, model);
        }

        var result = await _dimensionTypeService.CreateAsync(
            (model.Code ?? string.Empty).Trim(),
            model.Name,
            model.ToAttributeSchema(),
            // A type created here carries no self-nesting flag. Nothing reads it when deciding a
            // placement any more, and the structure grid is where self-nesting is now said.
            allowsSelfNesting: false,
            cancellationToken: cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(EditorViewName, model);
        }

        await _notifier.SuccessAsync(H[
            "Dimension type '{0}' was created. Its content type is '{1}'.",
            result.Value!.Code,
            result.Value.ContentTypeName]);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _dimensionTypeService.GetAsync(id, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        return View(DimensionTypeEditViewModel.Of(document));
    }

    [HttpPost]
    [ActionName(nameof(Edit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(string id, DimensionTypeEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _dimensionTypeService.GetAsync(id, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        // The code names the backing content type and is immutable once created; the posted
        // value is never trusted for an update, only shown.
        model.DimensionTypeId = document.DimensionTypeId;
        model.Code = document.Code;
        model.IsSystemDefined = document.IsSystemDefined;
        model.ContentTypeName = document.ContentTypeName;

        // Carried through unchanged. The screen stopped offering it with ADR-0010's addendum, and
        // a save that silently cleared it would change what a chain-shaped recipe row means the
        // next time one is applied.
        model.AllowsSelfNesting = document.AllowsSelfNesting;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _dimensionTypeService.UpdateAsync(
            document.DimensionTypeId,
            model.Name,
            model.ToAttributeSchema(),
            model.AllowsSelfNesting,
            cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        await _notifier.SuccessAsync(H["Dimension type '{0}' was updated.", result.Value!.Code]);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Retire(string id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _dimensionTypeService.GetAsync(id, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        if (document.IsSystemDefined)
        {
            await _notifier.WarningAsync(H[
                "'{0}' is defined by WorkMate and cannot be retired.", document.Code]);

            return RedirectToAction(nameof(Index));
        }

        return View(new DimensionTypeRetireViewModel
        {
            DimensionTypeId = document.DimensionTypeId,
            Code = document.Code,
            NameEn = document.Name.En,
            EffectiveDate = await _authorisation.TodayAsync(),
        });
    }

    [HttpPost]
    [ActionName(nameof(Retire))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirePost(DimensionTypeRetireViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        // Code and NameEn are [BindNever] now — a posted value for either is never bound — so
        // they must be set here, from the record the id names, before any redisplay, or the
        // confirmation message would show nothing on a validation failure.
        var document = await _dimensionTypeService.GetAsync(model.DimensionTypeId, cancellationToken: cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        model.Code = document.Code;
        model.NameEn = document.Name.En;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _dimensionTypeService.RetireAsync(
            model.DimensionTypeId,
            model.EffectiveDate,
            cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        await _notifier.SuccessAsync(H["Dimension type '{0}' was retired from {1:d}.", result.Value!.Code, model.EffectiveDate]);

        return RedirectToAction(nameof(Index));
    }

    private Task<bool> IsAuthorisedAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionTypes);

    /// <summary>
    /// Puts each violation where the reader has to act on it: under its own field when the rule
    /// names one, in the summary when it does not.
    /// </summary>
    /// <remarks>
    /// A missing English name belongs under the English box, not in a list at the top of the page
    /// that the reader then has to match up against the form. It must also never arrive as an
    /// unhandled exception: a validation problem is an answer, not a failure.
    /// </remarks>
    private void AddErrors(IReadOnlyList<DimensionError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
        }
    }
}
