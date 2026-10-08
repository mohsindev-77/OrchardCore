using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// Appointing and clearing a unit's head, from the unit's own card.
/// </summary>
/// <remarks>
/// <b>Here rather than on the employee</b>, because the question a person is asking is "who runs
/// this department", not "what does this person run". The employee's placement screen lists what
/// they lead and links back here, which is the same relationship the designer and the placement
/// screen already have about placements.
///
/// Every write goes through <see cref="IEmployeeAssignmentService"/>, which is the single source
/// prompt 5's approval routing also reads. A head appointment is its own dated record, not an
/// assignment and not a field on the unit — ADR-0012 has the argument.
///
/// Gated by <see cref="Permissions.AssignEmployees"/>, the permission that already covers placing
/// somebody: appointing a head is the same kind of act, and a separate permission would split one
/// capability in two for no gain.
/// </remarks>
[Admin("Dimensions/Heads/{action}", "UnitHead{action}")]
public sealed class UnitHeadAdminController : Controller
{
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionService _dimensions;
    private readonly IStructureService _structures;
    private readonly IEnumerable<IEmployeeLookup> _employeeLookups;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    public UnitHeadAdminController(
        IEmployeeAssignmentService assignments,
        IDimensionService dimensions,
        IStructureService structures,
        IEnumerable<IEmployeeLookup> employeeLookups,
        IDimensionAuthorisation authorisation,
        INotifier notifier,
        IStringLocalizer<UnitHeadAdminController> stringLocalizer,
        IHtmlLocalizer<UnitHeadAdminController> htmlLocalizer)
    {
        _assignments = assignments;
        _dimensions = dimensions;
        _structures = structures;
        _employeeLookups = employeeLookups;
        _authorisation = authorisation;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    public async Task<IActionResult> Set(
        string structureId,
        string recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return Forbid();
        }

        var model = await BuildAsync(structureId, recordId, asAt, cancellationToken);

        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ActionName(nameof(Set))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPost(UnitHeadViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return Forbid();
        }

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["A date is needed."].Value);
        }
        else if (string.IsNullOrEmpty(model.EmployeeId))
        {
            ModelState.AddModelError(nameof(model.EmployeeId), S["Choose somebody to head this unit."].Value);
        }
        else
        {
            var appointed = await _assignments.SetHeadAsync(
                model.StructureId ?? string.Empty,
                model.RecordId ?? string.Empty,
                model.EmployeeId,
                effectiveFrom,
                cancellationToken);

            if (!appointed.IsAuthorised)
            {
                return Forbid();
            }

            if (appointed.Succeeded)
            {
                await _notifier.SuccessAsync(H["{0} heads '{1}' from {2}.",
                    await NameOfAsync(model.EmployeeId, cancellationToken),
                    model.UnitNameEn ?? model.RecordId ?? string.Empty,
                    BilingualDisplay.Date(effectiveFrom)]);

                return RedirectToDesigner(model);
            }

            foreach (var error in appointed.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Message.Value);
            }
        }

        var rebuilt = await BuildAsync(
            model.StructureId ?? string.Empty,
            model.RecordId ?? string.Empty,
            model.AsAt,
            cancellationToken);

        if (rebuilt is null)
        {
            return NotFound();
        }

        // What the person typed survives the redisplay: a form that discards the date they chose
        // because the name was wrong makes them do both again.
        rebuilt.EffectiveFrom = model.EffectiveFrom;
        rebuilt.EmployeeId = model.EmployeeId;

        return View(rebuilt);
    }

    public async Task<IActionResult> Clear(
        string structureId,
        string recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return Forbid();
        }

        var model = await BuildAsync(structureId, recordId, asAt, cancellationToken);

        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ActionName(nameof(Clear))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearPost(UnitHeadViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return Forbid();
        }

        if (!IsoDate.TryParse(model.LastDay, out var lastDay))
        {
            ModelState.AddModelError(nameof(model.LastDay), S["A last day is needed."].Value);

            var rebuilt = await BuildAsync(
                model.StructureId ?? string.Empty,
                model.RecordId ?? string.Empty,
                model.AsAt,
                cancellationToken);

            return rebuilt is null ? NotFound() : View(rebuilt);
        }

        var cleared = await _assignments.ClearHeadAsync(
            model.StructureId ?? string.Empty,
            model.RecordId ?? string.Empty,
            lastDay,
            cancellationToken);

        if (!cleared.IsAuthorised)
        {
            return Forbid();
        }

        await _notifier.SuccessAsync(cleared.Value > 0
            ? H["'{0}' has no head from {1}.",
                model.UnitNameEn ?? model.RecordId ?? string.Empty,
                BilingualDisplay.Date(lastDay.AddDays(1))]
            : H["'{0}' already had no head.", model.UnitNameEn ?? model.RecordId ?? string.Empty]);

        return RedirectToDesigner(model);
    }

    private RedirectToActionResult RedirectToDesigner(UnitHeadViewModel model) =>
        RedirectToAction(
            nameof(OrganisationDesignerAdminController.Index),
            "OrganisationDesignerAdmin",
            new { structureId = model.StructureId, asAt = model.AsAt, expand = model.RecordId });

    /// <summary>
    /// The unit, the axis, who heads it today and the term history — or null if either has gone.
    /// </summary>
    private async Task<UnitHeadViewModel?> BuildAsync(
        string structureId,
        string recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        var structure = await _structures.GetAsync(structureId, cancellationToken);

        if (structure is null)
        {
            return null;
        }

        var date = IsoDate.TryParse(asAt, out var parsed) ? parsed : await _authorisation.TodayAsync();
        var unit = await _dimensions.GetAsync(recordId, date, cancellationToken);

        if (unit is null)
        {
            return null;
        }

        var model = new UnitHeadViewModel
        {
            StructureId = structureId,
            StructureCode = structure.Code,
            StructureNameEn = structure.Name.En,
            StructureNameAr = structure.Name.Ar,
            RecordId = recordId,
            UnitCode = unit.Code,
            UnitNameEn = unit.NameEn,
            UnitNameAr = unit.NameAr,
            AsAt = date.ToIso(),
            EffectiveFrom = date.ToIso(),
            LastDay = date.ToIso(),
        };

        var current = await _assignments.GetHeadAsync(structureId, recordId, date, cancellationToken);

        if (current is not null)
        {
            model.CurrentHeadName = await NameOfAsync(current.EmployeeId, cancellationToken);
            model.CurrentHeadFrom = current.Range.From;
        }

        var terms = new List<UnitHeadTermViewModel>();

        foreach (var term in await _assignments.GetHeadHistoryAsync(structureId, recordId, cancellationToken))
        {
            terms.Add(new UnitHeadTermViewModel
            {
                EmployeeName = await NameOfAsync(term.EmployeeId, cancellationToken),
                From = term.Range.From,
                To = term.Range.To,
            });
        }

        model.Terms = terms;

        return model;
    }

    /// <summary>
    /// An employee's name in the reader's language, or their id when nothing can resolve it.
    /// </summary>
    /// <remarks>
    /// Falling back to the id is deliberate here and is the opposite of what the designer's card
    /// does. On a card, an unresolvable head reads as "Vacant", because a content item id tells a
    /// reader nothing. On this screen the id is the only honest answer: somebody is recorded as
    /// heading this unit and their record cannot be found, and showing "Vacant" would hide a
    /// problem on the one screen whose job is to fix it.
    /// </remarks>
    private async Task<string> NameOfAsync(string employeeId, CancellationToken cancellationToken)
    {
        var lookup = _employeeLookups.FirstOrDefault();

        if (lookup is null)
        {
            return employeeId;
        }

        var employee = await lookup.GetAsync(employeeId, cancellationToken);

        return employee?.ForCulture() ?? employeeId;
    }
}
