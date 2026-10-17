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
    private readonly IPositionService _positions;
    private readonly IEmployeeOrganisationService _organisation;
    private readonly IEmployeeSectionCatalogue _sections;
    private readonly AccessRoleReminder _accessRoles;
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
        IPositionService positions,
        IEmployeeOrganisationService organisation,
        IEmployeeSectionCatalogue sections,
        AccessRoleReminder accessRoles,
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
        _positions = positions;
        _organisation = organisation;
        _sections = sections;
        _accessRoles = accessRoles;
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

        var model = new EmployeeCreateViewModel
        {
            // A suggestion on the form, not a default on the write: the service refuses a create
            // with no join date, and a person can see and change this before committing.
            JoinDate = (await _authorisation.TodayAsync()).ToIso(),
        };

        await FillJobChoicesAsync(model, cancellationToken);

        return View(model);
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

        // FIRST, AND ON EVERY PATH THROUGH THIS ACTION. Whether the chosen axis places employees at
        // positions is answered from the axis, never from the post: IsPositionBased is a property of
        // the tenant's chart, and a form that carried it would be a form that could be told the
        // wrong answer. It also has to be settled before anything below reads it — the defect this
        // replaced placed nobody, because the flag arrived false from a post that never sent it and
        // every position-shaped branch then quietly did nothing.
        await FillJobChoicesAsync(model, cancellationToken);

        // Changing the position or the axis is navigation, not a save. The same flag and the same
        // reason as the Placement screen's: without it, picking a position re-ran the whole save
        // path and answered "an employee code is needed" before anybody had typed one.
        if (model.IsRefresh)
        {
            model.IsRefresh = false;

            return View(model);
        }

        if (!IsoDate.TryParse(model.JoinDate, out var joinDate))
        {
            ModelState.AddModelError(nameof(model.JoinDate), S["A join date is needed."].Value);

            return View(model);
        }

        // ASKED BEFORE THE PERSON EXISTS. The validator is the authority and refuses a grade that
        // is not one of the position's on the write (D26 G) — but by then the employee has been
        // created, and an error about a grade is a strange thing to meet on the editor of somebody
        // who was just added. This is the same question read forwards, so the ordinary mistake is
        // answered on the form that made it.
        if (!await JobChoiceIsCoherentAsync(model, joinDate, cancellationToken) ||
            !LineManagerChoiceIsCoherent(model))
        {
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
                model.LineManagerEmployeeId,
                PhotoPath: null,
                // D31 addendum 2: which of three kinds of answer this is, stated by the form
                // because nothing can work it out later. FillJobChoicesAsync has already reduced
                // the id to a choice or to nothing, so a derived answer arrives here as None.
                LineManagerKind: model.LineManager.KindFor(model.LineManagerEmployeeId)),
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

        await PlaceAtChosenPositionAsync(model, created.Value, joinDate, cancellationToken);

        // The editor, told that this is a brand-new record, so it can offer the step the person
        // almost certainly came to take next rather than leaving them to find it.
        return RedirectToAction(nameof(Edit), new { id = created.Value.EmployeeId, created = true });
    }

    /// <summary>
    /// Places a newly created employee at the position the form chose, from their join date.
    /// </summary>
    /// <remarks>
    /// <b>The same write path as the Placement screen</b> — <c>IEmployeeAssignmentService</c>'s
    /// <c>PlaceAsync</c>, which is a one-row reallocation and not a second mechanism. A second write
    /// path is how a screen and an import come to disagree about what a placement means, which is
    /// the reason <c>EmployeePlacementAdminController</c>'s own header gives.
    ///
    /// <b>The join date is the effective date, and it is not a default.</b> CLAUDE.md rule 3 bans
    /// defaulting on a write, and this does not default: the person typed the date, on this form,
    /// in the field above. That a new employee's first placement starts the day they join is the
    /// statement the form is making, and it is why the Create screen may ask for a position at all
    /// when the editor may not — the editor has no date to offer (design note §2.1).
    ///
    /// <b>A failure here does not undo the employee.</b> They were created and the notification said
    /// so; the placement is reported separately and they land on the editor unplaced, with
    /// <em>Choose position</em> waiting. Silently discarding a person because a placement was
    /// refused would be worse than either half.
    /// </remarks>
    private async Task PlaceAtChosenPositionAsync(
        EmployeeCreateViewModel model,
        EmployeeRecord employee,
        DateOnly joinDate,
        CancellationToken cancellationToken)
    {
        if (!model.IsPositionBased ||
            string.IsNullOrEmpty(model.StructureId) ||
            string.IsNullOrEmpty(model.PositionRecordId))
        {
            // Left blank, which creates an unplaced employee exactly as this screen always has.
            return;
        }

        var placed = await _assignments.PlaceAsync(
            employee.EmployeeId,
            model.StructureId,
            model.PositionRecordId,
            joinDate,
            // A position-based form does not ask: the answer is always the main placement at 100
            // per cent, and a second concurrent position is a later slice.
            100m,
            isPrimary: true,
            model.GradeRecordId,
            cancellationToken);

        if (!placed.IsAuthorised || !placed.Succeeded)
        {
            // Authorisation is re-checked by the service and not only by the screen (CLAUDE.md
            // rule 5). It should be unreachable — FillJobChoicesAsync does not offer the block to
            // somebody who may not place — but an unauthorised result carries no errors to report,
            // so it is folded in here rather than left to print nothing at all.
            foreach (var error in placed.Errors)
            {
                await _notifier.WarningAsync(new LocalizedHtmlString(
                    error.Message.Name, error.Message.Value));
            }

            await _notifier.WarningAsync(
                H["{0} was added but not placed. Choose a position from their record.", employee.NameEn]);

            return;
        }

        await _notifier.SuccessAsync(H["{0} holds their position from {1}.",
            employee.NameEn, EmployeeDisplay.Date(joinDate)]);

        // D26 I. A position carries no permissions, so nothing is changed here — somebody is told
        // to look. One class raises it everywhere, so the security slice has one thing to replace.
        await _accessRoles.RaiseAsync(employee.NameEn ?? employee.EmployeeId);

        foreach (var advisory in placed.Errors)
        {
            await _notifier.WarningAsync(new LocalizedHtmlString(
                advisory.Message.Name, advisory.Message.Value));
        }
    }

    /// <summary>
    /// Whether the position and grade the form chose can be written, asked before anybody is created.
    /// </summary>
    /// <remarks>
    /// Deliberately only the questions a <em>read</em> can answer: is a position named when the axis
    /// needs one, and is the grade one of that position's (D26 G). It is not a copy of the
    /// validator and must not become one — the validator stays the authority, and runs on the write
    /// where a recipe and an import also pass. What this buys is that the common mistake is
    /// answered on the form, beside the control that made it.
    ///
    /// <b>A full position is not asked about</b>, because D26 F says a full post warns and is
    /// allowed: refusing it would stop the product recording what an organisation has actually done.
    /// </remarks>
    private async Task<bool> JobChoiceIsCoherentAsync(
        EmployeeCreateViewModel model,
        DateOnly joinDate,
        CancellationToken cancellationToken)
    {
        if (!model.IsPositionBased || string.IsNullOrEmpty(model.PositionRecordId))
        {
            // No position is a valid answer on this form: it creates an unplaced employee.
            return true;
        }

        if (string.IsNullOrEmpty(model.GradeRecordId))
        {
            // Allowed, and the right default: a position whose grades a tenant has not set up has
            // none to offer, and "no grade recorded" is the truth rather than an invention.
            return true;
        }

        var allowed = await _positions.GetAllowedGradesAsync(
            model.PositionRecordId, joinDate, cancellationToken);

        if (allowed.Any(grade => string.Equals(
            grade.RecordId, model.GradeRecordId, StringComparison.Ordinal)))
        {
            return true;
        }

        ModelState.AddModelError(
            nameof(model.GradeRecordId),
            S["Choose one of this position's own grades."].Value);

        return false;
    }

    /// <summary>
    /// Whether the line manager the form posted is one this position's reporting line allows.
    /// Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>Only the one case that is required.</b> With several holders of the post above, D28 A
    /// refuses to pick and D31 requires that somebody does — so an empty answer is refused and an
    /// answer outside those holders is refused, because the control offered only them and anything
    /// else arrived by a replayed post.
    ///
    /// Every other case is already an answer: one holder is derived, a vacant or top post has none,
    /// and an override is a deliberate exception that may name anyone. The override is checked for
    /// nothing beyond being somebody, which is what makes it an escape hatch rather than a second
    /// rule.
    /// </remarks>
    private bool LineManagerChoiceIsCoherent(EmployeeCreateViewModel model)
    {
        var lineManager = model.LineManager;

        if (lineManager.Mode != LineManagerMode.Choose)
        {
            return true;
        }

        if (string.IsNullOrEmpty(model.LineManagerEmployeeId))
        {
            ModelState.AddModelError(
                nameof(model.LineManagerEmployeeId),
                S["{0} is held by {1} people. Choose which of them this person answers to.",
                    lineManager.PositionName, lineManager.Holders.Count].Value);

            return false;
        }

        if (lineManager.Holders.Any(holder => string.Equals(
            holder.EmployeeId, model.LineManagerEmployeeId, StringComparison.Ordinal)))
        {
            return true;
        }

        ModelState.AddModelError(
            nameof(model.LineManagerEmployeeId),
            S["Choose one of the people who hold {0}, or use Change to name somebody else.",
                lineManager.PositionName].Value);

        return false;
    }

    /// <summary>
    /// The axes an employee may join on, and what the chosen position would mean. D28 A, D30.
    /// </summary>
    /// <remarks>
    /// The Placement screen's <c>FillChoicesAsync</c>, asked of the Create form's model. Both go
    /// through <see cref="IPositionService.GetEmployeeAxesAsync"/> — never every structure, because
    /// that offered the grade ladder and the reporting line, and neither is anywhere a person goes
    /// (ADR-0030) — and both compose the derived block through
    /// <see cref="IEmployeeOrganisationService"/>, so the two forms cannot come to disagree about
    /// where a position puts somebody.
    /// </remarks>
    private async Task FillJobChoicesAsync(
        EmployeeCreateViewModel model,
        CancellationToken cancellationToken)
    {
        // NOT RENDERED RATHER THAN RENDERED AND THEN REFUSED, which is this module's rule and the
        // one the lifecycle controls and the sections Save button already follow. Placing somebody
        // is AssignEmployees, a dimension-engine permission, and ManageEmployees does not imply it:
        // a recruiter who may add people and may not place them gets the form they have always had
        // rather than a block that fails on submit.
        if (!await _authorisation.AuthoriseAsync(WorkMate.Dimensions.Permissions.AssignEmployees))
        {
            model.Structures = [];
            model.IsPositionBased = false;
            model.PositionRecordId = null;
            model.GradeRecordId = null;
            model.Organisation = null;

            return;
        }

        var axes = await _positions.GetEmployeeAxesAsync(cancellationToken);

        model.Structures = [.. axes.Select(axis => new StructureOptionViewModel
        {
            StructureId = axis.Structure.StructureId,
            Code = axis.Structure.Code,
            NameEn = axis.Structure.Name.En,
            NameAr = axis.Structure.Name.Ar,
        })];

        // One axis is the ordinary tenant, so the screen chooses it rather than asking a question
        // with one answer.
        if (string.IsNullOrEmpty(model.StructureId) && axes.Count == 1)
        {
            model.StructureId = axes[0].StructureId;
        }

        var chosen = axes.FirstOrDefault(axis =>
            string.Equals(axis.StructureId, model.StructureId, StringComparison.Ordinal));

        model.IsPositionBased = chosen?.IsPositionBased == true;

        if (!model.IsPositionBased)
        {
            // A tenant that has built no positions sees the form it has always seen. Anything the
            // post carried about a position belonged to an axis that does not ask, so it is
            // dropped rather than written.
            model.PositionRecordId = null;
            model.GradeRecordId = null;
            model.Organisation = null;

            return;
        }

        if (string.IsNullOrEmpty(model.PositionRecordId))
        {
            model.Organisation = null;

            return;
        }

        var date = IsoDate.TryParse(model.JoinDate, out var parsed)
            ? parsed
            : await _authorisation.TodayAsync();

        // Resolved as at the JOIN DATE, not as at today: somebody hired in March joins the chart as
        // it stood in March, and a department renamed in June must not rewrite what they joined.
        model.Organisation = await _organisation.PreviewAsync(
            model.PositionRecordId, date, cancellationToken);

        // D31: THE LINE MANAGER FOLLOWS THE POSITION, so with one holder of the post above it is
        // set rather than defaulted — every time, overwriting whatever the last render left behind.
        // Defaulting only into an empty field was right while the field was free and is wrong now:
        // choosing a different position would have kept the previous position's manager, which is
        // the stale answer this decision exists to stop.
        //
        // An override is left alone, because that is the whole point of one.
        if (!model.LineManagerOverride)
        {
            var lineManager = model.LineManager;

            // D31 ADDENDUM 2: THE FORM CARRIES WHAT IT WILL STORE, AND IT STORES ONLY A CHOICE.
            //
            // A sole holder — of the post above, or of the acting post above a vacant one — is
            // what the chart says, so the form shows it and saves nothing. Writing it down was the
            // defect: the moment that post changed hands, the employee kept the old person for
            // ever and every screen showed it. Derived answers are read live by
            // IEmployeeOrganisationService.GetLineManagerAsync.
            //
            // A pick among several IS stored, because nothing can re-derive which of them somebody
            // meant — D28 A refuses to choose, and so must a later read.
            model.LineManagerEmployeeId = lineManager.Mode == LineManagerMode.Choose &&
                lineManager.Holders.Any(holder => string.Equals(
                    holder.EmployeeId, model.LineManagerEmployeeId, StringComparison.Ordinal))
                ? model.LineManagerEmployeeId
                : null;
        }
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

        await FillFixedCoreAsync(model, cancellationToken);

        return View(model);
    }

    /// <summary>
    /// The second half of the fixed core — where this person sits — and the section list beneath it.
    /// </summary>
    /// <remarks>
    /// <b>Two reads, and neither of them is a field on the record.</b> The organisation block is
    /// derived from the position held on the date (D30) and the section list is read from the
    /// tenant's content definition (§9.2 Rule 2). Both are recomputed on every render, which is
    /// what keeps them right when somebody transfers or when B3 publishes a new section.
    /// </remarks>
    private async Task FillFixedCoreAsync(
        EmployeeEditViewModel model,
        CancellationToken cancellationToken)
    {
        // Reads default to today; writes never do.
        var today = await _authorisation.TodayAsync();

        model.Organisation = await _organisation.GetAsync(model.EmployeeId, today, cancellationToken);

        // Whether to offer the block at all. A tenant that has built no positions is left exactly
        // as it was, which is D30's own scope — so the question is asked of the axes rather than of
        // this employee, who may simply not be placed yet.
        model.IsPositionBased = (await _positions.GetEmployeeAxesAsync(cancellationToken))
            .Any(axis => axis.IsPositionBased);

        model.Sections = await _sections.ListAsync(cancellationToken);

        // Who manages them, from the ONE read path. D31 addendum 2.
        model.LineManager = await _organisation.GetLineManagerAsync(
            model.EmployeeId, today, cancellationToken);
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

        var lineManager = await SettledLineManagerAsync(
            employee, model.LineManagerEmployeeId, cancellationToken);

        var updated = await _employees.UpdateAsync(
            model.EmployeeId,
            new BilingualText(model.NameEn ?? string.Empty, model.NameAr ?? string.Empty),
            new EmployeeDetails(
                dateOfBirth,
                model.NationalityCode,
                model.Gender,
                lineManager.EmployeeId,
                model.PhotoPath,
                lineManager.Kind),
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

            await FillFixedCoreAsync(model, cancellationToken);

            return View(model);
        }

        await _notifier.SuccessAsync(H["{0} was updated.", updated.Value!.NameEn]);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// What the line manager should be after this save. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// <b>This form does not always ask the question, so it must not always answer it.</b> On a
    /// tenant with positions the line-manager control is in the read-only organisation block,
    /// <em>outside</em> this form — so nothing is posted, and taking that absence at face value
    /// would clear a deliberate override every time somebody corrected a spelling. The stored value
    /// is therefore carried forward.
    ///
    /// <b>Except one case, which is the addendum's own rule.</b> An acting choice is valid only
    /// while the post above stays vacant; once it is filled, <c>GetLineManagerAsync</c> has already
    /// stopped obeying it and derives live instead. The next save is where it is cleared — and the
    /// test for that is the same single question asked of the read path: does its answer still name
    /// the person the record holds? If it does not, the record has stopped being the answer and
    /// stops holding one.
    ///
    /// A stale <em>choice</em> survives that test, because <c>NeedsChoice</c> still names the
    /// person it is asking about. That is deliberate: the screen must keep saying who no longer
    /// holds the post, and a save that quietly blanked it would turn an explicit question into
    /// silence.
    /// </remarks>
    private async Task<(string? EmployeeId, StoredLineManagerKind Kind)> SettledLineManagerAsync(
        EmployeeRecord employee,
        string? posted,
        CancellationToken cancellationToken)
    {
        var positionBased = (await _positions.GetEmployeeAxesAsync(cancellationToken))
            .Any(axis => axis.IsPositionBased);

        if (!positionBased)
        {
            // No reporting line to follow, so the picker on this form is the only answer there is
            // and what it posts is what is stored — including an empty box, which clears it.
            // D30's scope, and the state every pre-F6 tenant stays in.
            return (posted, StoredLineManagerKind.None);
        }

        var answer = await _organisation.GetLineManagerAsync(
            employee.EmployeeId, await _authorisation.TodayAsync(), cancellationToken);

        var stillTheAnswer = !string.IsNullOrEmpty(employee.LineManagerEmployeeId) &&
            string.Equals(answer.EmployeeId, employee.LineManagerEmployeeId, StringComparison.Ordinal);

        return stillTheAnswer
            ? (employee.LineManagerEmployeeId, employee.LineManagerKind)
            : (null, StoredLineManagerKind.None);
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
