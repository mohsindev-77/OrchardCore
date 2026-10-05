using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.Admin;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// The organisation designer's read-only tree, as a chart or as a list: a structure's roots,
/// lazily loaded children, search and its unplaced-records panel, all resolved as at a date.
/// Every action reads through <see cref="IDimensionGraphService"/>, the only entry to the link and
/// closure tables, the same as every other screen in this module; nothing here is computed from a
/// table this controller reads directly.
/// </summary>
/// <remarks>
/// Read-only by design for now: move, merge, retire, rename and "add unit" are a later slice.
/// Gated by <see cref="Permissions.ManageDimensionRecords"/> rather than a new permission, because
/// specification section 4 names seven permissions and viewing the tree is not a reason for an
/// eighth — it is the same day-to-day capability that already lets a caller create and change
/// records through the generic content screens.
///
/// Resolving the axis as at a <em>past</em> date is a different question and section 4 does name a
/// permission for it: <see cref="Permissions.ViewDimensionHistory"/>, "resolve the structure as at
/// a past date rather than only as it is today". Every action here that takes a date enforces it,
/// not only the screen that renders the control — <see cref="EffectiveDateAsync"/> is the one
/// place that decision is made.
/// </remarks>
[Admin("Dimensions/Designer/{action}", "OrganisationDesigner{action}")]
public sealed class OrganisationDesignerAdminController : Controller
{
    private readonly IStructureService _structureService;
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IDimensionGraphService _graphService;
    private readonly IEmployeeAssignmentService _assignmentService;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IAuthorizationService _authorizationService;

    public OrganisationDesignerAdminController(
        IStructureService structureService,
        IDimensionTypeService dimensionTypeService,
        IDimensionGraphService graphService,
        IEmployeeAssignmentService assignmentService,
        IDimensionAuthorisation authorisation,
        IAuthorizationService authorizationService)
    {
        _structureService = structureService;
        _dimensionTypeService = dimensionTypeService;
        _graphService = graphService;
        _assignmentService = assignmentService;
        _authorisation = authorisation;
        _authorizationService = authorizationService;
    }

    public async Task<IActionResult> Index(
        string? structureId,
        string? asAt,
        string? view,
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
            ViewMode = ResolveViewMode(view),
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

        return View(model);
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

        return [.. nodes.Select(node => DesignerNodeViewModel.Of(node, typesById, employeeCounts, childCounts))];
    }

    private Task<bool> IsAuthorisedAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords);

    private Task<bool> CanViewHistoryAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ViewDimensionHistory);
}
