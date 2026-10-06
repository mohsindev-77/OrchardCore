using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// The organisation designer: a structure's tree as a chart or as a list, resolved as at a date,
/// and the actions that change it — add a unit, rename one, retire one.
/// </summary>
/// <remarks>
/// Every read goes through <see cref="IDimensionGraphService"/> and every write through
/// <see cref="IDimensionService"/>, the same services the API and the recipe steps call. Nothing
/// here decides anything: the controller gathers what a form needs, hands it to the service, and
/// shows what comes back. The validator's violations arrive as
/// <see cref="DimensionError"/>s and are put straight into model state, so the screen shows them
/// before the commit while the service re-checks on write and remains the authority.
///
/// Three permissions from specification section 4 are read here. Looking at the tree needs any of
/// this module's structural permissions — see <see cref="IsAuthorisedAsync"/> — so that a reader
/// such as the auditor role gets the chart with no action menus on it. Changing a unit needs
/// <see cref="Permissions.ManageDimensionRecords"/>, checked on every action and again in the
/// service. Resolving the tree as at a <em>past</em> date needs
/// <see cref="Permissions.ViewDimensionHistory"/>, enforced in
/// <see cref="EffectiveDateAsync"/> rather than only by hiding the control.
///
/// Move, merge and cancel-move are a later slice.
/// </remarks>
[Admin("Dimensions/Designer/{action}", "OrganisationDesigner{action}")]
public sealed class OrganisationDesignerAdminController : Controller
{
    private readonly IStructureService _structureService;
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IDimensionGraphService _graphService;
    private readonly IEmployeeAssignmentService _assignmentService;
    private readonly IDimensionService _dimensionService;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IAuthorizationService _authorizationService;
    private readonly IStringLocalizer S;

    public OrganisationDesignerAdminController(
        IStructureService structureService,
        IDimensionTypeService dimensionTypeService,
        IDimensionGraphService graphService,
        IEmployeeAssignmentService assignmentService,
        IDimensionService dimensionService,
        IDimensionAuthorisation authorisation,
        IAuthorizationService authorizationService,
        IStringLocalizer<OrganisationDesignerAdminController> stringLocalizer)
    {
        _structureService = structureService;
        _dimensionTypeService = dimensionTypeService;
        _graphService = graphService;
        _assignmentService = assignmentService;
        _dimensionService = dimensionService;
        _authorisation = authorisation;
        _authorizationService = authorizationService;
        S = stringLocalizer;
    }

    /// <param name="expand">
    /// A unit whose branch should already be open on arrival. Set when returning from an action,
    /// so that a unit just added is on screen rather than hidden inside a parent the user would
    /// have to find and reopen.
    /// </param>
    public async Task<IActionResult> Index(
        string? structureId,
        string? asAt,
        string? view,
        string? expand,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var effective = await EffectiveDateAsync(asAt);

        if (effective is null)
        {
            return Forbid();
        }

        var structures = await _structureService.ListAsync(cancellationToken);
        var typesById = (await _dimensionTypeService.ListAsync(includeRetired: true, cancellationToken: cancellationToken))
            .ToDictionary(type => type.DimensionTypeId);

        var model = new OrganisationDesignerViewModel
        {
            Structures =
            [
                .. structures
                    .OrderBy(structure => structure.Code, StringComparer.Ordinal)
                    .Select(structure => StructureListItemViewModel.Of(structure, typesById)),
            ],
            AsAt = effective.Value,
            Today = await _authorisation.TodayAsync(),
            CanViewHistory = await CanViewHistoryAsync(),
            CanEdit = await CanEditAsync(),
            ViewMode = ResolveViewMode(view),
            ExpandRecordId = expand,
        };

        var selected = (structureId is not null
                ? structures.FirstOrDefault(structure => structure.StructureId == structureId)
                : null)
            ?? structures.FirstOrDefault(structure => structure.IsPrimaryOrganisation)
            ?? (structures is [var first, ..] ? first : null);

        if (selected is null)
        {
            return View(model);
        }

        model.SelectedStructureId = selected.StructureId;
        model.SelectedStructureCode = selected.Code;
        model.SelectedStructureNameEn = selected.Name.En;
        model.SelectedStructureNameAr = selected.Name.Ar;

        var roots = await _graphService.GetRootsAsync(selected.StructureId, effective, cancellationToken);
        var unplaced = await _graphService.GetUnplacedAsync(selected.StructureId, effective, cancellationToken);

        model.Roots = await ToViewModelsAsync(selected.StructureId, roots, typesById, effective.Value, cancellationToken);
        model.Unplaced = await ToViewModelsAsync(selected.StructureId, unplaced, typesById, effective.Value, cancellationToken);

        await ExplainWhyUnplacedAsync(selected.StructureId, model.Unplaced, cancellationToken);

        if (!string.IsNullOrEmpty(expand))
        {
            // The whole chain, root first, because the browser opens the branch by walking down
            // it: every id after the first is only in the page once the one before it has been
            // fetched. The server knows the chain; the browser would have to ask for it.
            var ancestors = await _graphService.GetAncestorsAsync(
                selected.StructureId, expand, effective, cancellationToken);

            model.ExpandPath = [.. ancestors.Select(ancestor => ancestor.RecordId).Reverse(), expand];
        }

        return View(model);
    }

