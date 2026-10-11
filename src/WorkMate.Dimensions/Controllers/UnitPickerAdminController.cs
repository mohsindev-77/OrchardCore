using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.Admin;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// What the unit picker asks as somebody types.
/// </summary>
/// <remarks>
/// <b>The same shape as the employee picker's search, and for the same reason.</b> That control was
/// a <c>&lt;select&gt;</c> of the first hundred people, which made the rest of the tenant
/// unreachable from every screen and said nothing about it. A unit picker rendered the same way has
/// the same hole: Crescent has four hundred units, and a control that can only filter what it was
/// sent can only filter the first page of them.
///
/// <b>Every option carries its full path</b> — "Crescent › Retail Banking › Credit" — because a
/// real organisation has several units with the same name. Crescent has two "Credit" departments
/// and three "Operations", and a flat list of names offers a reader identical rows with no way to
/// tell them apart. The path is not decoration; without it the control cannot be used correctly.
///
/// It answers only what a picker needs, and only for units the structure says may hold employees:
/// the forwards reading of the rule <c>IDimensionValidator.ValidateAssignmentAsync</c> reads
/// backwards, from the same <c>PermitsEmployeesAt</c>. ADR-0010's lesson — one rule, two
/// directions, one implementation.
/// </remarks>
[Admin("Dimensions/UnitPicker/{action}", "WorkMateUnitPicker{action}")]
[Authorize]
public sealed class UnitPickerAdminController : Controller
{
    private readonly IDimensionGraphService _graph;
    private readonly IAuthorizationService _authorizationService;

    public UnitPickerAdminController(
        IDimensionGraphService graph,
        IAuthorizationService authorizationService)
    {
        _graph = graph;
        _authorizationService = authorizationService;
    }

    /// <summary>How many options a page of results holds. The employee picker's figure.</summary>
    public const int PageSize = 20;

    /// <summary>Where the search endpoint lives, for a control that cannot ask a URL helper.</summary>
    /// <remarks>
    /// Pinned by <c>UnitPickerTenantTests</c>, which resolves the route for real, so a rename
    /// cannot break the control silently — the same guard <c>EmployeePickerTagHelper.SearchUrl</c>
    /// carries.
    /// </remarks>
    public const string SearchUrl = "/Admin/Dimensions/UnitPicker/Search";

    /// <summary>
    /// A page of units on <paramref name="structureId"/> matching <paramref name="q"/>.
    /// </summary>
    /// <param name="within">
    /// A unit to limit the results to the subtree of, or null for the whole structure. Resolved
    /// through the closure index on the same date the units are, so "within Retail Banking" means
    /// what it meant on that date rather than what it means today.
    /// </param>
    /// <param name="asAt">
    /// The date the placement is to take effect, so the picker offers the units that are active
    /// then. Reads default to today; a write never defaults at all.
    /// </param>
    /// <param name="childType">
    /// The dimension type a new record will be, when the control is choosing a <em>parent</em>
    /// rather than a place to put an employee. Switches the question from "which units hold
    /// people" to "which units may contain one of these", and with it the permission: the first is
    /// a placement decision, the second is a record-management one.
    /// </param>
    [HttpGet]
    public async Task<IActionResult> Search(
        string structureId,
        string? q,
        string? within,
        string? asAt,
        string? childType,
        int take = PageSize,
        CancellationToken cancellationToken = default)
    {
        var choosingAParent = !string.IsNullOrEmpty(childType);

        // The permission matches the decision the control is part of, rather than being one
        // permission for one endpoint. A reader who may not place anybody has no use for a list of
        // the units they could not place them at; a reader who may not create a record has no use
        // for a list of the parents they could not create one under.
        var permission = choosingAParent
            ? Permissions.ManageDimensionRecords
            : Permissions.AssignEmployees;

        if (!await _authorizationService.AuthorizeAsync(User, permission))
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(structureId))
        {
            return Json(new { items = Array.Empty<object>(), total = 0 });
        }

        DateOnly? date = IsoDate.TryParse(asAt, out var parsed) ? parsed : null;
        var page = Math.Clamp(take, 1, PageSize);

        var found = choosingAParent
            ? await _graph.SearchPermittedParentsAsync(
                structureId, childType!, q, within, date, page, cancellationToken)
            : await _graph.SearchEmployeeAttachableUnitsAsync(
                structureId, q, within, date, page, cancellationToken);

        var paths = await _graph.GetAncestorPathsAsync(
            structureId, [.. found.Items.Select(unit => unit.RecordId)], date, cancellationToken);

        return Json(new
        {
            items = found.Items.Select(unit => new
            {
                id = unit.RecordId,
                code = unit.Code,
                name = BilingualDisplay.Name(unit.NameEn, unit.NameAr),
                path = BilingualDisplay.Path(paths.GetValueOrDefault(unit.RecordId), unit),
            }),
            total = found.Total,
        });
    }
}
