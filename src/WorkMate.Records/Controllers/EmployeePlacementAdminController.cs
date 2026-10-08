using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Services;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Controllers;

/// <summary>
/// Where one employee sits, on every axis, and the dated operations that change it.
/// </summary>
/// <remarks>
/// <b>Placement is the brief's "Placement section", and it is a screen rather than a driver on the
/// employee editor.</b> A display driver's <c>UpdateAsync</c> has nowhere to put a required
/// effective date: it is handed a posted model and asked to write it. Every write here is dated and
/// nothing is defaulted, so the editor shows placement read-only and links here, and each operation
/// gets its own small form with its own date — exactly the shape the organisation designer's
/// actions already have.
///
/// Everything reads and writes through <see cref="IEmployeeAssignmentService"/>. This module never
/// touches the assignment tables, which are internal to <c>WorkMate.Dimensions</c> and are meant to
/// be: the compiler enforces it.
/// </remarks>
[Admin("Employees/Placement/{action}/{id?}", "EmployeePlacement{action}")]
public sealed class EmployeePlacementAdminController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IStructureService _structures;
    private readonly IDimensionService _dimensions;
    private readonly IDimensionGraphService _graph;
    private readonly IDimensionTypeService _types;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    public EmployeePlacementAdminController(
        IEmployeeService employees,
        IEmployeeAssignmentService assignments,
        IStructureService structures,
        IDimensionService dimensions,
        IDimensionGraphService graph,
        IDimensionTypeService types,
        IRecordsAuthorisation authorisation,
        INotifier notifier,
        IStringLocalizer<EmployeePlacementAdminController> stringLocalizer,
        IHtmlLocalizer<EmployeePlacementAdminController> htmlLocalizer)
    {
        _employees = employees;
        _assignments = assignments;
        _structures = structures;
        _dimensions = dimensions;
        _graph = graph;
        _types = types;
        _authorisation = authorisation;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    /// <summary>Where this employee sits on every axis on a date, and what they lead.</summary>
    public async Task<IActionResult> Index(string id, string? asAt, CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ViewEmployees))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(id, cancellationToken);

        if (employee is null)
        {
            return NotFound();
        }

        // Reads default to today; writes never do.
        var date = IsoDate.TryParse(asAt, out var parsed) ? parsed : await _authorisation.TodayAsync();

        var model = new EmployeePlacementViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            AsAt = date.ToIso(),
            CanAssign = await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees),
        };

        var axes = new List<AxisPlacementViewModel>();

        foreach (var structure in await _structures.ListAsync(cancellationToken))
        {
            var rows = await _assignments.GetAllEffectiveAsync(
                employee.EmployeeId, structure.StructureId, date, cancellationToken);

            var placements = new List<PlacementRowViewModel>();

            foreach (var row in rows)
            {
                var unit = await _dimensions.GetAsync(row.RecordId, date, cancellationToken);

                placements.Add(new PlacementRowViewModel
                {
                    RecordId = row.RecordId,
                    UnitCode = unit?.Code,
                    UnitNameEn = unit?.NameEn,
                    UnitNameAr = unit?.NameAr,
                    AllocationPercent = row.AllocationPercent,
                    IsPrimary = row.IsPrimary,
                    From = row.Range.From,
                    To = row.Range.To,
                });
            }

            axes.Add(new AxisPlacementViewModel
            {
                StructureId = structure.StructureId,
                StructureCode = structure.Code,
                StructureNameEn = structure.Name.En,
                StructureNameAr = structure.Name.Ar,

                // Primary first, which is what "where does this person work" means.
                Rows = [.. placements.OrderByDescending(row => row.IsPrimary).ThenBy(row => row.UnitName)],
            });
        }

        model.Axes = axes;
        model.Headships = await HeadshipsAsync(employee.EmployeeId, date, cancellationToken);

        return View(model);
    }

    private async Task<IReadOnlyList<HeadshipViewModel>> HeadshipsAsync(
        string employeeId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var headships = new List<HeadshipViewModel>();

        foreach (var headship in await _assignments.GetHeadshipsOfAsync(employeeId, asAt, cancellationToken))
        {
            var structure = await _structures.GetAsync(headship.StructureId, cancellationToken);
            var unit = await _dimensions.GetAsync(headship.RecordId, asAt, cancellationToken);

            headships.Add(new HeadshipViewModel
            {
                StructureId = headship.StructureId,
                StructureCode = structure?.Code,
                RecordId = headship.RecordId,
                UnitCode = unit?.Code,
                UnitNameEn = unit?.NameEn,
                UnitNameAr = unit?.NameAr,
                From = headship.Range.From,
                To = headship.Range.To,
            });
        }

        return headships;
    }

    public async Task<IActionResult> Place(
        string id,
        string? structureId,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(id, cancellationToken);

        if (employee is null)
        {
            return NotFound();
        }

        var model = new PlaceEmployeeViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            StructureId = structureId,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
        };

        await FillChoicesAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [ActionName(nameof(Place))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlacePost(PlaceEmployeeViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(model.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return NotFound();
        }

        model.Code = employee.Code;
        model.NameEn = employee.NameEn;
        model.NameAr = employee.NameAr;

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["A date is needed."].Value);
        }
        else if (string.IsNullOrEmpty(model.StructureId) || string.IsNullOrEmpty(model.RecordId))
        {
            ModelState.AddModelError(nameof(model.RecordId), S["Choose an axis and a unit."].Value);
        }
        else
        {
            var placed = await _assignments.PlaceAsync(
                model.EmployeeId,
                model.StructureId,
                model.RecordId,
                effectiveFrom,
                model.AllocationPercent,
                model.IsPrimary,
                cancellationToken);

            if (!placed.IsAuthorised)
            {
                return Forbid();
            }

            if (placed.Succeeded)
            {
                await _notifier.SuccessAsync(H["{0} was placed from {1}.",
                    employee.NameEn, EmployeeDisplay.Date(effectiveFrom)]);

                // Advisories ride along with a success and are surfaced rather than swallowed. The
                // one that matters here says the unit has units under it, so this person is counted
                // by it and again by every roll-up beneath it.
                foreach (var advisory in placed.Errors)
                {
                    await _notifier.WarningAsync(new LocalizedHtmlString(
                        advisory.Message.Name, advisory.Message.Value));
                }

                return RedirectToAction(nameof(Index), new { id = model.EmployeeId });
            }

            foreach (var error in placed.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Message.Value);
            }
        }

        await FillChoicesAsync(model, cancellationToken);

        return View(model);
    }

    public async Task<IActionResult> End(string id, string structureId, CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(id, cancellationToken);
        var structure = await _structures.GetAsync(structureId, cancellationToken);

        if (employee is null || structure is null)
        {
            return NotFound();
        }

        return View(new EndPlacementViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            StructureId = structure.StructureId,
            StructureName = structure.Name.En,
            LastDay = (await _authorisation.TodayAsync()).ToIso(),
        });
    }

    [HttpPost]
    [ActionName(nameof(End))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndPost(EndPlacementViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees))
        {
            return Forbid();
        }

        if (!IsoDate.TryParse(model.LastDay, out var lastDay))
        {
            ModelState.AddModelError(nameof(model.LastDay), S["A last day is needed."].Value);

            return View(model);
        }

        var ended = await _assignments.EndAsync(
            model.EmployeeId, model.StructureId ?? string.Empty, lastDay, cancellationToken);

        if (!ended.IsAuthorised)
        {
            return Forbid();
        }

        if (!ended.Succeeded)
        {
            foreach (var error in ended.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Message.Value);
            }

            return View(model);
        }

        await _notifier.SuccessAsync(H["{0} placement(s) ended on {1}.",
            ended.Value, EmployeeDisplay.Date(lastDay)]);

        return RedirectToAction(nameof(Index), new { id = model.EmployeeId });
    }

    /// <summary>
    /// The axes to choose from, and — once one is chosen — the units on it that may hold employees.
    /// </summary>
    /// <remarks>
    /// The unit list is filtered by the axis's own employee-attachment rule, so the picker offers
    /// what the validator would accept. That is the ADR-0010 lesson applied to a second rule: the
    /// forwards reading (what a picker offers) and the backwards reading (what a validator refuses)
    /// have to come from the same place, or they disagree on exactly the case the rule exists for.
    ///
    /// Units are read from the structure's own level types rather than by walking the tree, because
    /// the question is "which units may hold people", not "what does the chart look like".
    /// </remarks>
    private async Task FillChoicesAsync(PlaceEmployeeViewModel model, CancellationToken cancellationToken)
    {
        var structures = await _structures.ListAsync(cancellationToken);

        model.Structures =
        [
            .. structures.Select(structure => new StructureOptionViewModel
            {
                StructureId = structure.StructureId,
                Code = structure.Code,
                NameEn = structure.Name.En,
                NameAr = structure.Name.Ar,
            }),
        ];

        if (string.IsNullOrEmpty(model.StructureId))
        {
            return;
        }

        var chosen = structures.FirstOrDefault(structure =>
            string.Equals(structure.StructureId, model.StructureId, StringComparison.Ordinal));

        if (chosen is null)
        {
            return;
        }

        var date = IsoDate.TryParse(model.EffectiveFrom, out var parsed)
            ? parsed
            : await _authorisation.TodayAsync();

        var units = await _graph.GetEmployeeAttachableUnitsAsync(
            chosen.StructureId, date, cancellationToken);

        model.Units = [.. units.Select(node => UnitOptionViewModel.Of(node))];
    }
}
