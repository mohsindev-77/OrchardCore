using Microsoft.AspNetCore.Authorization;
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
/// The employee list, and the one screen that creates an employee.
/// </summary>
/// <remarks>
/// Every action calls <see cref="IEmployeeService"/> — the same service the recipe step and any
/// future API use — so nothing here decides a rule the service does not also enforce. The
/// permission checks are for the screen's own sake: what to show, and what to refuse before a POST.
///
/// <b>Why this module ships a list at all</b>, when the <c>Employee</c> type is listable and
/// Orchard has a perfectly good content list: an employee list wants a status filter, a search that
/// matches a code and both halves of a bilingual name, and the unit each person sits in — which
/// comes from the dimension engine and is not on the record at all. None of that is expressible on
/// the generic screen.
/// </remarks>
[Admin("Employees/{action}/{id?}", "Employees{action}")]
public sealed class EmployeesAdminController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IStructureService _structures;
    private readonly IDimensionService _dimensions;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    public EmployeesAdminController(
        IEmployeeService employees,
        IEmployeeAssignmentService assignments,
        IStructureService structures,
        IDimensionService dimensions,
        IRecordsAuthorisation authorisation,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IStringLocalizer<EmployeesAdminController> stringLocalizer,
        IHtmlLocalizer<EmployeesAdminController> htmlLocalizer)
    {
        _employees = employees;
        _assignments = assignments;
        _structures = structures;
        _dimensions = dimensions;
        _authorisation = authorisation;
        _authorizationService = authorizationService;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    public async Task<IActionResult> Index(
        string? search,
        EmploymentStatus? status,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ViewEmployees))
        {
            return Forbid();
        }

        var page = await _employees.ListAsync(search, status, skip, take, cancellationToken);

        var model = new EmployeeListViewModel
        {
            Employees = [.. page.Items.Select(EmployeeListItemViewModel.Of)],
            Search = search,
            Status = status,
            Total = page.Total,
            Skip = skip,
            Take = take,
            CanManage = await _authorisation.AuthoriseAsync(Permissions.ManageEmployees),
            CanChangeStatus = await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus),
            CanAssign = await _authorisation.AuthoriseAsync(
                WorkMate.Dimensions.Permissions.AssignEmployees),
        };

        await FillPrimaryUnitsAsync(model.Employees, cancellationToken);

        return View(model);
    }

    /// <summary>
    /// Fills in where each person on the page sits today, on the primary organisation axis.
    /// </summary>
    /// <remarks>
    /// One query for the whole page rather than one per row — <c>GetAllAxesAsync</c> per employee
    /// would be the N+1 that a list screen most easily grows — and then one batched name lookup for
    /// the units it found. A page of fifty therefore costs two queries, not a hundred.
    ///
    /// Silently skipped when the tenant has nominated no primary organisation axis, which a brand
    /// new tenant has not: the column is simply empty, which is true.
    /// </remarks>
    private async Task FillPrimaryUnitsAsync(
        IReadOnlyList<EmployeeListItemViewModel> employees,
        CancellationToken cancellationToken)
    {
        if (employees.Count == 0)
        {
            return;
        }

        var organisation = await _structures.GetPrimaryOrganisationAsync(cancellationToken);

        if (organisation is null)
        {
            return;
        }

        var today = await _authorisation.TodayAsync();
        var placements = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var employee in employees)
        {
            var assignment = await _assignments.GetEffectiveAsync(
                employee.EmployeeId, organisation.StructureId, today, cancellationToken);

            if (assignment is not null)
            {
                placements[employee.EmployeeId] = assignment.RecordId;
            }
        }

        foreach (var employee in employees)
        {
            if (placements.TryGetValue(employee.EmployeeId, out var recordId))
            {
                var unit = await _dimensions.GetAsync(recordId, today, cancellationToken);

                employee.PrimaryUnitName = unit is null
                    ? null
                    : BilingualText.Display(unit.NameEn, unit.NameAr);
            }
        }
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ManageEmployees))
        {
            return Forbid();
        }

        return View(new EmployeeCreateViewModel
        {
            // A suggestion on the form, not a default on the write: the service refuses a create
            // with no join date, and a person can see and change this before committing.
            JoinDate = (await _authorisation.TodayAsync()).ToIso(),
        });
    }

    [HttpPost]
    [ActionName(nameof(Create))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(EmployeeCreateViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageEmployees))
        {
            return Forbid();
        }

        if (!IsoDate.TryParse(model.JoinDate, out var joinDate))
        {
            ModelState.AddModelError(nameof(model.JoinDate), S["A join date is needed."].Value);

            return View(model);
        }

        DateOnly? dateOfBirth = IsoDate.TryParse(model.DateOfBirth, out var born) ? born : null;

        var created = await _employees.CreateAsync(
            model.Code ?? string.Empty,
            new BilingualText(model.NameEn ?? string.Empty, model.NameAr ?? string.Empty),
            joinDate,
            new EmployeeDetails(
                dateOfBirth,
                model.NationalityCode,
                model.Gender,
                model.LineManagerEmployeeId),
            cancellationToken);

        if (!created.IsAuthorised)
        {
            return Forbid();
        }

        if (!created.Succeeded)
        {
            AddErrors(created.Errors);

            return View(model);
        }

        await _notifier.SuccessAsync(H["{0} was added, and is prospective until somebody activates them.",
            created.Value!.NameEn]);

        return RedirectToAction(nameof(Edit), new { id = created.Value.EmployeeId });
    }

    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
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

        return View(ToEditModel(employee));
    }

    [HttpPost]
    [ActionName(nameof(Edit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(EmployeeEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageEmployees))
        {
            return Forbid();
        }

        var employee = await _employees.GetAsync(model.EmployeeId, cancellationToken);

        if (employee is null)
        {
            return NotFound();
        }

        DateOnly? dateOfBirth = IsoDate.TryParse(model.DateOfBirth, out var born) ? born : null;

        var updated = await _employees.UpdateAsync(
            model.EmployeeId,
            new BilingualText(model.NameEn ?? string.Empty, model.NameAr ?? string.Empty),
            new EmployeeDetails(
                dateOfBirth,
                model.NationalityCode,
                model.Gender,
                model.LineManagerEmployeeId,
                model.PhotoPath),
            cancellationToken);

        if (!updated.IsAuthorised)
        {
            return Forbid();
        }

        if (!updated.Succeeded)
        {
            AddErrors(updated.Errors);

            // Re-shown with what the record still says about the things this form cannot change,
            // so a failed save does not blank the status or the code on screen.
            model.Code = employee.Code;
            model.Status = employee.Status;
            model.JoinDate = employee.JoinDate;

            return View(model);
        }

        await _notifier.SuccessAsync(H["{0} was updated.", updated.Value!.NameEn]);

        return RedirectToAction(nameof(Index));
    }

    private static EmployeeEditViewModel ToEditModel(EmployeeRecord employee) => new()
    {
        EmployeeId = employee.EmployeeId,
        Code = employee.Code,
        NameEn = employee.NameEn,
        NameAr = employee.NameAr,
        DateOfBirth = employee.DateOfBirth?.ToIso(),
        NationalityCode = employee.NationalityCode,
        Gender = employee.Gender,
        LineManagerEmployeeId = employee.LineManagerEmployeeId,
        PhotoPath = employee.PhotoPath,
        Status = employee.Status,
        JoinDate = employee.JoinDate,
    };

    /// <summary>
    /// Puts each rule violation where the reader has to act, and the rest in the summary.
    /// </summary>
    /// <remarks>
    /// <c>RecordError.Field</c> is a hint the service offers and this is the caller that uses it.
    /// A message about the employee code belongs under the employee code box, not in a list at the
    /// top that the reader has to match up themselves.
    /// </remarks>
    private void AddErrors(IReadOnlyList<RecordError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
        }
    }
}