    /// <summary>
    /// Marks the unplaced records that got there by losing a parent, so the panel can say which
    /// is which instead of describing every one of them as having arrived from outside.
    /// </summary>
    private async Task ExplainWhyUnplacedAsync(
        string structureId,
        List<DesignerNodeViewModel> unplaced,
        CancellationToken cancellationToken)
    {
        if (unplaced.Count == 0)
        {
            return;
        }

        var orphaned = await _graphService.GetOrphanedByParentRetirementAsync(
            structureId, [.. unplaced.Select(node => node.RecordId)], cancellationToken);

        foreach (var node in unplaced)
        {
            if (!orphaned.TryGetValue(node.RecordId, out var reason))
            {
                continue;
            }

            node.OrphanedFromParentName = reason.FormerParentNameEn;
            node.OrphanedFromParentNameAr = reason.FormerParentNameAr;
            node.OrphanedOn = reason.RetiredOn.ToIso();
        }
    }

    /// <summary>A node's immediate children as at a date, for either view to expand on demand.</summary>
    [HttpGet]
    public async Task<IActionResult> Children(
        string structureId,
        string recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var effective = await EffectiveDateAsync(asAt);

        if (effective is null)
        {
            return Forbid();
        }

        var typesById = (await _dimensionTypeService.ListAsync(includeRetired: true, cancellationToken: cancellationToken))
            .ToDictionary(type => type.DimensionTypeId);

        var children = await _graphService.GetChildrenAsync(structureId, recordId, effective, cancellationToken);

        return Json(await ToViewModelsAsync(structureId, children, typesById, effective.Value, cancellationToken));
    }

