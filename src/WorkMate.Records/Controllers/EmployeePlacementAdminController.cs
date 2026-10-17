using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;
using WorkMate.Records.Services;
using WorkMate.Records.ViewModels;

namespace WorkMate.Records.Controllers;

/// <summary>
/// Where one employee sits, on every structure, and the dated operations that change it.
/// </summary>
/// <remarks>
/// <b>Placement is the brief's "Placement section", and it is a screen rather than a driver on the
/// employee editor.</b> A display driver's <c>UpdateAsync</c> has nowhere to put a required
/// effective date: it is handed a posted model and asked to write it. Every write here is dated and
/// nothing is defaulted, so the editor shows placement read-only and links here, and each operation
/// gets its own small form with its own date — exactly the shape the organisation designer's
/// actions already have.
///
/// <b>Three operations, one write path each.</b> <see cref="Place"/> adds one placement,
/// <see cref="Change"/> states the whole split, and <see cref="End"/> closes everything on a
/// structure. All three go through <see cref="IEmployeeAssignmentService"/>, and the first two
/// through the same <c>ReallocateAsync</c> the <c>employee-assignments</c> recipe step calls —
/// <c>PlaceAsync</c> is a one-row reallocation, not a second mechanism. A second write path is how
/// a screen and an import come to disagree about what a split means.
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
    private readonly PlacementSplitMessages _splitMessages;
    private readonly IPositionService _positions;
    private readonly IEmployeeOrganisationService _organisation;
    private readonly AccessRoleReminder _accessRoles;
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
        PlacementSplitMessages splitMessages,
        IPositionService positions,
        IEmployeeOrganisationService organisation,
        AccessRoleReminder accessRoles,
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
        _splitMessages = splitMessages;
        _positions = positions;
        _organisation = organisation;
        _accessRoles = accessRoles;
        _authorisation = authorisation;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    /// <summary>Where this employee sits on every structure on a date, and what they lead.</summary>
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

        // The axes an employee can be placed on, which is not every structure: a grade ladder and a
        // reporting line are axes the Position type is a level of and neither is anywhere a person
        // goes. ADR-0030.
        foreach (var axis in await _positions.GetEmployeeAxesAsync(cancellationToken))
        {
            var structure = axis.Structure;

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
                IsPositionBased = axis.IsPositionBased,

                // Main first, which is what "where does this person work" means.
                Rows = [.. placements.OrderByDescending(row => row.IsPrimary).ThenBy(row => row.UnitName)],
            });
        }

        model.Axes = axes;

        // What the position they hold means — the chain, the grade, who they report to — or the
        // "No position yet" state ADR-0029 leaves behind and D28 C requires be made legible.
        model.Organisation = await _organisation.GetAsync(employee.EmployeeId, date, cancellationToken);

        // Who manages them, from the ONE read path. D31 addendum 2: derived live where the chart
        // answers, stored only where somebody chose — so a change of holder above shows up here
        // without anybody editing this record.
        model.LineManager = await _organisation.GetLineManagerAsync(
            employee.EmployeeId, date, cancellationToken);

        model.JobHistory = await JobHistoryAsync(employee.EmployeeId, cancellationToken);
        model.Headships = await HeadshipsAsync(employee.EmployeeId, date, cancellationToken);

        return View(model);
    }

    /// <summary>
    /// Every dated row this person has held, newest first: position, where it sat, grade, from, to.
    /// </summary>
    /// <remarks>
    /// <b>The organisation path is resolved as at each row's own start date</b>, not as at today.
    /// That is the whole reason this is worth a screen: a department renamed in June, or a position
    /// moved from one division to another, must read on a March row as it read in March. Resolving
    /// it as at today would make the history quietly agree with the present, which is the one thing
    /// a history must not do.
    ///
    /// <b>Only rows on axes an employee can be placed on</b>, and only the ones at a position or a
    /// unit — never a grade ladder or a reporting line, which nothing writes to anyway (ADR-0028).
    /// </remarks>
    private async Task<IReadOnlyList<JobHistoryRowViewModel>> JobHistoryAsync(
        string employeeId,
        CancellationToken cancellationToken)
    {
        var rows = new List<JobHistoryRowViewModel>();

        foreach (var axis in await _positions.GetEmployeeAxesAsync(cancellationToken))
        {
            foreach (var row in await _assignments.GetHistoryAsync(
                employeeId, axis.StructureId, cancellationToken))
            {
                var node = await _dimensions.GetAsync(row.RecordId, row.Range.From, cancellationToken);

                if (node is null)
                {
                    continue;
                }

                // As at the row's own start, so a rename or a move after the fact does not rewrite
                // what this period says.
                var paths = await _graph.GetAncestorPathsAsync(
                    axis.StructureId, [row.RecordId], row.Range.From, cancellationToken);

                var grade = row.GradeRecordId is null
                    ? null
                    : await _dimensions.GetAsync(row.GradeRecordId, DateOnly.MaxValue, cancellationToken);

                rows.Add(new JobHistoryRowViewModel
                {
                    RecordId = row.RecordId,
                    Name = BilingualDisplay.Name(node.NameEn, node.NameAr),
                    OrganisationPath = string.Join(
                        BilingualDisplay.PathSeparator,
                        (paths.GetValueOrDefault(row.RecordId) ?? [])
                            .Select(step => BilingualDisplay.Name(step.NameEn, step.NameAr))),
                    GradeName = grade is null
                        ? string.Empty
                        : BilingualDisplay.Name(grade.NameEn, grade.NameAr),
                    From = row.Range.From,
                    To = row.Range.To,
                });
            }
        }

        // Newest first: a history is read backwards from now.
        return [.. rows.OrderByDescending(row => row.From).ThenBy(row => row.Name, StringComparer.Ordinal)];
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

    // ---- place one person at one unit --------------------------------------------------

    /// <param name="id">
    /// The employee, when the screen was opened from a person. Absent when it was opened from a
    /// unit's card on the organisation designer, in which case the form asks who.
    /// </param>
    /// <param name="recordId">The unit, when the card already knows it.</param>
    /// <param name="asAt">
    /// The date the designer was showing, so a reader working on last month's chart gets last
    /// month's date suggested rather than today's. A suggestion on the form, never a default on the
    /// write.
    /// </param>
    public async Task<IActionResult> Place(
        string? id,
        string? structureId,
        string? recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees))
        {
            return Forbid();
        }

        var model = new PlaceEmployeeViewModel
        {
            StructureId = structureId,
            RecordId = recordId,
            EffectiveFrom = (IsoDate.TryParse(asAt, out var shown)
                ? shown
                : await _authorisation.TodayAsync()).ToIso(),
        };

        if (!string.IsNullOrEmpty(id))
        {
            var employee = await _employees.GetAsync(id, cancellationToken);

            if (employee is null)
            {
                return NotFound();
            }

            model.EmployeeId = employee.EmployeeId;
            model.Code = employee.Code;
            model.NameEn = employee.NameEn;
            model.NameAr = employee.NameAr;
        }

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

        var employee = string.IsNullOrEmpty(model.EmployeeId)
            ? null
            : await _employees.GetAsync(model.EmployeeId, cancellationToken);

        if (employee is not null)
        {
            model.Code = employee.Code;
            model.NameEn = employee.NameEn;
            model.NameAr = employee.NameAr;
        }

        // FIRST, AND ON EVERY PATH THROUGH THIS ACTION. Whether this axis places employees at
        // positions is a property of the tenant's chart, so it is read from the chart and never
        // from the post — the form does not carry it, and a form that did could be told the wrong
        // answer.
        //
        // It has to be settled before anything below reads it. Without this, IsPositionBased
        // arrived false from a post that never sent it, and every position-shaped branch of the
        // write quietly took the other road: the D26 I access-roles reminder was never raised, and
        // the "always 100 per cent, always the main placement" rule fell back to whatever the
        // hidden allocation fields happened to hold.
        await FillChoicesAsync(model, cancellationToken);

        // Changing the structure is navigation, not a save. It used to run the whole save path and
        // answer "Choose a structure and a unit" before anybody had chosen either — a form telling
        // somebody off for not finishing a question they had only just been asked. A refresh
        // redraws with the new axis's choices and validates nothing.
        if (model.IsRefresh)
        {
            // ONLY WHEN THE AXIS CHANGED. The unit or position belonged to the axis that was
            // showing, so it is dropped rather than carried onto one where it means nothing.
            //
            // The two refreshes have to be told apart since D31: the position picker now redraws
            // the form too, so that the derived block and the line manager follow the choice. A
            // redraw that cleared the position would clear it the instant it was chosen, and the
            // control would be unusable.
            if (model.AxisChanged)
            {
                model.RecordId = null;
                model.GradeRecordId = null;
            }

            model.IsRefresh = false;
            model.AxisChanged = false;

            // Again, so the redraw does not describe a choice that has just been dropped.
            await FillChoicesAsync(model, cancellationToken);

            return View(model);
        }

        if (employee is null)
        {
            // Not a 404. Arriving from a unit's card there is no employee yet, so "choose one" is
            // the form's own validation rather than a missing record.
            ModelState.AddModelError(nameof(model.EmployeeId), S["Choose an employee."].Value);
        }
        else if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["A date is needed."].Value);
        }
        else if (string.IsNullOrEmpty(model.StructureId) || string.IsNullOrEmpty(model.RecordId))
        {
            ModelState.AddModelError(
                nameof(model.RecordId),
                model.IsPositionBased
                    ? S["Choose a position."].Value
                    : S["Choose a structure and a unit."].Value);
        }
        else
        {
            var placed = await _assignments.PlaceAsync(
                model.EmployeeId,
                model.StructureId,
                model.RecordId,
                effectiveFrom,
                // A position-based screen does not ask: the answer is always the main placement at
                // 100 per cent. The model keeps both so a later slice can build the second
                // concurrent position — acting, secondment — which ReallocateAsync already
                // expresses. D30: hiding a control is not removing a capability.
                model.IsPositionBased ? 100m : model.AllocationPercent,
                model.IsPositionBased || model.IsPrimary,
                // The grade, when the node chosen is a position (D26 G). Null for a unit, which is
                // what this screen has always placed into and what ADR-0029 keeps valid.
                model.GradeRecordId,
                cancellationToken);

            if (!placed.IsAuthorised)
            {
                return Forbid();
            }

            if (placed.Succeeded)
            {
                await _notifier.SuccessAsync(H["{0} was placed from {1}.",
                    employee.NameEn, EmployeeDisplay.Date(effectiveFrom)]);

                // D31 ADDENDUM 2: SETTLE THE LINE MANAGER WITH THE PLACEMENT.
                //
                // This screen rendered the control and never saved it, so a choice made here was
                // silently discarded. It writes only what cannot be derived — a pick among several
                // holders, or an override — and CLEARS otherwise, which is the ordinary outcome:
                // moving somebody into a post whose line already answers removes whatever a
                // previous choice left behind.
                if (model.IsPositionBased)
                {
                    var settled = await _employees.SetLineManagerAsync(
                        model.EmployeeId,
                        model.LineManagerEmployeeId,
                        model.LineManager.KindFor(model.LineManagerEmployeeId),
                        cancellationToken);

                    foreach (var error in settled.Errors)
                    {
                        await _notifier.WarningAsync(new LocalizedHtmlString(
                            error.Message.Name, error.Message.Value));
                    }
                }

                // D26 I. A position carries no permissions, so nothing is changed here — somebody
                // is told to look. One class raises it everywhere, so the security slice has one
                // thing to replace.
                if (model.IsPositionBased)
                {
                    await _accessRoles.RaiseAsync(employee.NameEn ?? employee.EmployeeId);
                }

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

    // ---- change the grade inside the position somebody already holds --------------------

    /// <summary>
    /// An increment: a new grade within the position they hold, from a date. Decision D26 G.
    /// </summary>
    /// <remarks>
    /// <b>Its own action because it is its own change.</b> A promotion or a transfer is a new
    /// <em>position</em>; an increment is a new <em>grade</em> within the one they hold, and the
    /// two produce different history and different questions. Putting both on one form would make
    /// somebody re-choose a position they are not changing.
    ///
    /// <b>It still writes a dated row</b>, because ADR-0026 put the grade on the assignment row:
    /// the current row closes the day before and a new one opens with the same position and the new
    /// grade. That is a cost ADR-0026 states rather than designs around, and it reads correctly —
    /// <em>"held HR Manager at HRM-1 from January, at HRM-2 from June"</em> is two rows and is what
    /// happened.
    /// </remarks>
    public async Task<IActionResult> ChangeGrade(
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

        var today = await _authorisation.TodayAsync();
        var held = await _organisation.GetAsync(employee.EmployeeId, today, cancellationToken);

        if (held?.Position is null)
        {
            // Nothing to change the grade of. Said rather than rendered as an empty form.
            await _notifier.WarningAsync(H["{0} holds no position, so there is no grade to change.",
                employee.NameEn ?? employee.EmployeeId]);

            return RedirectToAction(nameof(Index), new { id = employee.EmployeeId });
        }

        return View(new ChangeGradeViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            StructureId = structureId ?? held.StructureId,
            PositionRecordId = held.Position.PositionRecordId,
            PositionName = held.Position.PositionName,
            CurrentGradeName = held.GradeName,
            GradeRecordId = held.GradeRecordId,
            CurrentFrom = held.Since,
            EffectiveFrom = today.ToIso(),
        });
    }

    [HttpPost]
    [ActionName(nameof(ChangeGrade))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeGradePost(
        ChangeGradeViewModel model,
        CancellationToken cancellationToken)
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

            return View(model);
        }

        // Back-dating before the row that is open would not be a change to it — it would be a
        // statement about a period the row does not cover, and ReallocateAsync would close the row
        // on a day before it started. Refused with the date it would have to be on or after.
        if (model.CurrentFrom is { } from && effectiveFrom < from)
        {
            ModelState.AddModelError(
                nameof(model.EffectiveFrom),
                S["This grade would start before the position they hold did, on {0}. Choose that date or later.",
                    EmployeeDisplay.Date(from)].Value);

            return View(model);
        }

        var placed = await _assignments.PlaceAsync(
            model.EmployeeId,
            model.StructureId!,
            model.PositionRecordId!,
            effectiveFrom,
            gradeRecordId: model.GradeRecordId,
            cancellationToken: cancellationToken);

        if (!placed.IsAuthorised)
        {
            return Forbid();
        }

        if (!placed.Succeeded)
        {
            foreach (var error in placed.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Message.Value);
            }

            return View(model);
        }

        await _notifier.SuccessAsync(H["{0}'s grade was changed from {1}.",
            employee.NameEn, EmployeeDisplay.Date(effectiveFrom)]);

        return RedirectToAction(nameof(Index), new { id = model.EmployeeId });
    }

    // ---- change the whole split on one structure ---------------------------------------

    /// <summary>
    /// The employee's current placements on one structure, as editable rows.
    /// </summary>
    /// <remarks>
    /// One structure at a time, because a split is only meaningful within one: the rules are that
    /// the allocations total 100 and that exactly one row is main, and both are statements about
    /// one employee on one structure. A screen showing every structure at once would be several
    /// independent forms wearing one Save button.
    /// </remarks>
    public async Task<IActionResult> Change(
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

        var today = await _authorisation.TodayAsync();

        var model = new ChangePlacementViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            StructureId = structureId,
            EffectiveFrom = today.ToIso(),
        };

        model.StructureId = await ResolveStructureAsync(
            employee.EmployeeId, structureId, today, cancellationToken);

        var current = await CurrentAsync(employee.EmployeeId, model.StructureId, today, cancellationToken);

        model.Rows =
        [
            .. current.Select(row => new PlacementEditRowViewModel
            {
                RecordId = row.RecordId,
                UnitPath = row.UnitPath,
                AllocationPercent = row.AllocationPercent,
                IsMain = row.IsPrimary,
            }),
        ];

        // Nothing on this structure yet: one empty row, so the form is a form rather than an empty
        // page with an "add" button on it.
        if (model.Rows.Count == 0)
        {
            model.Rows = [new PlacementEditRowViewModel { AllocationPercent = 100m, IsMain = true }];
        }

        await FillChangeChoicesAsync(model, current, cancellationToken);

        return View(model);
    }

    /// <param name="command">
    /// Which button was pressed: <c>add</c>, <c>remove:{index}</c>, or absent for Save.
    /// </param>
    /// <param name="mainRow">
    /// The index of the row marked main. A radio group rather than a checkbox per row, because
    /// "exactly one" is what a radio group means — a set of checkboxes is a set of independent
    /// answers and would let a reader build a split the validator must then refuse.
    /// </param>
    /// <remarks>
    /// Adding and removing a row are round trips rather than script, for the reason every other
    /// form in this module is: these screens commit dated decisions, and one that only worked with
    /// script would silently do nothing when a script failed to load. The round trip also redraws
    /// each row's unit picker against the current structure and date, which a cloned row could not
    /// do.
    /// </remarks>
    [HttpPost]
    [ActionName(nameof(Change))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePost(
        ChangePlacementViewModel model,
        string? command,
        int? mainRow,
        CancellationToken cancellationToken)
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
        model.Rows ??= [];

        // The radio is the authority on which row is main. Applied before anything is validated, so
        // the rules see the form as the reader left it.
        for (var index = 0; index < model.Rows.Count; index++)
        {
            model.Rows[index].IsMain = mainRow == index;
        }

        var today = await _authorisation.TodayAsync();
        var shownAt = IsoDate.TryParse(model.EffectiveFrom, out var asAt) ? asAt : today;

        if (string.Equals(command, "add", StringComparison.Ordinal))
        {
            // The remainder of 100, so the common second row needs no arithmetic from the reader.
            var remaining = 100m - model.Rows.Sum(row => row.AllocationPercent);

            model.Rows.Add(new PlacementEditRowViewModel
            {
                AllocationPercent = remaining > 0m ? remaining : 0m,
                IsMain = model.Rows.Count == 0,
            });

            return await RedrawAsync(model, shownAt, cancellationToken);
        }

        if (command?.StartsWith("remove:", StringComparison.Ordinal) == true &&
            int.TryParse(command["remove:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var removing) &&
            removing >= 0 && removing < model.Rows.Count)
        {
            var wasMain = model.Rows[removing].IsMain;

            model.Rows.RemoveAt(removing);

            // Removing the main row leaves none, and a form whose radio group has nothing selected
            // reads as a question the reader has not answered when in fact they have not been asked.
            if (wasMain && model.Rows.Count > 0)
            {
                model.Rows[0].IsMain = true;
            }

            return await RedrawAsync(model, shownAt, cancellationToken);
        }

        DateOnly? effectiveFrom = IsoDate.TryParse(model.EffectiveFrom, out var parsed) ? parsed : null;

        var latestChange = await LatestChangeAsync(
            model.EmployeeId, model.StructureId, cancellationToken);

        var violations = PlacementSplit.Validate(
            [.. model.Rows.Select(row => new PlacementSplitRow(row.RecordId, row.AllocationPercent, row.IsMain))],
            effectiveFrom,
            latestChange);

        foreach (var violation in violations)
        {
            ModelState.AddModelError(FieldFor(violation), _splitMessages.Describe(violation));
        }

        if (string.IsNullOrEmpty(model.StructureId))
        {
            // In the summary rather than on a field: the structure is chosen on its own small GET
            // form above, so there is no control in this one to put the message under.
            ModelState.AddModelError(string.Empty, S["Choose a structure."].Value);
        }

        if (ModelState.IsValid && effectiveFrom is { } from)
        {
            // The one write, and it is the same call the employee-assignments recipe step makes
            // with the same changes/split shape. Everything running on that date is closed the day
            // before; nothing is deleted.
            var changed = await _assignments.ReallocateAsync(
                model.EmployeeId,
                model.StructureId!,
                [
                    .. model.Rows.Select(row =>
                        new AssignmentSplitEntry(row.RecordId!, row.AllocationPercent, row.IsMain)),
                ],
                from,
                cancellationToken);

            if (!changed.IsAuthorised)
            {
                return Forbid();
            }

            if (changed.Succeeded)
            {
                await _notifier.SuccessAsync(H.Plural(
                    model.Rows.Count,
                    "{0} is placed at {1} unit from {2}.",
                    "{0} is placed at {1} units from {2}.",
                    employee.NameEn,
                    model.Rows.Count,
                    EmployeeDisplay.Date(from)));

                foreach (var advisory in changed.Errors)
                {
                    await _notifier.WarningAsync(new LocalizedHtmlString(
                        advisory.Message.Name, advisory.Message.Value));
                }

                return RedirectToAction(nameof(Index), new { id = model.EmployeeId });
            }

            // The engine's own refusals — a unit that does not hold employees, a record that does
            // not exist. Surfaced rather than translated: it is the authority, and a screen that
            // reworded its reasons would be a second description of the same rule.
            foreach (var error in changed.Errors.Where(error => !error.IsAdvisory))
            {
                ModelState.AddModelError(string.Empty, error.Message.Value);
            }
        }

        return await RedrawAsync(model, shownAt, cancellationToken);
    }

    /// <summary>
    /// Which control a broken rule belongs under: the row it is about, or the form as a whole.
    /// </summary>
    /// <remarks>
    /// A message about row two's allocation belongs beside row two's allocation, not in a list at
    /// the top the reader has to match up themselves — the same reasoning
    /// <c>EmployeesAdminController.AddErrors</c> applies to <c>RecordError.Field</c>.
    /// </remarks>
    private static string FieldFor(PlacementSplitViolation violation) => violation switch
    {
        { Rule: PlacementSplitRule.EffectiveDateMissing } or
        { Rule: PlacementSplitRule.EffectiveDateBeforeLatestChange } =>
            nameof(ChangePlacementViewModel.EffectiveFrom),

        { Rule: PlacementSplitRule.AllocationNotPositive, RowIndex: { } row } =>
            $"Rows[{row.ToString(CultureInfo.InvariantCulture)}].AllocationPercent",

        { RowIndex: { } row } =>
            $"Rows[{row.ToString(CultureInfo.InvariantCulture)}].RecordId",

        _ => string.Empty,
    };

    /// <summary>Re-renders the form with everything the view needs to draw it again.</summary>
    private async Task<IActionResult> RedrawAsync(
        ChangePlacementViewModel model,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var current = await CurrentAsync(model.EmployeeId, model.StructureId, asAt, cancellationToken);

        await FillChangeChoicesAsync(model, current, cancellationToken);

        return View(nameof(Change), model);
    }

    /// <summary>
    /// The structure to open on: the one asked for, else the primary organisation if they sit on
    /// it, else the first they sit on, else the primary organisation.
    /// </summary>
    /// <remarks>
    /// The same precedence the organisation designer uses when it is opened with no structure, and
    /// for the same reason: a tenant routinely nominates a primary organisation before anything is
    /// built on it, and a screen that opened on an empty structure while the person's actual
    /// placement sat on another would look like somebody who is not placed anywhere.
    /// </remarks>
    private async Task<string?> ResolveStructureAsync(
        string employeeId,
        string? asked,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var structures = await _structures.ListAsync(cancellationToken);

        if (!string.IsNullOrEmpty(asked) &&
            structures.Any(structure => string.Equals(structure.StructureId, asked, StringComparison.Ordinal)))
        {
            return asked;
        }

        var placed = await _assignments.GetAllAxesAsync(employeeId, asAt, cancellationToken);
        var organisation = await _structures.GetPrimaryOrganisationAsync(cancellationToken);

        if (organisation is not null &&
            placed.Any(row => string.Equals(row.StructureId, organisation.StructureId, StringComparison.Ordinal)))
        {
            return organisation.StructureId;
        }

        return (placed is [var first, ..] ? first.StructureId : null)
            ?? organisation?.StructureId
            ?? (structures is [var only, ..] ? only.StructureId : null);
    }

    /// <summary>What this employee is placed at on this structure on a date, named and pathed.</summary>
    private async Task<IReadOnlyList<PlacementRowViewModel>> CurrentAsync(
        string employeeId,
        string? structureId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(structureId))
        {
            return [];
        }

        var rows = await _assignments.GetAllEffectiveAsync(employeeId, structureId, asAt, cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var paths = await _graph.GetAncestorPathsAsync(
            structureId, [.. rows.Select(row => row.RecordId)], asAt, cancellationToken);

        var placements = new List<PlacementRowViewModel>();

        foreach (var row in rows)
        {
            var unit = await _dimensions.GetAsync(row.RecordId, asAt, cancellationToken);

            placements.Add(new PlacementRowViewModel
            {
                RecordId = row.RecordId,
                UnitCode = unit?.Code,
                UnitNameEn = unit?.NameEn,
                UnitNameAr = unit?.NameAr,
                UnitPath = unit is null
                    ? null
                    : BilingualDisplay.Path(paths.GetValueOrDefault(row.RecordId), unit),
                AllocationPercent = row.AllocationPercent,
                IsPrimary = row.IsPrimary,
                From = row.Range.From,
                To = row.Range.To,
            });
        }

        return [.. placements.OrderByDescending(row => row.IsPrimary).ThenBy(row => row.UnitName)];
    }

    /// <summary>The date of the latest placement change on record, or null when there is none.</summary>
    private async Task<DateOnly?> LatestChangeAsync(
        string employeeId,
        string? structureId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(structureId))
        {
            return null;
        }

        var history = await _assignments.GetHistoryAsync(employeeId, structureId, cancellationToken);

        return history.Count == 0 ? null : history.Max(row => row.Range.From);
    }

    private async Task FillChangeChoicesAsync(
        ChangePlacementViewModel model,
        IReadOnlyList<PlacementRowViewModel> current,
        CancellationToken cancellationToken)
    {
        model.Structures = [.. (await _structures.ListAsync(cancellationToken)).Select(ToOption)];
        model.Current = current;
        model.LatestChange = await LatestChangeAsync(model.EmployeeId, model.StructureId, cancellationToken);

        // The path a row's unit had when the form was drawn, so a row whose picker is closed still
        // says which "Credit" it means.
        var byRecord = current.ToDictionary(row => row.RecordId, row => row.UnitPath, StringComparer.Ordinal);

        foreach (var row in model.Rows.Where(row => !string.IsNullOrEmpty(row.RecordId)))
        {
            row.UnitPath = byRecord.GetValueOrDefault(row.RecordId!) ?? row.UnitPath;
        }
    }

    // ---- end every placement on one structure ------------------------------------------

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
    /// The structures to choose from, and the path of the unit a card arrived with.
    /// </summary>
    /// <remarks>
    /// The units themselves are no longer a list on the model: <c>workmate-unit-picker</c> reads
    /// them from <c>IDimensionGraphService</c> and searches the whole structure rather than
    /// filtering a page it was handed. That is the same change the employee picker went through and
    /// it was made for the same defect — a flat select could only offer what fitted on it, and said
    /// nothing about the rest.
    /// </remarks>
    private async Task FillChoicesAsync(PlaceEmployeeViewModel model, CancellationToken cancellationToken)
    {
        // The axes an employee can actually be placed on. Never every structure: that offered the
        // grade ladder and the reporting line, which are axes the Position type is a level of and
        // neither of which is anywhere a person goes. ADR-0030.
        var axes = await _positions.GetEmployeeAxesAsync(cancellationToken);

        model.Structures = [.. axes.Select(axis => ToOption(axis.Structure))];

        // One axis is the ordinary tenant, so the screen chooses it rather than asking a question
        // with one answer.
        if (string.IsNullOrEmpty(model.StructureId) && axes.Count == 1)
        {
            model.StructureId = axes[0].StructureId;
        }

        var chosen = axes.FirstOrDefault(axis =>
            string.Equals(axis.StructureId, model.StructureId, StringComparison.Ordinal));

        model.IsPositionBased = chosen?.IsPositionBased == true;

        var date = IsoDate.TryParse(model.EffectiveFrom, out var parsed)
            ? parsed
            : await _authorisation.TodayAsync();

        if (model.IsPositionBased)
        {
            await FillPositionChoicesAsync(model, date, cancellationToken);

            return;
        }

        if (string.IsNullOrEmpty(model.StructureId) || string.IsNullOrEmpty(model.RecordId))
        {
            return;
        }

        var unit = await _dimensions.GetAsync(model.RecordId, date, cancellationToken);

        if (unit is null)
        {
            return;
        }

        var paths = await _graph.GetAncestorPathsAsync(
            model.StructureId, [model.RecordId], date, cancellationToken);

        model.UnitPath = BilingualDisplay.Path(paths.GetValueOrDefault(model.RecordId), unit);
    }

    /// <summary>
    /// The derived half of a position-based form: where the chosen position sits, and who it
    /// answers to. Decisions D28 A and D30.
    /// </summary>
    /// <remarks>
    /// <b>Computed, never stored.</b> D30: the organisation chain comes from the position's
    /// ancestors as at the date and is on the employee's record nowhere. Recomputed on every render,
    /// which is what makes it correct when the date or the position changes.
    ///
    /// <b>The line manager is defaulted, not derived.</b> With exactly one holder of the parent
    /// post that holder is offered (D28 A); with several, nothing is chosen for the operator. Only
    /// filled in where the form has not already been given one, so a redraw does not overwrite
    /// somebody's answer.
    /// </remarks>
    private async Task FillPositionChoicesAsync(
        PlaceEmployeeViewModel model,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(model.RecordId))
        {
            return;
        }

        model.Organisation = await _organisation.PreviewAsync(model.RecordId, date, cancellationToken);

        model.UnitPath = string.Join(
            BilingualDisplay.PathSeparator,
            model.Organisation.Chain.Select(step => step.Name));

        // D31: THE LINE MANAGER FOLLOWS THE POSITION. With one holder of the post above it is set
        // every time rather than only into an empty field — otherwise choosing a different position
        // keeps the previous one's manager, which is the stale answer this decision exists to stop.
        // An override is left alone, because that is the whole point of one.
        if (model.LineManagerOverride)
        {
            return;
        }

        // D31 ADDENDUM 2: ONLY A CHOICE IS CARRIED, because only a choice is stored.
        //
        // A sole holder — of the post above, or of the acting post above a vacant one — is what the
        // chart says, so the form shows it and saves nothing. Writing it down was the defect: the
        // moment that post changed hands the employee kept the old person for ever. Derived
        // answers are read live by IEmployeeOrganisationService.GetLineManagerAsync.
        var lineManager = model.LineManager;

        model.LineManagerEmployeeId = lineManager.Mode == LineManagerMode.Choose &&
            lineManager.Holders.Any(holder => string.Equals(
                holder.EmployeeId, model.LineManagerEmployeeId, StringComparison.Ordinal))
            ? model.LineManagerEmployeeId
            : null;
    }

    private static StructureOptionViewModel ToOption(StructureDocument structure) => new()
    {
        StructureId = structure.StructureId,
        Code = structure.Code,
        NameEn = structure.Name.En,
        NameAr = structure.Name.Ar,
    };
}
