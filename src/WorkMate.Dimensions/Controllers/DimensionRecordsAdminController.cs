using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using OrchardCore.Admin;
using OrchardCore.DisplayManagement.Notify;
using WorkMate.Core;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Controllers;

/// <summary>
/// One list screen and one editor, serving every dimension type the tenant has.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> Until it, a Department could only be created or edited inside the
/// organisation designer. The designer is a chart: it answers "what sits under what", it is
/// navigated by expanding branches, and finding one department by a half-remembered code means
/// opening the branch it is in first. HR administrators asked for the ordinary shape every other
/// list in the product has — a list, a search box, a New button — and that is this.
///
/// <b>One controller, not one per type.</b> The types are the customer's: a tenant adds Cost centre
/// on Tuesday and expects a screen for it on Tuesday. So the type is a parameter, the columns come
/// from its attribute schema, and nothing here is written per type.
///
/// <b>Reached from the Dimension types list</b>, by the <b>View units</b> link on each row. It was
/// briefly reached from a menu entry per type instead; see <c>Navigation.AdminMenu</c> for what that
/// read like on a real tenant and why it was withdrawn.
///
/// <b>No second write path.</b> Creating calls <c>IDimensionService.AddUnitAsync</c> — the record
/// and its dated link as one operation, the same call the designer's Add unit screen makes. Renames
/// go through the same dated <c>RenameAsync</c>/<c>CorrectNameAsync</c> pair, attribute values
/// through the same <c>SetAttributeValuesAsync</c>, and moving and retiring are links to the
/// designer's own screens. Every refusal a reader sees here is the validator's own words.
/// </remarks>
[Admin("Dimensions/Records/{action}/{id?}", "DimensionRecords{action}")]
public sealed class DimensionRecordsAdminController : Controller
{
    private readonly IDimensionTypeService _types;
    private readonly IDimensionService _records;
    private readonly IDimensionGraphService _graph;
    private readonly IStructureService _structures;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IEnumerable<IEmployeeLookup> _employeeLookups;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IStringLocalizer S;
    private readonly IHtmlLocalizer H;

    public DimensionRecordsAdminController(
        IDimensionTypeService types,
        IDimensionService records,
        IDimensionGraphService graph,
        IStructureService structures,
        IEmployeeAssignmentService assignments,
        IEnumerable<IEmployeeLookup> employeeLookups,
        IDimensionAuthorisation authorisation,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IStringLocalizer<DimensionRecordsAdminController> stringLocalizer,
        IHtmlLocalizer<DimensionRecordsAdminController> htmlLocalizer)
    {
        _types = types;
        _records = records;
        _graph = graph;
        _structures = structures;
        _assignments = assignments;
        _employeeLookups = employeeLookups;
        _authorisation = authorisation;
        _authorizationService = authorizationService;
        _notifier = notifier;
        S = stringLocalizer;
        H = htmlLocalizer;
    }

    // ---- the list ----------------------------------------------------------------------

    /// <param name="columns">
    /// The attribute names this reader wants as columns. Off by default and carried on the query
    /// string, so a useful arrangement is a URL somebody can bookmark and send to a colleague.
    /// </param>
    public async Task<IActionResult> Index(
        string dimensionTypeId,
        string? search,
        string? structureId,
        string? within,
        DimensionRecordStatus status = DimensionRecordStatus.Active,
        string[]? columns = null,
        string? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var type = await _types.GetAsync(dimensionTypeId, cancellationToken: cancellationToken);

        if (type is null)
        {
            return NotFound();
        }

        var date = IsoDate.TryParse(asAt, out var parsed) ? parsed : await _authorisation.TodayAsync();
        var structures = await StructuresForAsync(type, cancellationToken);
        var scope = ResolveStructure(structures, structureId);

        var found = await _graph.SearchRecordsOfTypeAsync(
            dimensionTypeId, search, scope?.StructureId, within, status, date, skip, take, cancellationToken);

        var chosenColumns = (columns ?? []).ToHashSet(StringComparer.Ordinal);

        var model = new DimensionRecordListViewModel
        {
            DimensionTypeId = type.DimensionTypeId,
            TypeCode = type.Code,
            TypeName = BilingualText.Display(type.Name.En, type.Name.Ar),
            Search = search,
            StructureId = scope?.StructureId,
            Within = within,
            Status = status,
            AsAtIso = date.ToIso(),
            Skip = skip,
            Take = take,
            Total = found.Total,
            Structures = structures,
            CanEdit = await CanEditAsync(),
            CanViewEmployees = await CanViewEmployeesAsync(),
            AttributeColumns =
            [
                .. type.AttributeSchema.Select(attribute => new DimensionAttributeColumnViewModel(
                    attribute.Name,
                    BilingualText.Display(attribute.Label.En, attribute.Label.Ar),
                    chosenColumns.Contains(attribute.Name))),
            ],
        };

        model.Rows = await RowsAsync(model, found.Items, structures, scope, date, cancellationToken);

        return View(model);
    }

