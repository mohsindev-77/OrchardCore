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
/// The structures list and editor. Every action calls <see cref="IStructureService"/> — no
/// business rule, including which type a level may be or whether a second structure may claim
/// the primary organisation, is decided here; the permission check below is for the screen's own
/// sake, not a second source of authority.
/// </summary>
[Admin("Dimensions/Structures/{action}/{id?}", "Structures{action}")]
public sealed class StructuresAdminController : Controller
{
    private readonly IStructureService _structureService;
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    /// <summary>The view both Create and Edit render, named for the action whose file it is.</summary>
    private const string EditorViewName = "Edit";

    public StructuresAdminController(
        IStructureService structureService,
        IDimensionTypeService dimensionTypeService,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IStringLocalizer<StructuresAdminController> stringLocalizer,
        IHtmlLocalizer<StructuresAdminController> htmlLocalizer)
    {
        _structureService = structureService;
        _dimensionTypeService = dimensionTypeService;
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

        var structures = await _structureService.ListAsync(cancellationToken);
        var typesById = (await _dimensionTypeService.ListAsync(includeRetired: true, cancellationToken: cancellationToken))
            .ToDictionary(type => type.DimensionTypeId);

        return View(structures
            .OrderBy(structure => structure.Code, StringComparer.Ordinal)
            .Select(structure => StructureListItemViewModel.Of(structure, typesById))
            .ToList());
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var model = new StructureEditViewModel
        {
            AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken),
        };

