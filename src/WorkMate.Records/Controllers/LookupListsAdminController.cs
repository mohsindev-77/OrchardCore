using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Controllers;

/// <summary>
/// The lookup list screens: the list of lists, the editor for one, and the narrow deletion.
/// </summary>
/// <remarks>
/// Every action calls <see cref="ILookupListService"/> — the same service the <c>lookup-lists</c>
/// recipe step and the export use — so nothing here decides a rule the service does not also
/// enforce. The permission check below is for the screen's own sake: what to show, and what to
/// forbid before a POST. It is not a second source of authority.
///
/// <b>Reached from Forms, never from the menu.</b> Specification-level constraint from the product
/// owner: the form designer adds one menu entry and everything else is a click from an existing
/// screen. <c>FormsAdminController.Index</c> is the way in, and the editor's field panel in slice
/// B2 becomes a second one.
/// </remarks>
[Admin("Forms/Lookups/{action}/{id?}", "FormsLookups{action}")]
public sealed class LookupListsAdminController : Controller
{
    /// <summary>The view both Create and Edit render, named for the action whose file it is.</summary>
    private const string EditorViewName = "Edit";

    /// <summary>
    /// The gap between the sort orders a new row is given.
    /// </summary>
    /// <remarks>
    /// Ten rather than one so that a reader can slot an entry between two others by typing a number
    /// instead of renumbering the list. The same reason a BASIC listing numbered its lines in tens.
    /// </remarks>
    private const int SortOrderStep = 10;

    private readonly ILookupListService _lookupLists;
    private readonly ILookupValueReconciliation _reconciliation;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    public LookupListsAdminController(
        ILookupListService lookupLists,
        ILookupValueReconciliation reconciliation,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IStringLocalizer<LookupListsAdminController> stringLocalizer,
        IHtmlLocalizer<LookupListsAdminController> htmlLocalizer)
    {
        _lookupLists = lookupLists;
        _reconciliation = reconciliation;
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

        var lists = await _lookupLists.ListAsync(cancellationToken);

        return View(lists.Select(LookupListListItemViewModel.Of).ToList());
    }