    /// <summary>
    /// Records on this axis whose code or name matches <paramref name="q"/>, each carrying the
    /// ancestor chain either view must expand to reveal it.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        string structureId,
        string q,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorisedAsync())
        {
            return Forbid();
        }

        var effective = await EffectiveDateAsync(asAt);

        if (effective is null)
        {
            return Forbid();
        }

        var typesById = (await _dimensionTypeService.ListAsync(includeRetired: true, cancellationToken: cancellationToken))
            .ToDictionary(type => type.DimensionTypeId);

        var matches = await _graphService.SearchAsync(structureId, q, effective, cancellationToken);

        var hits = new List<DesignerSearchHitViewModel>();

        foreach (var match in matches)
        {
            var ancestors = await _graphService.GetAncestorsAsync(structureId, match.RecordId, effective, cancellationToken);

            hits.Add(new DesignerSearchHitViewModel(
                match.RecordId,
                match.Code,
                match.NameEn,
                match.NameAr,
                typesById.TryGetValue(match.DimensionTypeId, out var type) ? type.Name.En : match.DimensionTypeId,
                [.. ancestors.Select(ancestor => ancestor.RecordId).Reverse()]));
        }

        return Json(hits);
    }

    // ---- add a unit ---------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> AddUnit(
        string structureId,
        string? parentId,
        string? dimensionTypeId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var model = new AddUnitViewModel
        {
            StructureId = structureId,
            ParentRecordId = parentId,
            DimensionTypeId = dimensionTypeId ?? string.Empty,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
            AsAt = asAt ?? string.Empty,
        };

        return await PrepareAddUnitAsync(model, cancellationToken) is { } failure
            ? failure
            : View(model);
    }

    [HttpPost]
    [ActionName(nameof(AddUnit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUnitPost(AddUnitViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await CanEditAsync())
        {
            return Forbid();
        }

        if (await PrepareAddUnitAsync(model, cancellationToken) is { } failure)
        {
            return failure;
        }

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["Enter the date this unit starts."].Value);

            return View(nameof(AddUnit), model);
        }

        var result = await _dimensionService.AddUnitAsync(
            model.StructureId,
            model.ParentRecordId,
            model.DimensionTypeId,
            model.Code?.Trim() ?? string.Empty,
            model.Name,
            effectiveFrom,
            model.ToAttributeValues(),
            cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);

            return View(nameof(AddUnit), model);
        }

        return RedirectToDesigner(model.StructureId, model.AsAt, model.ParentRecordId);
    }

    /// <summary>
    /// Fills in everything the add-unit form shows but does not post back: the parent's name, the
    /// types that may go under it, and that type's attribute inputs. Returns a result to send
    /// instead when the request cannot be served at all.
    /// </summary>
    private async Task<IActionResult?> PrepareAddUnitAsync(
        AddUnitViewModel model,
        CancellationToken cancellationToken)
    {
        var structure = await _structureService.GetAsync(model.StructureId, cancellationToken);

        if (structure is null)
        {
            return NotFound();
        }

        model.StructureNameEn = structure.Name.En;

        if (model.ParentRecordId is not null)
        {
            var parent = await _dimensionService.GetAsync(model.ParentRecordId, null, cancellationToken);

            if (parent is null)
            {
                return NotFound();
            }

            model.ParentNameEn = parent.NameEn;
        }

        var permittedIds = await _graphService.GetPermittedChildTypeIdsAsync(
            model.StructureId, model.ParentRecordId, null, cancellationToken);

        var types = (await _dimensionTypeService.ListAsync(includeRetired: false, cancellationToken: cancellationToken))
            .ToDictionary(type => type.DimensionTypeId);

        model.PermittedTypes =
        [
            .. permittedIds
                .Where(types.ContainsKey)
                .Select(id => new DimensionTypeChoiceViewModel(id, types[id].Name.En, types[id].Code)),
        ];

        // No choice to make when the structure permits exactly one type here, which is the usual
        // case: a strict structure with no skipping has one level below any parent.
        if (string.IsNullOrEmpty(model.DimensionTypeId) && model.PermittedTypes.Count == 1)
        {
            model.DimensionTypeId = model.PermittedTypes[0].DimensionTypeId;
        }

        if (!string.IsNullOrEmpty(model.DimensionTypeId)
            && types.TryGetValue(model.DimensionTypeId, out var chosen))
        {
            MergeAttributeInputs(model, chosen);
        }

        return null;
    }

    /// <summary>
    /// Lines the posted attribute values up with the schema the type actually declares, in schema
    /// order, keeping whatever the user had typed.
    /// </summary>
    /// <remarks>
    /// Rebuilt from the schema rather than trusted from the form, so that a hand-edited post
    /// cannot introduce an input for an attribute the type does not have, and so that a form
    /// redrawn after a validation failure matches the schema even if the type was changed in
    /// another tab in between.
    /// </remarks>
    private static void MergeAttributeInputs(AddUnitViewModel model, DimensionTypeDocument type)
    {
        var submitted = model.Attributes.ToDictionary(attribute => attribute.Name, StringComparer.Ordinal);

        model.Attributes =
        [
            .. type.AttributeSchema.Select(definition =>
            {
                var input = UnitAttributeInputViewModel.Of(definition);

                if (submitted.TryGetValue(definition.Name, out var posted))
                {
                    input.Value = posted.Value;
                    input.ValueAr = posted.ValueAr;
                }

                return input;
            }),
        ];
    }

    // ---- rename a unit ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Rename(
        string structureId,
        string recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var record = await _dimensionService.GetAsync(recordId, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        return View(new RenameUnitViewModel
        {
            StructureId = structureId,
            RecordId = recordId,
            Code = record.Code,
            CurrentNameEn = record.NameEn,
            CurrentNameAr = record.NameAr,
            NameEn = record.NameEn,
            NameAr = record.NameAr,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
            AsAt = asAt ?? string.Empty,
        });
    }

    [HttpPost]
    [ActionName(nameof(Rename))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenamePost(RenameUnitViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var record = await _dimensionService.GetAsync(model.RecordId, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        model.Code = record.Code;
        model.CurrentNameEn = record.NameEn;
        model.CurrentNameAr = record.NameAr;

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["Enter the date this name applies from."].Value);

            return View(nameof(Rename), model);
        }

        // The two renames are different operations on the record, not a flag on one: a correction
        // rewrites the name in effect on that date, a substantive rename opens a new period from
        // it. Architecture section 6.
        var result = model.Kind == RenameKind.Corrective
            ? await _dimensionService.CorrectNameAsync(model.RecordId, model.Name, effectiveFrom, cancellationToken)
            : await _dimensionService.RenameAsync(model.RecordId, model.Name, effectiveFrom, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);

            return View(nameof(Rename), model);
        }

        // Back onto the renamed unit, so the new name is on screen rather than inside a branch
        // the user has to find and reopen to check the change took.
        return RedirectToDesigner(model.StructureId, model.AsAt, parentOf: model.RecordId);
    }

    // ---- retire a unit ------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Retire(
        string structureId,
        string recordId,
        string? asAt,
        CancellationToken cancellationToken)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var record = await _dimensionService.GetAsync(recordId, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        var model = new RetireUnitViewModel
        {
            StructureId = structureId,
            RecordId = recordId,
            Code = record.Code,
            NameEn = record.NameEn,
            NameAr = record.NameAr,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
            AsAt = asAt ?? string.Empty,
            CanMoveChildren = await CanMoveAsync(),
        };

        return View(model);
    }

    [HttpPost]
    [ActionName(nameof(Retire))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirePost(RetireUnitViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var record = await _dimensionService.GetAsync(model.RecordId, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        model.Code = record.Code;
        model.NameEn = record.NameEn;
        model.NameAr = record.NameAr;
        model.CanMoveChildren = await CanMoveAsync();

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveDate))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["Enter the date this unit closes."].Value);

            return View(nameof(Retire), model);
        }

        var planned = await _dimensionService.PlanRetireAsync(
            model.StructureId, model.RecordId, effectiveDate, cancellationToken);

        if (!planned.Succeeded)
        {
            AddErrors(planned);

            return View(nameof(Retire), model);
        }

        model.Plan = planned.Value;

        // Shown before it happens, and only acted on once the user has seen this exact plan and
        // said yes to it — the same dry-run-then-confirm shape as a level change, a move and a
        // merge. What is still attached is a warning, not a refusal: closing a branch from the
        // top is a legitimate thing to do, and retirement is what the architecture prescribes for
        // a record that history still refers to.
        if (!model.Confirmed)
        {
            return View(nameof(Retire), model);
        }

        // Children are the one thing that is not a warning. A parent closing over live units
        // leaves them active with no place on the tree, so the screen will not commit until
        // somebody has said which of the three things should happen to them.
        if (model.Plan!.HasChildrenToDecide && model.ChildrenDisposition is null)
        {
            ModelState.AddModelError(
                nameof(model.ChildrenDisposition),
                S["Choose what happens to the units under this one."].Value);

            return View(nameof(Retire), model);
        }

        var disposition = model.Plan.HasChildrenToDecide
            ? Disposition(model)
            : null;

        var result = await _dimensionService.RetireAsync(
            model.StructureId, model.RecordId, effectiveDate, disposition, cancellationToken);

        if (!result.Succeeded)
        {
            AddErrors(result);

            return View(nameof(Retire), model);
        }

        return RedirectToDesigner(model.StructureId, model.AsAt, parentOf: null);
    }

    /// <summary>
    /// The choice the form carried, as the services understand it. An empty parent on a move means
    /// the top of the structure, which is a real answer and not a missing one.
    /// </summary>
    private static ChildrenDisposition Disposition(RetireUnitViewModel model) =>
        model.ChildrenDisposition switch
        {
            ChildrenDispositionKind.MoveToParent => ChildrenDisposition.MoveTo(
                string.IsNullOrWhiteSpace(model.NewParentRecordId) ? null : model.NewParentRecordId),
            ChildrenDispositionKind.RetireCascade => ChildrenDisposition.Cascade,
            _ => ChildrenDisposition.Unplaced,
        };

    // ---- shared -------------------------------------------------------------------------

    /// <summary>
    /// Back to the tree the action was started from, on the same structure and the same date.
    /// </summary>
    /// <param name="parentOf">
    /// The branch to open on arrival, so a unit just added is on screen rather than hidden inside
    /// a collapsed parent.
    /// </param>
    private RedirectToActionResult RedirectToDesigner(string structureId, string? asAt, string? parentOf) =>
        RedirectToAction(nameof(Index), new
        {
            structureId,
            asAt = string.IsNullOrEmpty(asAt) ? null : asAt,
            expand = parentOf,
        });

    private void AddErrors<TValue>(DimensionResult<TValue> result)
    {
        if (!result.IsAuthorised)
        {
            ModelState.AddModelError(string.Empty, S["You do not have permission to do that."].Value);

            return;
        }

        AddErrors(result.Errors);
    }

    private void AddErrors(IReadOnlyList<DimensionError> errors)
    {
        foreach (var error in errors.Where(error => !error.IsAdvisory))
        {
            ModelState.AddModelError(string.Empty, error.Message.Value);
        }
    }

    /// <summary>
    /// The date to resolve the axis as at, or null when the caller asked for one they may not
    /// have.
    /// </summary>
    /// <remarks>
    /// Parsed as ISO-8601 explicitly rather than through model binding on a <c>DateOnly</c>
    /// parameter: an <c>&lt;input type="date"&gt;</c> always submits <c>yyyy-MM-dd</c> whatever
    /// the page's culture, and this screen renders under an Arabic culture too. A culture-sensitive
    /// parse would read the same wire format differently for different users, which is the kind of
    /// defect that only shows up for the one customer whose calendar differs.
    ///
    /// An unparseable date falls back to today rather than failing: it can only come from a
    /// hand-edited URL, and a designer that opens on today is a better answer than an error page.
    /// </remarks>
    private async Task<DateOnly?> EffectiveDateAsync(string? asAt)
    {
        var today = await _authorisation.TodayAsync();

        if (string.IsNullOrWhiteSpace(asAt) ||
            !IsoDate.TryParse(asAt, out var parsed) ||
            parsed == today)
        {
            return today;
        }

        return await CanViewHistoryAsync() ? parsed : null;
    }

    /// <summary>
    /// The view to render: what the caller just chose, else what they chose last time, else the
    /// chart. Writing the cookie here rather than from script keeps the choice remembered for a
    /// browser with no JavaScript, which is the one that most needs the list.
    /// </summary>
    private DesignerViewMode ResolveViewMode(string? view)
    {
        if (!string.IsNullOrWhiteSpace(view))
        {
            var chosen = string.Equals(view, nameof(DesignerViewMode.List), StringComparison.OrdinalIgnoreCase)
                ? DesignerViewMode.List
                : DesignerViewMode.Chart;

            Response.Cookies.Append(
                OrganisationDesignerViewModel.ViewModeCookieName,
                chosen.ToString(),
                new Microsoft.AspNetCore.Http.CookieOptions
                {
                    // Readable and writable from script on purpose: switching view with script on
                    // is a class swap that never reaches the server, and the choice still has to
                    // be remembered. It carries a view preference, not anything privileged.
                    HttpOnly = false,
                    IsEssential = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                });

            return chosen;
        }

        return Request.Cookies.TryGetValue(OrganisationDesignerViewModel.ViewModeCookieName, out var remembered)
            && string.Equals(remembered, nameof(DesignerViewMode.List), StringComparison.OrdinalIgnoreCase)
                ? DesignerViewMode.List
                : DesignerViewMode.Chart;
    }

    private async Task<List<DesignerNodeViewModel>> ToViewModelsAsync(
        string structureId,
        IReadOnlyList<DimensionNodeRef> nodes,
        IReadOnlyDictionary<string, DimensionTypeDocument> typesById,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        var recordIds = nodes.Select(node => node.RecordId).ToList();

        var employeeCounts = await _assignmentService.CountEmployeesAtAsync(
            structureId, recordIds, asAt, cancellationToken);

        var childCounts = await _graphService.CountChildrenAsync(
            structureId, recordIds, asAt, cancellationToken);

        var canEdit = await CanEditAsync();

        return
        [
            .. nodes.Select(node => DesignerNodeViewModel.Of(
                node, typesById, employeeCounts, childCounts, structureId, asAt.ToIso(), canEdit)),
        ];
    }

    /// <summary>
    /// Who may look at the designer at all: anyone holding any of this module's structural
    /// permissions.
    /// </summary>
    /// <remarks>
    /// Viewing was gated on <see cref="Permissions.ManageDimensionRecords"/> while the screen was
    /// read-only for everybody, which was defensible then and is not now. The brief for the
    /// mutations requires that "users without edit permissions see the chart read-only with no
    /// action menus", and that state is unreachable if seeing the chart needs the same permission
    /// as changing it. The auditor role, which holds only
    /// <see cref="Permissions.ViewDimensionHistory"/>, is exactly the reader that requirement
    /// describes.
    ///
    /// Still no eighth permission: specification section 4 names seven and this uses four of them.
    /// Reading is the union; writing is <see cref="CanEditAsync"/>, checked separately on every
    /// action and again in the service.
    /// </remarks>
    private async Task<bool> IsAuthorisedAsync() =>
        await _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords)
        || await _authorizationService.AuthorizeAsync(User, Permissions.ViewDimensionHistory)
        || await _authorizationService.AuthorizeAsync(User, Permissions.MoveDimensionRecords)
        || await _authorizationService.AuthorizeAsync(User, Permissions.MergeDimensionRecords);

    /// <summary>
    /// Who may add, rename or retire a unit. The service checks this again on write; this is what
    /// decides whether the screen offers the action at all.
    /// </summary>
    private Task<bool> CanEditAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords);

    /// <summary>
    /// Who may send a unit to a different parent. Moving the children of a unit being retired is
    /// still a move — it changes what every historical report under them resolves to — so the
    /// option is only offered to somebody who could make the same move directly.
    /// </summary>
    private Task<bool> CanMoveAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.MoveDimensionRecords);

    private Task<bool> CanViewHistoryAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ViewDimensionHistory);
}