        return View(EditorViewName, model);
    }

    [HttpPost]
    [ActionName(nameof(Create))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(StructureEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            model.AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken);
            return View(EditorViewName, model);
        }

        var result = await _structureService.CreateAsync(
            (model.Code ?? string.Empty).Trim(),
            model.Name,
            model.ToLevelDimensionTypeIds(),
            model.AllowSkipLevel,
            model.IsStrict,
            model.IsPrimaryOrganisation,
            model.ToShape(),
            cancellationToken: cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            model.AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken);
            return View(EditorViewName, model);
        }

        await _notifier.SuccessAsync(H["Structure '{0}' was created.", result.Value!.Code]);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _structureService.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        var model = StructureEditViewModel.Of(document);
        model.AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken);

        return View(model);
    }

    [HttpPost]
    [ActionName(nameof(Edit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(string id, StructureEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _structureService.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        // The code names no content type here, but it is still the key links, closure rows and
        // assignments are scoped by; the posted value is never trusted for an update, only shown.
        model.StructureId = document.StructureId;
        model.Code = document.Code;

        if (!ModelState.IsValid)
        {
            model.AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken);
            return View(model);
        }

        var levelDimensionTypeIds = model.ToLevelDimensionTypeIds();

        // Shown once, before anything is saved, the same way a move or a merge is: what this
        // change would do to records already placed on the axis. UpdateAsync re-checks this exact
        // plan before saving, so confirming here can never bypass it.
        var planResult = await _structureService.PlanLevelChangeAsync(
            document.StructureId,
            levelDimensionTypeIds,
            model.AllowSkipLevel,
            model.IsStrict,
            model.ToShape(),
            cancellationToken);

        if (!planResult.IsAuthorised)
        {
            return Forbid();
        }

        if (!planResult.Succeeded)
        {
            AddErrors(planResult.Errors);
            model.AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken);
            return View(model);
        }

        if (!planResult.Value!.IsEmpty)
        {
            return View("ConfirmLevelChange", await BuildConfirmationAsync(model, planResult.Value, cancellationToken));
        }

        return await ApplyUpdateAsync(document.StructureId, model, cancellationToken);
    }

    /// <summary>
    /// Applies a level change the confirmation screen showed, exactly as shown: every value here
    /// came back as a hidden field on that screen, not as something re-typed, so there is nothing
    /// to validate that <see cref="EditPost"/> did not already validate once.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditConfirmed(StructureLevelChangeConfirmationViewModel confirmation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var model = new StructureEditViewModel
        {
            StructureId = confirmation.StructureId,
            Code = confirmation.Code,
            NameEn = confirmation.NameEn,
            NameAr = confirmation.NameAr,
            AllowSkipLevel = confirmation.AllowSkipLevel,
            IsStrict = confirmation.IsStrict,
            IsPrimaryOrganisation = confirmation.IsPrimaryOrganisation,
            LevelDimensionTypeIds = confirmation.LevelDimensionTypeIds,
            RootDimensionTypeIds = confirmation.RootDimensionTypeIds,
            ContainmentPairs = confirmation.ContainmentPairs,
        };

        return await ApplyUpdateAsync(confirmation.StructureId, model, cancellationToken);
    }

    private async Task<IActionResult> ApplyUpdateAsync(
        string structureId, StructureEditViewModel model, CancellationToken cancellationToken)
    {
        var result = await _structureService.UpdateAsync(
            structureId,
            model.Name,
            model.ToLevelDimensionTypeIds(),
            model.AllowSkipLevel,
            model.IsStrict,
            model.IsPrimaryOrganisation,
            model.ToShape(),
            cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            // A violation found here and not at the preview a moment ago means something else
            // changed the structure in between; redisplay the ordinary editor with the reason
            // rather than the confirmation screen, which no longer describes the current state.
            AddErrors(result.Errors);
            model.AvailableDimensionTypes = await AvailableDimensionTypesAsync(cancellationToken);
            return View(EditorViewName, model);
        }

        await _notifier.SuccessAsync(H["Structure '{0}' was updated.", result.Value!.Code]);

        return RedirectToAction(nameof(Index));
    }

    private async Task<StructureLevelChangeConfirmationViewModel> BuildConfirmationAsync(
        StructureEditViewModel model, StructureLevelChangePlan plan, CancellationToken cancellationToken)
    {
        var typesById = (await _dimensionTypeService.ListAsync(includeRetired: true, cancellationToken: cancellationToken))
            .ToDictionary(type => type.DimensionTypeId);

        string LabelFor(string dimensionTypeId) =>
            typesById.TryGetValue(dimensionTypeId, out var type) ? type.Name.En : dimensionTypeId;

        return new StructureLevelChangeConfirmationViewModel
        {
            StructureId = model.StructureId!,
            Code = model.Code,
            NameEn = model.NameEn ?? string.Empty,
            NameAr = model.NameAr ?? string.Empty,
            AllowSkipLevel = model.AllowSkipLevel,
            IsStrict = model.IsStrict,
            IsPrimaryOrganisation = model.IsPrimaryOrganisation,
            LevelDimensionTypeIds = [.. model.LevelDimensionTypeIds],
            RootDimensionTypeIds = [.. model.RootDimensionTypeIds],
            ContainmentPairs = [.. model.ContainmentPairs],
            AddedLevelLabels = [.. plan.AddedDimensionTypeIds.Select(LabelFor)],
            RemovalImpacts =
            [
                .. plan.RemovalImpacts.Select(impact => new LevelRemovalImpactViewModel
                {
                    Label = LabelFor(impact.DimensionTypeId),
                    RecordCount = impact.RecordCount,
                    EmployeesAffected = impact.EmployeesAffected,
                }),
            ],
        };
    }

    private async Task<List<StructureLevelOptionViewModel>> AvailableDimensionTypesAsync(CancellationToken cancellationToken) =>
        [
            .. (await _dimensionTypeService.ListAsync(cancellationToken: cancellationToken))
                .OrderBy(type => type.Name.En, StringComparer.OrdinalIgnoreCase)
                .Select(type => new StructureLevelOptionViewModel
                {
                    DimensionTypeId = type.DimensionTypeId,
                    Code = type.Code,
                    NameEn = type.Name.En,
                    NameAr = type.Name.Ar,
                }),
        ];

    private Task<bool> IsAuthorisedAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ManageStructures);

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