    /// <summary>
    /// Fills a page of rows: where each record sits on each structure, how many people are at it,
    /// and whichever attribute columns the reader asked for.
    /// </summary>
    /// <remarks>
    /// Batched per structure, never per row. The paths are two queries for the page, the depths one
    /// more, and the headcounts one — so a page of fifty rows on a tenant with two structures costs
    /// a fixed handful of queries rather than two hundred.
    ///
    /// The attribute values are the exception and are read one per record, because they live on the
    /// record's own content part rather than in an index. They are only read when a reader has
    /// actually asked for a column, which is the common case of not paying for it.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionRecordRowViewModel>> RowsAsync(
        DimensionRecordListViewModel model,
        IReadOnlyList<DimensionNodeRef> records,
        IReadOnlyList<StructureChoiceViewModel> structures,
        StructureChoiceViewModel? scope,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var recordIds = records.Select(record => record.RecordId).ToList();

        // The structure chosen, or every structure this type is on when none was. A reader looking
        // at a type that only ever appears on one structure should not have to choose it first.
        var reported = scope is null ? structures : [scope];
        var placements = new Dictionary<string, List<DimensionRecordPlacementViewModel>>(StringComparer.Ordinal);

        foreach (var structure in reported)
        {
            var paths = await _graph.GetAncestorPathsAsync(
                structure.StructureId, recordIds, asAt, cancellationToken);

            var depths = await _graph.GetDepthsAsync(
                structure.StructureId, recordIds, asAt, cancellationToken);

            foreach (var recordId in recordIds)
            {
                var ancestors = paths.GetValueOrDefault(recordId) ?? [];

                var state = !depths.ContainsKey(recordId)
                    ? DimensionRecordPlacementState.Unplaced
                    : ancestors.Count == 0
                        ? DimensionRecordPlacementState.TopLevel
                        : DimensionRecordPlacementState.Placed;

                if (!placements.TryGetValue(recordId, out var forRecord))
                {
                    placements[recordId] = forRecord = [];
                }

                forRecord.Add(new DimensionRecordPlacementViewModel(
                    structure.StructureId,
                    structure.Name,
                    state == DimensionRecordPlacementState.Placed
                        ? string.Join(BilingualDisplay.PathSeparator,
                            ancestors.Select(ancestor => BilingualDisplay.Name(ancestor.NameEn, ancestor.NameAr)))
                        : string.Empty,
                    state));
            }
        }

        // Headcounts are a question about one structure — an employee is placed on one — so they
        // are only shown when the screen is reporting on one.
        var counts = scope is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : await _assignments.CountEmployeesAtAsync(scope.StructureId, recordIds, asAt, cancellationToken);

        var shown = model.ShownColumns;
        var rows = new List<DimensionRecordRowViewModel>();

        foreach (var record in records)
        {
            var values = new List<string>();

            if (shown.Count > 0)
            {
                var attributes = (await _records.GetAttributeValuesAsync(record.RecordId, cancellationToken))
                    .ToDictionary(attribute => attribute.Name, StringComparer.Ordinal);

                values.AddRange(shown.Select(column =>
                    attributes.TryGetValue(column.Name, out var value)
                        ? BilingualText.Display(value.Value, value.ValueAr)
                        : string.Empty));
            }

            rows.Add(new DimensionRecordRowViewModel
            {
                RecordId = record.RecordId,
                Code = record.Code,
                NameEn = record.NameEn,
                NameAr = record.NameAr,
                IsEffective = record.EffectiveRange.Contains(asAt),
                From = record.EffectiveRange.From,
                To = record.EffectiveRange.To,
                IsActive = record.IsActive,
                EmployeeCount = counts.TryGetValue(record.RecordId, out var count) ? count : null,
                Placements = placements.GetValueOrDefault(record.RecordId) ?? [],
                AttributeValues = values,
            });
        }

