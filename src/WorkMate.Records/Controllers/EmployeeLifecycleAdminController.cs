using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Core;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Controllers;

/// <summary>
/// The five dated lifecycle transitions, each its own small form.
/// </summary>
/// <remarks>
/// Each is a separate action rather than one screen with a dropdown, for the reason the dimension
/// designer gives every action its own page: these commit dated decisions with consequences, and a
/// form that could do five different things is a form somebody submits having changed their mind
/// halfway down it.
///
/// <b>The exit is a dry run first.</b> <c>GET Exit</c> computes what ending employment would close —
/// every placement on every axis, and every unit the person heads — and <c>POST</c> commits it. The
/// same dry-run-then-apply shape as a move, a merge, a retirement and a level change, and here for
/// the sharpest reason of the lot: a department losing its approver on Friday is something somebody
/// has to act on, and finding out from a stuck approval the following week is too late.
/// </remarks>
[Admin("Employees/Lifecycle/{action}/{id?}", "EmployeeLifecycle{action}")]
public sealed class EmployeeLifecycleAdminController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly IStructureService _structures;
    private readonly IDimensionService _dimensions;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    /// <summary>The view every transition but the exit renders.</summary>
    private const string TransitionViewName = "Transition";

    public EmployeeLifecycleAdminController(
        IEmployeeService employees,
        IStructureService structures,
        IDimensionService dimensions,
        IRecordsAuthorisation authorisation,
        INotifier notifier,
        IStringLocalizer<EmployeeLifecycleAdminController> stringLocalizer,
        IHtmlLocalizer<EmployeeLifecycleAdminController> htmlLocalizer)
    {
        _employees = employees;
        _structures = structures;
        _dimensions = dimensions;
        _authorisation = authorisation;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    public Task<IActionResult> Activate(string id, CancellationToken cancellationToken) =>
        TransitionFormAsync(id, EmploymentStatus.Active, cancellationToken);

    [HttpPost]
    [ActionName(nameof(Activate))]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivatePost(EmployeeTransitionViewModel model, CancellationToken cancellationToken) =>
        CommitAsync(model, EmploymentStatus.Active, _employees.ActivateAsync, cancellationToken);

    public Task<IActionResult> PutOnLeave(string id, CancellationToken cancellationToken) =>
        TransitionFormAsync(id, EmploymentStatus.OnLeave, cancellationToken);

    [HttpPost]
    [ActionName(nameof(PutOnLeave))]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> PutOnLeavePost(EmployeeTransitionViewModel model, CancellationToken cancellationToken) =>
        CommitAsync(model, EmploymentStatus.OnLeave, _employees.PutOnLeaveAsync, cancellationToken);

    public Task<IActionResult> Suspend(string id, CancellationToken cancellationToken) =>
        TransitionFormAsync(id, EmploymentStatus.Suspended, cancellationToken);

    [HttpPost]
    [ActionName(nameof(Suspend))]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SuspendPost(EmployeeTransitionViewModel model, CancellationToken cancellationToken) =>
        CommitAsync(model, EmploymentStatus.Suspended, _employees.SuspendAsync, cancellationToken);

    public Task<IActionResult> Reinstate(string id, CancellationToken cancellationToken) =>
        TransitionFormAsync(id, EmploymentStatus.Active, cancellationToken);

    [HttpPost]
    [ActionName(nameof(Reinstate))]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ReinstatePost(EmployeeTransitionViewModel model, CancellationToken cancellationToken) =>
        CommitAsync(model, EmploymentStatus.Active, _employees.ReinstateAsync, cancellationToken);

    // ---- the exit --------------------------------------------------------------------

    /// <summary>
    /// The dry run: what ending employment on a date would close.
    /// </summary>
    /// <remarks>
    /// Shown with no plan on the first visit, because there is no date yet and a plan needs one.
    /// Posting the date to <see cref="ExitPreview"/> fills it in; only <see cref="ExitPost"/>
    /// commits anything.
    /// </remarks>
    public async Task<IActionResult> Exit(string id, CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(id, cancellationToken);

        if (employee is null)
        {
            return NotFound();
        }

        return View(new EmployeeExitViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            CurrentStatus = employee.Status,
            LastDay = (await _authorisation.TodayAsync()).ToIso(),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExitPreview(EmployeeExitViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return Forbid();
        }

        var filled = await FillExitPlanAsync(model, cancellationToken);

        return filled is null ? NotFound() : View(nameof(Exit), filled);
    }

    [HttpPost]
    [ActionName(nameof(Exit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExitPost(EmployeeExitViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return Forbid();
        }

        if (!IsoDate.TryParse(model.LastDay, out var lastDay))
        {
            ModelState.AddModelError(nameof(model.LastDay), S["A last day of service is needed."].Value);

            return View(nameof(Exit), await FillExitPlanAsync(model, cancellationToken) ?? model);
        }

        var exit = await _employees.ExitAsync(model.EmployeeId, lastDay, cancellationToken);

        if (!exit.IsAuthorised)
        {
            return Forbid();
        }

        if (!exit.Succeeded)
        {
            foreach (var error in exit.Errors)
            {
                ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
            }

            return View(nameof(Exit), await FillExitPlanAsync(model, cancellationToken) ?? model);
        }

        var result = exit.Value!;

        await _notifier.SuccessAsync(H["{0} left on {1}. {2} placement(s) and {3} headship(s) were closed.",
            result.Employee.NameEn,
            EmployeeDisplay.Date(result.LastDayOfService),
            result.AssignmentsClosed,
            result.HeadshipsClosed]);

        // Said separately, and as a warning rather than a success, because it is the half somebody
        // has to do something about.
        foreach (var unit in result.UnitsLeftWithoutAHead)
        {
            await _notifier.WarningAsync(H["'{0}' now has no head. Appoint one on the organisation designer.",
                unit.NameEn]);
        }

        return RedirectToAction(nameof(EmployeesAdminController.Index), "EmployeesAdmin");
    }

    /// <summary>
    /// Computes the dry run and names everything in it, or null when the employee has gone.
    /// </summary>
    private async Task<EmployeeExitViewModel?> FillExitPlanAsync(
        EmployeeExitViewModel model,
        CancellationToken cancellationToken)
    {
        var employee = await _employees.GetAsync(model.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return null;
        }

        model.Code = employee.Code;
        model.NameEn = employee.NameEn;
        model.NameAr = employee.NameAr;
        model.CurrentStatus = employee.Status;

        if (!IsoDate.TryParse(model.LastDay, out var lastDay))
        {
            return model;
        }

        var plan = await _employees.PlanExitAsync(model.EmployeeId, lastDay, cancellationToken);

        if (!plan.Succeeded)
        {
            foreach (var error in plan.Errors)
            {
                ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
            }

            return model;
        }

        var placements = new List<ExitPlacementViewModel>();

        foreach (var assignment in plan.Value!.Assignments)
        {
            var structure = await _structures.GetAsync(assignment.StructureId, cancellationToken);
            var unit = await _dimensions.GetAsync(assignment.RecordId, lastDay, cancellationToken);

            placements.Add(new ExitPlacementViewModel
            {
                StructureName = structure is null
                    ? assignment.StructureId
                    : BilingualText.Display(structure.Name.En, structure.Name.Ar),
                UnitName = unit is null
                    ? assignment.RecordId
                    : BilingualText.Display(unit.NameEn, unit.NameAr),
                UnitCode = unit?.Code,
                AllocationPercent = assignment.AllocationPercent,
                IsPrimary = assignment.IsPrimary,
            });
        }

        model.HasPlan = true;
        model.Placements = placements;
        model.UnitsLosingTheirHead = [.. plan.Value.UnitsLosingTheirHead.Select(VacatedUnitViewModel.Of)];

        return model;
    }

    // ---- the shared transition form ---------------------------------------------------

    private async Task<IActionResult> TransitionFormAsync(
        string id,
        EmploymentStatus to,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(id, cancellationToken);

        if (employee is null)
        {
            return NotFound();
        }

        return View(TransitionViewName, new EmployeeTransitionViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            CurrentStatus = employee.Status,
            CurrentStatusFrom = employee.StatusEffectiveFrom,
            To = to,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
        });
    }

    private async Task<IActionResult> CommitAsync(
        EmployeeTransitionViewModel model,
        EmploymentStatus to,
        Func<string, DateOnly, CancellationToken, Task<RecordResult<EmployeeRecord>>> transition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(transition);

        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
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
        model.CurrentStatus = employee.Status;
        model.CurrentStatusFrom = employee.StatusEffectiveFrom;
        model.To = to;

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(
                nameof(model.EffectiveFrom),
                S["A date is needed. Nothing here is defaulted on a write."].Value);

            return View(TransitionViewName, model);
        }

        var result = await transition(model.EmployeeId, effectiveFrom, cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
            }

            return View(TransitionViewName, model);
        }

        await _notifier.SuccessAsync(H["{0} is {1} from {2}.",
            result.Value!.NameEn,
            result.Value.Status.ToString(),
            EmployeeDisplay.Date(effectiveFrom)]);

        return RedirectToAction(nameof(EmployeesAdminController.Index), "EmployeesAdmin");
    }
}
