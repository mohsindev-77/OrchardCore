using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.Admin;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// What the position and grade pickers ask as somebody types, and what the grade picker asks when
/// the position beside it changes. Decisions D26 F and D26 G.
/// </summary>
/// <remarks>
/// <b>Two endpoints because there are two questions, and the second depends on the first.</b>
/// <see cref="Search"/> answers *"which positions could be chosen on this date"*;
/// <see cref="Grades"/> answers *"and which grades does the one that was chosen allow"*. D26 G makes
/// the grade control empty and disabled until a position is chosen and refreshed when it changes, so
/// the second question has to be askable on its own rather than only as part of rendering a form.
///
/// <b>Server-side, for the reason the unit and employee pickers are.</b> A control that filters a
/// list it already holds can only narrow what was sent, and what was sent is the first page — so on
/// a tenant with four hundred positions the rest are unreachable and nothing says so.
///
/// <b>Every option carries its unit and its filled/approved count</b> (D26 F). Both are load-bearing
/// rather than decorative: two positions called <em>Supervisor</em> are told apart by their unit,
/// and the count is what lets somebody see they are about to over-fill a post — which is allowed,
/// and warned about.
/// </remarks>
[Admin("Dimensions/PositionPicker/{action}", "WorkMatePositionPicker{action}")]
[Authorize]
public sealed class PositionPickerAdminController : Controller
{
    private readonly IPositionService _positions;
    private readonly IStructureService _structures;
    private readonly IAuthorizationService _authorizationService;

    public PositionPickerAdminController(
        IPositionService positions,
        IStructureService structures,
        IAuthorizationService authorizationService)
    {
        _positions = positions;
        _structures = structures;
        _authorizationService = authorizationService;
    }

    /// <summary>How many options a page of results holds. The unit picker's figure.</summary>
    public const int PageSize = 20;

    /// <summary>Where the position search lives, for a control that cannot ask a URL helper.</summary>
    /// <remarks>
    /// Pinned by <c>PositionPickerTenantTests</c>, which resolves the route for real, so a rename
    /// cannot break the control silently — the same guard <c>UnitPickerAdminController.SearchUrl</c>
    /// carries.
    /// </remarks>
    public const string SearchUrl = "/Admin/Dimensions/PositionPicker/Search";

    /// <inheritdoc cref="SearchUrl"/>
    public const string GradesUrl = "/Admin/Dimensions/PositionPicker/Grades";

    /// <summary>
    /// A page of positions that may be chosen on <paramref name="asAt"/>, matching
    /// <paramref name="q"/>.
    /// </summary>
    /// <param name="asAt">
    /// The date the choice takes effect, so the control offers the positions active then. Reads
    /// default to today; a write never defaults at all.
    /// </param>
    /// <param name="excluding">
    /// A position to leave out — the one being edited, when this control is choosing what it reports
    /// to. A position reporting to itself is a loop the validator refuses anyway; leaving it out of
    /// the list is the picker not offering a choice whose only outcome is a refusal, which is the
    /// rule every picker in this module follows.
    /// </param>
    /// <param name="structureId">
    /// The axis the choice will be written on, so only positions that may go on it are offered.
    /// Absent on a tenant with one Position type, where there is nothing to narrow — see
    /// <c>PositionPickerTagHelper.StructureId</c>.
    /// </param>
    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        string? asAt,
        string? excluding,
        string? structureId,
        int take = PageSize,
        CancellationToken cancellationToken = default)
    {
        // A list of posts somebody could be put into is of no use to a reader who may do neither of
        // the things it is for, and the two readers differ: choosing a reporting line is record
        // management, and putting a person into a post is an assignment. Either is enough.
        if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords) &&
            !await _authorizationService.AuthorizeAsync(User, Permissions.AssignEmployees))
        {
            return Forbid();
        }

        DateOnly? date = IsoDate.TryParse(asAt, out var parsed) ? parsed : null;
        var page = Math.Clamp(take, 1, PageSize);

        var found = await _positions.SearchSelectableAsync(q, date, page, cancellationToken);

        var onTheAxis = string.IsNullOrEmpty(structureId)
            ? null
            : (await _structures.GetAsync(structureId, cancellationToken))?.DimensionTypeIds
                .ToHashSet(StringComparer.Ordinal);

        return Json(new
        {
            items = found.Items
                .Where(option => string.IsNullOrEmpty(excluding) ||
                    !string.Equals(option.Position.RecordId, excluding, StringComparison.Ordinal))
                .Where(option => onTheAxis is null || onTheAxis.Contains(option.Position.DimensionTypeId))
                .Select(option => new
                {
                    id = option.Position.RecordId,
                    code = option.Position.Code,
                    name = BilingualDisplay.Name(option.Position.NameEn, option.Position.NameAr),
                    unit = option.UnitPath,
                    approved = option.Establishment.Approved,
                    filled = option.Establishment.Filled,
                    full = option.IsFull,
                }),
            total = found.Total,
        });
    }

    /// <summary>
    /// The grades <paramref name="positionId"/> allows on <paramref name="asAt"/>. Decision D26 G.
    /// </summary>
    /// <remarks>
    /// Answers an empty list for a position with no grades and for no position at all, which the
    /// control renders the same way: a disabled, empty box. The difference between them is a
    /// sentence beside it, not a different response — the control has nothing different to do.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> Grades(
        string? positionId,
        string? asAt,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords) &&
            !await _authorizationService.AuthorizeAsync(User, Permissions.AssignEmployees))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(positionId))
        {
            return Json(new { items = Array.Empty<object>(), total = 0 });
        }

        DateOnly? date = IsoDate.TryParse(asAt, out var parsed) ? parsed : null;

        var grades = await _positions.GetAllowedGradesAsync(positionId, date, cancellationToken);

        return Json(new
        {
            items = grades.Select(grade => new
            {
                id = grade.RecordId,
                code = grade.Code,
                name = BilingualDisplay.Name(grade.NameEn, grade.NameAr),
            }),
            total = grades.Count,
        });
    }
}
