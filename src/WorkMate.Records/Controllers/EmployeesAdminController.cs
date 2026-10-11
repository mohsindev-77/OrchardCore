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
    private readonly EmployeeUnitNames _units;
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
        EmployeeUnitNames units,
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
        _units = units;
        _authorisation = authorisation;
        _authorizationService = authorizationService;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    /// <summary>
    /// The employee list, optionally narrowed to one unit on one axis as at one date.
    /// </summary>
    /// <remarks>
    /// One action rather than a second screen, because everything the unfiltered list already does
    /// — search, status filter, paging, the row actions — is wanted when it is filtered too, and a
    /// second screen would be a second copy of all of it drifting from this one. The unit is three
    /// query-string parameters; absent them this is the list it has always been.
    /// </remarks>
    public async Task<IActionResult> Index(
        string? search,
        EmploymentStatus? status,
        string? structureId,
        string? recordId,
        string? asAt,
        bool includeSubUnits = false,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ViewEmployees))
        {
            return Forbid();
        }

        var unit = await UnitFilterAsync(structureId, recordId, asAt, includeSubUnits, cancellationToken);

        // The members of the unit, when there is one. Resolved before the list, because the list is
        // then narrowed to them: unit membership is an assignment row in the dimension engine and
        // deliberately not a column on the employee record.
        var membership = unit is null
            ? null
            : await MembershipAsync(unit, cancellationToken);

        var page = await _employees.ListAsync(
            search, status, skip, take, membership?.EveryoneListed, cancellationToken);

        var model = new EmployeeListViewModel
        {
            Employees = [.. page.Items.Select(EmployeeListItemViewModel.Of)],
            Search = search,
            Status = status,
            Total = page.Total,
            Skip = skip,
            Take = take,
            Unit = unit,
            CanManage = await _authorisation.AuthoriseAsync(Permissions.ManageEmployees),
            CanChangeStatus = await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus),
            CanAssign = await _authorisation.AuthoriseAsync(
                WorkMate.Dimensions.Permissions.AssignEmployees),
        };

        // On the structure being looked at when there is one, and on the primary organisation
        // otherwise. An "Additional" row only means anything if the main placement beside it is
        // read off the same structure — and a tenant whose organisation chart is not the structure
        // nominated as primary (every tenant until somebody populates that one) would otherwise
        // show "Not placed" against people who are plainly placed.
        await FillPlacementsAsync(model.Employees, unit?.StructureId, cancellationToken);

        if (unit is not null && membership is not null)
        {
            FillRoles(model.Employees, membership);

            await FillHeadshipsAsync(model.Employees, unit.AsAtIso, cancellationToken);
        }

        return View(model);
    }

    /// <summary>
    /// The unit the list is filtered to, resolved and counted, or null when it is not filtered.
    /// </summary>
    /// <remarks>
    /// The count comes from the same <c>IEmployeeAssignmentService</c> call that draws the designer
    /// card the reader clicked, so the header and the card show one number. Computing it from the
    /// list instead would make them disagree the moment somebody typed in the search box, which is
    /// precisely when a reader is most likely to be comparing them.
    /// </remarks>
    private async Task<UnitFilterViewModel?> UnitFilterAsync(
        string? structureId,
        string? recordId,
        string? asAt,
        bool includeSubUnits,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(structureId) || string.IsNullOrEmpty(recordId))
        {
            return null;
        }

        var structure = await _structures.GetAsync(structureId, cancellationToken);
        var date = IsoDate.TryParse(asAt, out var parsed) ? parsed : await _authorisation.TodayAsync();
        var record = await _dimensions.GetAsync(recordId, date, cancellationToken);

        if (structure is null || record is null)
        {
            return null;
        }

        var total = includeSubUnits
            ? (await _assignments.GetEmployeesUnderAsync(
                structureId, recordId, date, includeDescendants: true, skip: 0, take: 1, cancellationToken)).Total
            : (await _assignments.CountEmployeesAtAsync(structureId, [recordId], date, cancellationToken))
                .GetValueOrDefault(recordId);

        return new UnitFilterViewModel(
            structureId,
            recordId,
            BilingualText.Display(structure.Name.En, structure.Name.Ar),
            BilingualText.Display(record.NameEn, record.NameAr),
            date.ToIso(),
            includeSubUnits,
            total);
    }

    /// <summary>
    /// Who is at the unit, what each of them is there, and who heads it.
    /// </summary>
    /// <remarks>
    /// The head is listed whether or not they are placed in the unit — ADR-0012 — because "who runs
    /// this department" is the first thing anybody asks of a department, and a head borrowed from
    /// another department is ordinary. They are not added to the count, because they work somewhere
    /// else and that somewhere else is already counting them; counting them here would make the
    /// company's headcount larger than the company.
    /// </remarks>
    private async Task<UnitMembership> MembershipAsync(
        UnitFilterViewModel unit,
        CancellationToken cancellationToken)
    {
        var date = IsoDate.TryParse(unit.AsAtIso, out var parsed) ? parsed : await _authorisation.TodayAsync();

        var rows = await _assignments.GetAssignmentsAtAsync(
            unit.StructureId, unit.RecordId, date, unit.IncludeSubUnits, cancellationToken);

        var byEmployee = rows
            .GroupBy(row => row.EmployeeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                // Primary first: somebody with a primary and a secondary at the same unit — which
                // a split allocation across a subtree produces — is primarily there.
                group => group.OrderByDescending(row => row.IsPrimary).First(),
                StringComparer.Ordinal);

        // The head of the unit itself, never of a sub-unit: "who runs this" has one answer, and
        // listing every sub-unit's head as though they ran this one would be several.
        var head = await _assignments.GetHeadAsync(unit.StructureId, unit.RecordId, date, cancellationToken);

        var listed = byEmployee.Keys.ToHashSet(StringComparer.Ordinal);

        if (head is not null)
        {
            listed.Add(head.EmployeeId);
        }

        return new UnitMembership(byEmployee, head?.EmployeeId, listed);
    }

    private static void FillRoles(IReadOnlyList<EmployeeListItemViewModel> employees, UnitMembership membership)
    {
        foreach (var employee in employees)
        {
            employee.IsHeadHere = string.Equals(
                employee.EmployeeId, membership.HeadEmployeeId, StringComparison.Ordinal);

            if (membership.Rows.TryGetValue(employee.EmployeeId, out var row))
            {
                employee.RoleHere = row.IsPrimary ? UnitRole.Primary : UnitRole.Secondary;
                employee.AllocationPercent = row.AllocationPercent;
            }
            else
            {
                // Listed and not placed: the head who is not a member. No allocation, because an
                // appointment carries none.
                employee.RoleHere = UnitRole.HeadOnly;
            }
        }
    }

    /// <summary>Who is at a unit, how, and who heads it.</summary>
    private sealed record UnitMembership(
        IReadOnlyDictionary<string, EmployeeAssignment> Rows,
        string? HeadEmployeeId,
        IReadOnlyCollection<string> EveryoneListed);
    /// <summary>
    /// Fills in where each person on the page sits, through the one service that answers it.
    /// </summary>
    /// <remarks>
    /// <see cref="EmployeeUnitNames"/> rather than a second copy here. This screen and the employee
    /// picker both show "where they sit" and used to disagree: the picker fell back to another
    /// structure when the primary organisation had nothing on it and this did not, so the same
    /// person read "Mechanical" on one and "Not placed" on the other. There is one answer to the
    /// question now and one place it is worked out.
    ///
    /// <b>The main placement and the others, not one row of several.</b> Somebody split 60/40 has
    /// two true answers, and this column used to show whichever the engine returned first.
    /// </remarks>
    private async Task FillPlacementsAsync(
        IReadOnlyList<EmployeeListItemViewModel> employees,
        string? onStructureId,
        CancellationToken cancellationToken)
    {
        if (employees.Count == 0)
        {
            return;
        }

        var summaries = await _units.SummariesAsync(
            [.. employees.Select(employee => employee.EmployeeId)],
            onStructureId,
            await _authorisation.TodayAsync(),
            cancellationToken);

        foreach (var employee in employees)
        {
            if (!summaries.TryGetValue(employee.EmployeeId, out var summary))
            {
                continue;
            }

            employee.PrimaryUnitName = summary.Main?.Name;
            employee.PrimaryUnitPath = summary.Main?.Path;
            employee.OtherUnits = summary.Others;
        }
    }

    /// <summary>
    /// Names the units each starred person heads, for the star's tooltip.
    /// </summary>
    /// <remarks>
    /// Only for the people who are starred, which on a unit-filtered list is at most one: the star
    /// marks the head of <em>this</em> unit. A query per listed person would be fifty to label one.
    ///
    /// Every unit they head, though, not only this one — ADR-0012 allows somebody to head several,
    /// and the Zenith demo has a person over two departments and a site team. "Heads this unit" is
    /// the least useful true thing available to say to that reader.
    /// </remarks>
    private async Task FillHeadshipsAsync(
        IReadOnlyList<EmployeeListItemViewModel> employees,
        string asAtIso,
        CancellationToken cancellationToken)
    {
        var date = IsoDate.TryParse(asAtIso, out var parsed) ? parsed : await _authorisation.TodayAsync();

        foreach (var employee in employees.Where(employee => employee.IsHeadHere))
        {
            var names = new List<string>();

            foreach (var headship in await _assignments.GetHeadshipsOfAsync(
                employee.EmployeeId, date, cancellationToken))
            {
                var unit = await _dimensions.GetAsync(headship.RecordId, date, cancellationToken);

                if (unit is not null)
                {
                    names.Add(BilingualText.Display(unit.NameEn, unit.NameAr));
                }
            }

            // Composed here, where the localiser and the culture are. A view joining the names
            // with ", " would be writing English punctuation into an Arabic sentence — Arabic uses
            // "، ", which is a different character — so even the separator is a resource. The same
            // reason DesignerNodeViewModel.HeadLabel is built on the server.
            employee.HeadsLabel = names.Count > 0
                ? S["Heads {0}", string.Join(S[", "].Value, names)].Value
                : S["Heads this unit"].Value;
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

        // The editor, told that this is a brand-new record, so it can offer the step the person
        // almost certainly came to take next rather than leaving them to find it.
        return RedirectToAction(nameof(Edit), new { id = created.Value.EmployeeId, created = true });
    }

    /// <param name="created">
    /// True when arriving straight from Add, so the editor offers "Activate now" rather than
    /// reporting "Prospective" and leaving the next step unlabelled.
    /// </param>
    public async Task<IActionResult> Edit(string id, bool created, CancellationToken cancellationToken)
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

        var model = ToEditModel(employee);

        model.IsNewlyCreated = created;
        model.CanChangeStatus = await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus);

        return View(model);
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
            model.CanChangeStatus = await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus);

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