    public async Task<IActionResult> Create()
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        // One empty row, because a list with no entries is not useful and an editor that starts
        // with nothing to fill in makes the reader find the Add button before they can begin.
        return View(EditorViewName, new LookupListEditViewModel
        {
            IsNew = true,
            Items = [new LookupListItemRowViewModel { SortOrder = SortOrderStep }],
        });
    }

    [HttpPost]
    [ActionName(nameof(Create))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(
        LookupListEditViewModel model,
        string? command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        model.IsNew = true;
        model.Items ??= [];

        if (HandleRowCommand(model, command) is { } redraw)
        {
            return View(EditorViewName, redraw);
        }

        if (!ModelState.IsValid)
        {
            return View(EditorViewName, model);
        }

        var result = await _lookupLists.CreateAsync(
            (model.Code ?? string.Empty).Trim(),
            model.Name,
            model.ToItems(),
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

        await _notifier.SuccessAsync(H["Lookup list '{0}' was created.", result.Value!.Code]);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _lookupLists.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        return View(LookupListEditViewModel.Of(document));
    }

    [HttpPost]
    [ActionName(nameof(Edit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(
        string id,
        LookupListEditViewModel model,
        string? command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _lookupLists.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        // The code is the natural key and is immutable; a posted value is never trusted for an
        // update, only shown. The same shape DimensionTypesAdminController uses for a dimension
        // type's code, and for the same reason: a screen that let one through would rename a list
        // every field binding and every recipe already names.
        model.LookupListId = document.LookupListId;
        model.Code = document.Code;
        model.IsNew = false;
        model.IsSystemDefined = document.IsSystemDefined;
        model.Items ??= [];

        // Which rows the tenant already holds, restored from the document rather than taken from
        // the post. It decides only what the screen offers — the service refuses a dropped or
        // renamed entry whatever this says — but a reader who could see Remove on a stored entry
        // would press it and be refused, which is a worse screen than one that does not offer it.
        MarkStoredRows(model, document.Items.Select(item => item.Code));

        if (HandleRowCommand(model, command) is { } redraw)
        {
            return View(redraw);
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _lookupLists.UpdateAsync(
            document.LookupListId,
            model.Name,
            model.ToItems(),
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

        await _notifier.SuccessAsync(H["Lookup list '{0}' was saved.", result.Value!.Code]);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _lookupLists.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        // Refused on the way in as well as on the way out, so that a reader who follows a stale
        // link gets the explanation on the list they came from rather than a confirmation page for
        // something that cannot happen.
        if (document.IsSystemDefined || document.Items.Count > 0)
        {
            await _notifier.WarningAsync(H[
                "'{0}' cannot be deleted. A list can only be deleted while it holds no entries, because an entry may be a value records already carry — retire the entries you no longer offer instead.",
                document.Code]);

            return RedirectToAction(nameof(Index));
        }

        return View(new LookupListDeleteViewModel
        {
            LookupListId = document.LookupListId,
            Code = document.Code,
            NameEn = document.Name.En,
        });
    }

    [HttpPost]
    [ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePost(
        LookupListDeleteViewModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _lookupLists.GetAsync(model.LookupListId ?? string.Empty, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        // Code and NameEn are [BindNever], so they must be set from the document before any
        // redisplay or the confirmation page would name nothing.
        model.Code = document.Code;
        model.NameEn = document.Name.En;

        var result = await _lookupLists.DeleteAsync(document.LookupListId, cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);

            return View(model);
        }

        await _notifier.SuccessAsync(H["Lookup list '{0}' was deleted.", document.Code]);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The values records hold that this list has no entry for.
    /// </summary>
    /// <remarks>
    /// Its own screen rather than part of the editor, because it is a different question answered
    /// at a different cost: the editor is a form, and this reads every employee record in the
    /// tenant. Reached by a button on the editor, which is where somebody who has just bound a list
    /// already is.
    /// </remarks>
    public async Task<IActionResult> Values(string id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _lookupLists.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        return View(await ValuesViewModelAsync(document, cancellationToken));
    }

    /// <summary>
    /// Applies the reader's choices: adds entries, maps values, and leaves everything else alone.
    /// </summary>
    /// <remarks>
    /// <b>Adds first, then maps.</b> A reader can legitimately add one value as an entry and map
    /// another onto it in the same press, and doing it the other way round would refuse the map
    /// because its target did not exist yet.
    ///
    /// <b>Every row defaults to leave.</b> Pressing Apply without choosing anything changes
    /// nothing, which is the behaviour a screen that rewrites records had better have.
    /// </remarks>
    [HttpPost]
    [ActionName(nameof(Values))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValuesPost(
        string id,
        LookupValuesViewModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var document = await _lookupLists.GetAsync(id, cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        var rows = model.Rows ?? [];

        var toAdd = rows
            .Where(row => row.Action == UnmatchedValueActions.Add && !string.IsNullOrWhiteSpace(row.Value))
            .ToList();

        var toMap = rows
            .Where(row => row.Action == UnmatchedValueActions.Map &&
                !string.IsNullOrWhiteSpace(row.Value) &&
                !string.IsNullOrWhiteSpace(row.MapTo))
            .ToList();

        if (toAdd.Count == 0 && toMap.Count == 0)
        {
            await _notifier.InformationAsync(H["Nothing was changed."]);

            return RedirectToAction(nameof(Values), new { id });
        }

        if (toAdd.Count > 0)
        {
            // One write for every added entry at once: the list is stated whole, so adding four
            // values is one update rather than four, and four audit entries saying the same thing.
            var added = await _lookupLists.UpdateAsync(
                document.LookupListId,
                document.Name,
                [
                    .. document.Items,
                    .. toAdd.Select((row, ordinal) => new LookupListItem(
                        row.Value!.Trim(),
                        // The value becomes the English name as well as the code, because that is
                        // all the tenant has ever recorded about it. Somebody renames it afterwards
                        // if they want to; inventing a different name here would be guessing.
                        new WorkMate.Core.BilingualText(row.Value!.Trim(), string.Empty),
                        IsActive: true,
                        SortOrder: NextSortOrder(document) + (ordinal * SortOrderStep))),
                ],
                cancellationToken);

            if (!added.Succeeded)
            {
                AddErrors(added.Errors);

                return View(await ValuesViewModelAsync(document, cancellationToken));
            }
        }

        var mapped = 0;

        foreach (var row in toMap)
        {
            var result = await _reconciliation.MapAsync(
                document.Code, row.Value!, row.MapTo!, cancellationToken);

            if (!result.IsAuthorised)
            {
                return Forbid();
            }

            if (!result.Succeeded)
            {
                AddErrors(result.Errors);

                return View(await ValuesViewModelAsync(document, cancellationToken));
            }

            mapped += result.Value;
        }

        await _notifier.SuccessAsync(H[
            "{0} entry/entries added, and {1} stored value(s) rewritten.", toAdd.Count, mapped]);

        return RedirectToAction(nameof(Values), new { id });
    }

    /// <summary>The next free sort order, so an added entry lands at the end of the list.</summary>
    private static int NextSortOrder(Models.LookupListDocument document) =>
        document.Items.Count == 0 ? SortOrderStep : document.Items.Max(item => item.SortOrder) + SortOrderStep;

    private async Task<LookupValuesViewModel> ValuesViewModelAsync(
        Models.LookupListDocument document,
        CancellationToken cancellationToken)
    {
        var scan = await _reconciliation.ScanAsync(document.Code, cancellationToken);
        var locations = await _reconciliation.BoundLocationsAsync(document.Code, cancellationToken);

        return new LookupValuesViewModel
        {
            LookupListId = document.LookupListId,
            Code = document.Code,
            NameEn = document.Name.En,
            BoundLocations = [.. locations.Select(Describe)],
            EmployeesScanned = scan.EmployeesScanned,
            WasTruncated = scan.WasTruncated,
            Targets =
            [
                .. document.Items
                    .Where(item => item.IsActive)
                    .Select(item => new LookupValueTargetViewModel
                    {
                        Code = item.Code,
                        Label = $"{WorkMate.Core.BilingualText.Display(item.Name.En, item.Name.Ar)} ({item.Code})",
                    }),
            ],
            Rows =
            [
                .. scan.Values.Select(value => new UnmatchedValueRowViewModel
                {
                    Value = value.Value,
                    Count = value.Count,
                    SampleEmployeeCodes = string.Join(", ", value.SampleEmployeeCodes),
                    Action = UnmatchedValueActions.Leave,
                }),
            ],
        };
    }

    /// <summary>Where a bound value lives, in words rather than in definition names.</summary>
    private string Describe(LookupBoundLocation location) =>
        location.FieldName == LookupValueReconciliation.CoreNationality
            ? S["The employee record's nationality"].Value
            : S["{0} — {1}", location.SectionName, location.FieldName].Value;

    /// <summary>
    /// Adds or removes an entry row, returning the model to redraw, or null when the press was Save.
    /// </summary>
    /// <remarks>
    /// <b>Round trips rather than script, like every other repeating form in this module.</b> The
    /// placement screen's rows work this way and say why: these forms commit decisions, and one
    /// that only worked once a script had loaded would silently do nothing when it had not. It also
    /// means the row the server draws is the row the server will read back, which a cloned row
    /// cannot promise — and here it is what keeps a stored entry's code read-only after an add.
    ///
    /// <c>formnovalidate</c> on both buttons in the view is what lets a reader add a second row
    /// before finishing the first.
    /// </remarks>
    private static LookupListEditViewModel? HandleRowCommand(LookupListEditViewModel model, string? command)
    {
        if (string.Equals(command, "add", StringComparison.Ordinal))
        {
            var highest = model.Items.Count == 0 ? 0 : model.Items.Max(row => row.SortOrder);

            model.Items.Add(new LookupListItemRowViewModel
            {
                SortOrder = highest + SortOrderStep,
            });

            return model;
        }

        if (command?.StartsWith("remove:", StringComparison.Ordinal) == true &&
            int.TryParse(
                command["remove:".Length..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var removing) &&
            removing >= 0 &&
            removing < model.Items.Count &&
            // A stored entry is never removed from the form, because it can never be removed from
            // the list. Refusing here as well as in the service keeps a hand-made post from
            // producing a form that looks like it worked and then fails on Save.
            !model.Items[removing].IsStored)
        {
            model.Items.RemoveAt(removing);

            return model;
        }

        return null;
    }

    /// <summary>
    /// Marks the rows whose code the tenant already holds, by code rather than by position.
    /// </summary>
    /// <remarks>
    /// By code, because a reader may have added, removed and reordered rows before posting, so the
    /// row at index three is not necessarily the entry that was stored at index three. Matching on
    /// position would eventually mark the wrong row as immutable — and the symptom would be a
    /// Remove button on an entry records depend on.
    /// </remarks>
    private static void MarkStoredRows(LookupListEditViewModel model, IEnumerable<string> storedCodes)
    {
        var stored = new HashSet<string>(
            storedCodes.Select(LookupCodes.Normalise),
            LookupCodes.Comparer);

        foreach (var row in model.Items)
        {
            row.IsStored = stored.Contains(LookupCodes.Normalise(row.Code));
        }
    }

    private Task<bool> IsAuthorisedAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ManageLookupLists);

    /// <summary>
    /// Puts each violation where the reader has to act on it: under its own field when the rule
    /// names one, in the summary when it does not.
    /// </summary>
    /// <remarks>
    /// The same shape <c>DimensionTypesAdminController</c> uses. A malformed code on the fourth
    /// entry belongs under the fourth entry's code box, not in a list at the top that the reader
    /// then has to match up against a table of ten rows.
    /// </remarks>
    private void AddErrors(IReadOnlyList<RecordError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
        }
    }
}