        return rows;
    }

    // ---- creating one ------------------------------------------------------------------

    public async Task<IActionResult> Create(
        string dimensionTypeId,
        string? structureId,
        CancellationToken cancellationToken)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var model = new DimensionRecordCreateViewModel
        {
            DimensionTypeId = dimensionTypeId,
            StructureId = structureId,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
        };

        return await PrepareCreateAsync(model, cancellationToken) is { } failure
            ? failure
            : View(model);
    }

    [HttpPost]
    [ActionName(nameof(Create))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(
        DimensionRecordCreateViewModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await CanEditAsync())
        {
            return Forbid();
        }

        if (await PrepareCreateAsync(model, cancellationToken) is { } failure)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(model.StructureId))
        {
            ModelState.AddModelError(nameof(model.StructureId), S["Choose a structure."].Value);

            return View(model);
        }

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["Enter the date this unit starts."].Value);

            return View(model);
        }

        // The one write: the record and its dated link together, validated before either is
        // written. The same call the designer's Add unit screen makes.
        var result = await _records.AddUnitAsync(
            model.StructureId,
            string.IsNullOrEmpty(model.ParentRecordId) ? null : model.ParentRecordId,
            model.DimensionTypeId,
            model.Code?.Trim() ?? string.Empty,
            model.Name,
            effectiveFrom,
            model.ToAttributeValues(),
            // No sort order from the form: a unit added here belongs after the siblings that were
            // already there, which is what the service's default does.
            sortOrder: null,
            cancellationToken);

        if (!result.IsAuthorised)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors);

            return View(model);
        }

        await _notifier.SuccessAsync(H["{0} '{1}' was created.", model.TypeName, result.Value!.NameEn]);

        return RedirectToAction(nameof(Index), new
        {
            dimensionTypeId = model.DimensionTypeId,
            structureId = model.StructureId,
        });
    }

    /// <summary>
    /// Fills in everything the create form shows but does not post back: the type's name, the
    /// structures it is a level of, whether it may sit at the top of the chosen one, and its
    /// attribute inputs.
    /// </summary>
    private async Task<IActionResult?> PrepareCreateAsync(
        DimensionRecordCreateViewModel model,
        CancellationToken cancellationToken)
    {
        var type = await _types.GetAsync(model.DimensionTypeId, cancellationToken: cancellationToken);

        if (type is null)
        {
            return NotFound();
        }

        model.TypeName = BilingualText.Display(type.Name.En, type.Name.Ar);
        model.Structures = await StructuresForAsync(type, cancellationToken);

        var scope = ResolveStructure(model.Structures, model.StructureId);

        model.StructureId = scope?.StructureId;

        if (scope is not null &&
            await _structures.GetAsync(scope.StructureId, cancellationToken) is { } structure)
        {
            model.MayBeTopLevel = structure.PermitsRoot(type.DimensionTypeId);

            // Whether there is anywhere at all for one of these. A type that is neither a root nor
            // permitted under anything is a configuration mistake, and saying so beats a picker
            // that offers nothing and a save that is refused.
            var parents = await _graph.SearchPermittedParentsAsync(
                scope.StructureId, type.DimensionTypeId, searchText: null, withinRecordId: null,
                asAt: null, take: 1, cancellationToken);

            model.HasAnywhereToGo = model.MayBeTopLevel || parents.Total > 0;
        }

        MergeAttributeInputs(model.Attributes, type);

        return null;
    }

    // ---- editing one -------------------------------------------------------------------

    public async Task<IActionResult> Edit(string id, string? structureId, CancellationToken cancellationToken)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var record = await _records.GetAsync(id, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        var model = new DimensionRecordEditViewModel
        {
            RecordId = record.RecordId,
            Code = record.Code,
            NameEn = record.NameEn,
            NameAr = record.NameAr,
            StructureId = structureId,
            EffectiveFrom = (await _authorisation.TodayAsync()).ToIso(),
        };

        return await PrepareEditAsync(model, record, cancellationToken) is { } failure
            ? failure
            : View(model);
    }

    [HttpPost]
    [ActionName(nameof(Edit))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(
        string id,
        DimensionRecordEditViewModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!await CanEditAsync())
        {
            return Forbid();
        }

        var record = await _records.GetAsync(id, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        model.RecordId = record.RecordId;

        if (await PrepareEditAsync(model, record, cancellationToken) is { } failure)
        {
            return failure;
        }

        if (!IsoDate.TryParse(model.EffectiveFrom, out var effectiveFrom))
        {
            ModelState.AddModelError(nameof(model.EffectiveFrom), S["A date is needed."].Value);

            return View(model);
        }

        // The name first, and only when it actually changed. Saving an unchanged name as a
        // substantive rename would write a new name period starting today, which is a dated claim
        // that the unit became something else — on a form somebody opened to fix a cost centre.
        if (model.NameChanged)
        {
            var renamed = model.IsCorrection
                ? await _records.CorrectNameAsync(record.RecordId, model.Name, effectiveFrom, cancellationToken)
                : await _records.RenameAsync(record.RecordId, model.Name, effectiveFrom, cancellationToken);

            if (!renamed.IsAuthorised)
            {
                return Forbid();
            }

            if (!renamed.Succeeded)
            {
                AddErrors(renamed.Errors);

                return View(model);
            }
        }

        if (model.Attributes.Count > 0)
        {
            var written = await _records.SetAttributeValuesAsync(
                record.RecordId, model.ToAttributeValues(), cancellationToken);

            if (!written.IsAuthorised)
            {
                return Forbid();
            }

            if (!written.Succeeded)
            {
                AddErrors(written.Errors);

                return View(model);
            }
        }

        await _notifier.SuccessAsync(H["'{0}' was updated.", model.Name.En]);

        return RedirectToAction(nameof(Index), new
        {
            dimensionTypeId = model.DimensionTypeId,
            structureId = model.StructureId,
        });
    }

    private async Task<IActionResult?> PrepareEditAsync(
        DimensionRecordEditViewModel model,
        DimensionNodeRef record,
        CancellationToken cancellationToken)
    {
        var type = await _types.GetAsync(record.DimensionTypeId, cancellationToken: cancellationToken);

        if (type is null)
        {
            return NotFound();
        }

        model.DimensionTypeId = type.DimensionTypeId;
        model.TypeName = BilingualText.Display(type.Name.En, type.Name.Ar);
        model.Code = record.Code;
        model.CurrentNameEn = record.NameEn;
        model.CurrentNameAr = record.NameAr;

        var today = await _authorisation.TodayAsync();
        var structures = await StructuresForAsync(type, cancellationToken);
        var placements = new List<DimensionRecordPlacementViewModel>();

        foreach (var structure in structures)
        {
            var ancestors = (await _graph.GetAncestorPathsAsync(
                structure.StructureId, [record.RecordId], today, cancellationToken))
                .GetValueOrDefault(record.RecordId) ?? [];

            var onIt = (await _graph.GetDepthsAsync(
                structure.StructureId, [record.RecordId], today, cancellationToken))
                .ContainsKey(record.RecordId);

            placements.Add(new DimensionRecordPlacementViewModel(
                structure.StructureId,
                structure.Name,
                string.Join(BilingualDisplay.PathSeparator,
                    ancestors.Select(ancestor => BilingualDisplay.Name(ancestor.NameEn, ancestor.NameAr))),
                !onIt
                    ? DimensionRecordPlacementState.Unplaced
                    : ancestors.Count == 0
                        ? DimensionRecordPlacementState.TopLevel
                        : DimensionRecordPlacementState.Placed));
        }

        model.Placements = placements;

        MergeAttributeInputs(model.Attributes, type);

        // The stored values, where the reader has not typed over them. A redrawn form after a
        // validation failure keeps what was typed; a freshly opened one shows what is stored.
        var stored = (await _records.GetAttributeValuesAsync(record.RecordId, cancellationToken))
            .ToDictionary(attribute => attribute.Name, StringComparer.Ordinal);

        foreach (var input in model.Attributes.Where(input =>
            input.Value is null && input.ValueAr is null && stored.ContainsKey(input.Name)))
        {
            input.Value = stored[input.Name].Value;
            input.ValueAr = stored[input.Name].ValueAr;
        }

        return null;
    }

    // ---- shared ------------------------------------------------------------------------

    /// <summary>
    /// Lines the posted attribute inputs up with the schema the type actually declares, in schema
    /// order, keeping whatever the user had typed.
    /// </summary>
    /// <remarks>
    /// Rebuilt from the schema rather than trusted from the form, so a hand-edited post cannot
    /// introduce an input for an attribute the type does not have, and so a form redrawn after a
    /// validation failure matches the schema even if the type was changed in another tab in
    /// between. The designer's own add-unit form does the same; the service re-checks every
    /// submitted name against the real schema regardless.
    /// </remarks>
    private static void MergeAttributeInputs(List<UnitAttributeInputViewModel> inputs, DimensionTypeDocument type)
    {
        var submitted = inputs.ToDictionary(attribute => attribute.Name, StringComparer.Ordinal);

        inputs.Clear();

        inputs.AddRange(type.AttributeSchema.Select(definition =>
        {
            var input = UnitAttributeInputViewModel.Of(definition);

            if (submitted.TryGetValue(definition.Name, out var posted))
            {
                input.Value = posted.Value;
                input.ValueAr = posted.ValueAr;
            }

            return input;
        }));
    }

    /// <summary>The structures this type is a level of, in code order.</summary>
    /// <remarks>
    /// By level rather than by what is placed: a type that is a level of a structure belongs on it
    /// whether or not anybody has created a record yet, and a screen that hid an empty structure
    /// would hide the one somebody is about to fill in.
    /// </remarks>
    private async Task<IReadOnlyList<StructureChoiceViewModel>> StructuresForAsync(
        DimensionTypeDocument type,
        CancellationToken cancellationToken) =>
        [
            .. (await _structures.ListAsync(cancellationToken))
                .Where(structure => structure.OrdinalOf(type.DimensionTypeId) is not null)
                .OrderBy(structure => structure.Code, StringComparer.Ordinal)
                .Select(structure => new StructureChoiceViewModel(
                    structure.StructureId,
                    structure.Code,
                    BilingualText.Display(structure.Name.En, structure.Name.Ar))),
        ];

    /// <summary>
    /// The structure asked for, or the only one, or none.
    /// </summary>
    /// <remarks>
    /// Defaulting to "the only one" is what makes the common tenant — one organisation chart —
    /// never have to answer a question with one possible answer. Defaulting to an arbitrary first
    /// when there are several would be a guess, and the Parent column and the headcount would
    /// silently be about a structure nobody chose.
    /// </remarks>
    private static StructureChoiceViewModel? ResolveStructure(
        IReadOnlyList<StructureChoiceViewModel> structures,
        string? structureId)
    {
        if (!string.IsNullOrEmpty(structureId))
        {
            return structures.FirstOrDefault(structure =>
                string.Equals(structure.StructureId, structureId, StringComparison.Ordinal));
        }

        return structures.Count == 1 ? structures[0] : null;
    }

    /// <summary>
    /// Who may open the list. The same set the organisation designer admits, because it is the same
    /// data seen a different way, and a reader allowed to look at the chart is allowed to look at
    /// the list of what is on it.
    /// </summary>
    private async Task<bool> CanViewAsync() =>
        await _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords)
        || await _authorizationService.AuthorizeAsync(User, Permissions.ViewDimensionHistory)
        || await _authorizationService.AuthorizeAsync(User, Permissions.MoveDimensionRecords)
        || await _authorizationService.AuthorizeAsync(User, Permissions.MergeDimensionRecords);

    private Task<bool> CanEditAsync() =>
        _authorizationService.AuthorizeAsync(User, Permissions.ManageDimensionRecords);

    /// <summary>
    /// Whether this reader may follow a headcount into the employee list, asked of the module that
    /// owns employees — the permission is theirs, and on a tenant without that module there is no
    /// list to open.
    /// </summary>
    private async Task<bool> CanViewEmployeesAsync()
    {
        var lookup = _employeeLookups.FirstOrDefault();

        return lookup is not null && await lookup.CanViewEmployeeListAsync();
    }

    /// <summary>
    /// Puts each violation where the reader has to act on it: under its own field when the rule
    /// names one, in the summary when it does not.
    /// </summary>
    private void AddErrors(IReadOnlyList<DimensionError> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(error.Field ?? string.Empty, error.Message.Value);
        }
    }
}
