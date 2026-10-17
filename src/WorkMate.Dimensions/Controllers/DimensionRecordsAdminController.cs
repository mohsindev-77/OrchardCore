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
    /// <summary>
    /// How many values tree view loads at once.
    /// </summary>
    /// <remarks>
    /// <b>A tree and a page cannot be combined.</b> A value whose parent happens to be on another
    /// page would be drawn as a root of the dimension, which is not a partial answer but a wrong
    /// one — and a wrong one that looks right. So tree view asks for the whole dimension up to this
    /// bound and tells the reader when it stopped early, rather than paging and quietly reshaping
    /// the tenant's organisation.
    ///
    /// Five hundred is chosen to be past any real single dimension — Crescent, the ragged reference
    /// tenant, has twenty-odd departments — while still bounded, because "load them all" on a
    /// dimension somebody has imported badly is how a screen becomes a denial of service. List view
    /// is unaffected and still pages.
    /// </remarks>
    internal const int TreeCap = 500;

    private readonly IDimensionTypeService _types;
    private readonly IDimensionService _records;
    private readonly IDimensionGraphService _graph;
    private readonly IStructureService _structures;
    private readonly IEmployeeAssignmentService _assignments;

    /// <summary>
    /// The position-shaped reads: the reporting-line axis, whether a position can be placed yet,
    /// and the three establishment figures the Positions list draws. Decisions D26 E and D26 F.
    /// </summary>
    private readonly IPositionService _positions;

    /// <summary>The authority on what a position may say it reports to. D31.</summary>
    private readonly IDimensionValidator _validator;

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
        IPositionService positions,
        IDimensionValidator validator,
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
        _positions = positions;
        _validator = validator;
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
    /// <param name="view">
    /// Tree or list, FRS v2.5 §4.1. On the query string rather than in a cookie, so a tree somebody
    /// is looking at is a URL they can send to a colleague — which is also what makes the toggle a
    /// pair of ordinary links that work with no script.
    /// </param>
    /// <param name="group">
    /// A dimension <em>name</em> rather than a type: the Organisation menu's <em>All Departments</em>
    /// entry, decision D23 C. Every type sharing this English singular name is listed on one screen,
    /// which is what lets a tenant with three Department types stop seeing three menu entries without
    /// having to share one type — the fix D17 proposed, which was built and reverted because a shared
    /// type belongs to every chart that declares it.
    ///
    /// With <paramref name="dimensionTypeId"/> as well, the group is narrowed to that one type: that
    /// is the Organisation filter, which is a filter on the type because each of a group's types is
    /// one organisation's.
    /// </param>
    public async Task<IActionResult> Index(
        string? dimensionTypeId,
        string? search,
        string? structureId,
        string? within,
        string? group = null,
        DimensionRecordStatus status = DimensionRecordStatus.Active,
        string[]? columns = null,
        string? asAt = null,
        DimensionRecordView view = DimensionRecordView.List,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var scopeOfScreen = await ResolveGroupAsync(dimensionTypeId, group, cancellationToken);

        if (scopeOfScreen is null)
        {
            return NotFound();
        }

        var (groupKey, groupCaption, siblings, listed) = scopeOfScreen;

        var date = IsoDate.TryParse(asAt, out var parsed) ? parsed : await _authorisation.TodayAsync();
        var structures = await StructuresForAsync(listed, cancellationToken);
        var scope = ResolveStructure(structures, structureId);

        // Parentage is scoped to a structure, so a type used by two axes has two trees and the
        // screen cannot draw "the" one. The same refusal to guess the Parent column and the
        // headcount already make; see ResolveStructure. A group spanning several organisations is
        // the same problem arriving a second way.
        var treeNeedsAStructure = view == DimensionRecordView.Tree && (scope is null || listed.Count > 1);
        var tree = view == DimensionRecordView.Tree && !treeNeedsAStructure;

        var found = await _graph.SearchRecordsOfTypesAsync(
            [.. listed.Select(type => type.DimensionTypeId)],
            search,
            scope?.StructureId,
            within,
            status,
            date,
            tree ? 0 : skip,
            tree ? TreeCap : take,
            cancellationToken);

        var chosenColumns = (columns ?? []).ToHashSet(StringComparer.Ordinal);
        var only = listed.Count == 1 ? listed[0] : null;

        var model = new DimensionRecordListViewModel
        {
            DimensionTypeId = only?.DimensionTypeId ?? string.Empty,
            TypeCode = only?.Code ?? string.Empty,
            TypeName = only is null
                ? groupCaption
                : BilingualText.Display(only.Name.En, only.Name.Ar),
            Group = groupKey,
            GroupCaption = groupCaption,
            GroupTypes = await GroupChoicesAsync(siblings, cancellationToken),
            View = view,
            Search = search,
            StructureId = scope?.StructureId,
            Within = within,
            Status = status,
            AsAtIso = date.ToIso(),
            Skip = tree ? 0 : skip,
            Take = tree ? TreeCap : take,
            Total = found.Total,
            Structures = structures,
            TreeNeedsAStructure = treeNeedsAStructure,
            TreeTruncated = tree && found.Total > found.Items.Count,
            CanEdit = await CanEditAsync(),
            CanViewEmployees = await CanViewEmployeesAsync(),

            // Approved / Filled / Vacant and Allowed grades, on a Positions screen only. Asked of
            // every type the screen is listing rather than of one, because a tenant with two
            // companies has two Position types in one group and both are positions.
            ShowsEstablishment = siblings.Count > 0 &&
                siblings.All(type => type.StandardRole == DimensionTypeRole.Position),

            // The attribute columns are one type's own schema, so they are only offered when the
            // screen is about one type. Three Department types can declare three different schemas,
            // and a column that existed for a third of the rows would be a column that looked empty.
            AttributeColumns = only is null
                ?
                [
                ]
                :
                [
                    .. only.AttributeSchema.Select(attribute => new DimensionAttributeColumnViewModel(
                        attribute.Name,
                        BilingualText.Display(attribute.Label.En, attribute.Label.Ar),
                        chosenColumns.Contains(attribute.Name))),
                ],
        };

        model.Rows = await RowsAsync(model, found.Items, structures, scope, date, cancellationToken);

        if (tree)
        {
            model.TreeRows = await TreeRowsAsync(
                model.Rows, scope!.StructureId, only!.DimensionTypeId, date, cancellationToken);
        }

        return View(model);
    }

    /// <summary>
    /// What one screen is about: a dimension group, the types in it, and the ones actually listed.
    /// </summary>
    /// <param name="Siblings">
    /// Every type in the group, whether or not the reader has filtered down to one. What the
    /// Organisation filter offers.
    /// </param>
    /// <param name="Listed">The types whose records the screen shows: all the siblings, or just one.</param>
    private sealed record ScreenScope(
        string GroupKey,
        string Caption,
        IReadOnlyList<DimensionTypeDocument> Siblings,
        IReadOnlyList<DimensionTypeDocument> Listed);

    /// <summary>
    /// Works out which types the screen is about, from a type id, a group name, or both.
    /// </summary>
    /// <remarks>
    /// <b>The group is resolved from the types, not from the menu.</b> The menu hides what a tenant
    /// has switched off (<c>ShowInMenu</c>) and the list screen must not: a hidden dimension's
    /// screens stay reachable from the Dimension types list, which is the promise D3 made when it
    /// introduced the switch. So the grouping rule is shared with the menu —
    /// <see cref="DimensionMenuComposition.KeyOf"/> is the one implementation — and the visibility
    /// filter is not.
    ///
    /// <b>A type id alone still works and is still the common case.</b> Every existing link —
    /// <em>View units</em> on the Dimension types list, every redirect after a save — passes one,
    /// and gets exactly the screen it got before: that type, its own attribute columns, no
    /// Organisation column. The group only widens the screen when a reader asks it to.
    /// </remarks>
    private async Task<ScreenScope?> ResolveGroupAsync(
        string? dimensionTypeId,
        string? group,
        CancellationToken cancellationToken)
    {
        var chosen = string.IsNullOrEmpty(dimensionTypeId)
            ? null
            : await _types.GetAsync(dimensionTypeId, cancellationToken: cancellationToken);

        if (chosen is null && string.IsNullOrEmpty(group))
        {
            return null;
        }

        var key = chosen is not null ? DimensionMenuComposition.KeyOf(chosen) : group!;

        var siblings = (await _types.ListAsync(cancellationToken: cancellationToken))
            .Where(type => string.Equals(DimensionMenuComposition.KeyOf(type), key, StringComparison.Ordinal))
            .OrderBy(type => type.MenuOrder)
            .ThenBy(type => type.Code, StringComparer.Ordinal)
            .ToList();

        if (siblings.Count == 0)
        {
            // A group named by a URL that matches nothing, or a type retired since the menu was
            // built. The chosen type still answers for itself if there was one.
            return chosen is null ? null : new ScreenScope(key, CaptionOf([chosen]), [chosen], [chosen]);
        }

        // Narrowed to one type when the reader asked for one — which is both the old single-type
        // link and the new Organisation filter, and they are the same thing.
        IReadOnlyList<DimensionTypeDocument> listed = chosen is null ? siblings : [chosen];

        return new ScreenScope(key, CaptionOf(siblings), siblings, listed);
    }

    /// <summary>
    /// What the screen calls the group: the first authored plural in it, else the singular.
    /// </summary>
    /// <remarks>
    /// The same rule and the same order as <see cref="DimensionMenuGroup.Caption"/>, so the heading
    /// on the screen is the words on the menu entry that opened it. Never derived — a plural is
    /// authored on the dimension type editor or it is not had, which is ADR-0024.
    /// </remarks>
    private static string CaptionOf(List<DimensionTypeDocument> siblings)
    {
        var plural = siblings
            .Select(type => type.PluralName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name.En) || !string.IsNullOrWhiteSpace(name.Ar));

        var display = plural is null ? string.Empty : BilingualText.Display(plural.En, plural.Ar);

        return string.IsNullOrWhiteSpace(display)
            ? BilingualText.Display(siblings[0].Name.En, siblings[0].Name.Ar)
            : display;
    }

    /// <summary>
    /// The group's types as the Organisation filter offers them: each labelled by the chart it
    /// belongs to, falling back to its own name.
    /// </summary>
    /// <remarks>
    /// <b>Labelled by the organisation rather than by the type</b>, because within a group every
    /// type has the same name — three of them are called Department — so a picker listing their
    /// names would offer the same word three times. What tells them apart is whose chart they are a
    /// level of, which is the same thing the menu puts in brackets after a value.
    ///
    /// A type on no chart, or on several, has no single organisation to name and falls back to its
    /// code: unusual, and better than a blank option.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionGroupTypeViewModel>> GroupChoicesAsync(
        IReadOnlyList<DimensionTypeDocument> siblings,
        CancellationToken cancellationToken)
    {
        if (siblings.Count < 2)
        {
            return [];
        }

        var structures = await _structures.ListAsync(cancellationToken);
        var choices = new List<DimensionGroupTypeViewModel>();

        foreach (var type in siblings)
        {
            var onIt = structures
                .Where(structure => structure.OrdinalOf(type.DimensionTypeId) is not null)
                .ToList();

            choices.Add(new DimensionGroupTypeViewModel(
                type.DimensionTypeId,
                onIt.Count == 1
                    ? BilingualText.Display(onIt[0].Name.En, onIt[0].Name.Ar)
                    : type.Code));
        }

        return choices;
    }

    /// <summary>
    /// Arranges a dimension's own values into its own tree: indented under the nearest value
    /// <em>of the same dimension</em> above them, and nothing else.
    /// </summary>
    /// <remarks>
    /// FRS v2.5 Rule 2 — <em>"a tree view only shows the values that belong to that one
    /// dimension"</em>. A Department sits under a Division on the organisation chart; on the
    /// Department screen it sits under <em>HR</em>, or at the top. So the parent here is the last
    /// same-type entry in the record's ancestor chain, and a record whose chain has none is a root
    /// of this dimension however deep it sits on the chart.
    ///
    /// <b>One batched query for the whole set, not one per node.</b>
    /// <see cref="IDimensionGraphService.GetAncestorPathsAsync"/> already answers it in two queries
    /// for every record at once, and it already returns each chain root-first with each entry's
    /// depth — which is why the nearest same-type ancestor is the <em>last</em> match rather than
    /// the first. Asking for children per node instead would be a query per row, on the screen most
    /// likely to have a lot of them.
    ///
    /// <b>A parent outside the loaded set makes its child a root.</b> That can only happen when the
    /// search or the status filter has excluded the parent, or when the dimension has more values
    /// than <see cref="TreeCap"/>. Both are stated on the screen rather than left to look like the
    /// tenant's real shape — a filtered tree says it is filtered, and a capped one says it is
    /// capped.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionRecordRowViewModel>> TreeRowsAsync(
        IReadOnlyList<DimensionRecordRowViewModel> rows,
        string structureId,
        string dimensionTypeId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var present = rows.ToDictionary(row => row.RecordId, StringComparer.Ordinal);

        var paths = await _graph.GetAncestorPathsAsync(
            structureId, [.. rows.Select(row => row.RecordId)], asAt, cancellationToken);

        var childrenOf = new Dictionary<string, List<DimensionRecordRowViewModel>>(StringComparer.Ordinal);
        var roots = new List<DimensionRecordRowViewModel>();

        foreach (var row in rows)
        {
            // Root-first, so the nearest ancestor of this dimension is the last one that matches.
            var parent = (paths.GetValueOrDefault(row.RecordId) ?? [])
                .LastOrDefault(ancestor =>
                    string.Equals(ancestor.DimensionTypeId, dimensionTypeId, StringComparison.Ordinal) &&
                    present.ContainsKey(ancestor.RecordId));

            if (parent is null)
            {
                roots.Add(row);

                continue;
            }

            row.ParentRecordId = parent.RecordId;

            if (!childrenOf.TryGetValue(parent.RecordId, out var siblings))
            {
                childrenOf[parent.RecordId] = siblings = [];
            }

            siblings.Add(row);
        }

        foreach (var row in rows)
        {
            row.ChildCount = childrenOf.TryGetValue(row.RecordId, out var children) ? children.Count : 0;
        }

        var ordered = new List<DimensionRecordRowViewModel>(rows.Count);

        // Iterative rather than recursive: the depth of a customer's tree is their business, and a
        // stack overflow on an imported cycle would be a crash rather than a message. Cycles cannot
        // reach here — the engine refuses them across every date — but a screen is the wrong place
        // to rely on that.
        var pending = new Stack<(DimensionRecordRowViewModel Row, int Depth)>();

        foreach (var root in Enumerable.Reverse(roots))
        {
            pending.Push((root, 0));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var (row, depth) = pending.Pop();

            if (!seen.Add(row.RecordId))
            {
                continue;
            }

            row.Depth = depth;
            ordered.Add(row);

            if (!childrenOf.TryGetValue(row.RecordId, out var children))
            {
                continue;
            }

            foreach (var child in Enumerable.Reverse(children))
            {
                pending.Push((child, depth + 1));
            }
        }

        return ordered;
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

        // The three establishment figures and the grades, on a Positions screen only (D26 C, D26 E).
        // Both reads are batched over the whole page; neither runs at all for any other dimension,
        // which is what keeps decision D25's "the All <plural> screens do not change" true of them.
        var establishment = model.ShowsEstablishment
            ? await _positions.GetEstablishmentAsync(recordIds, asAt, cancellationToken)
            : null;

        var allowedGrades = model.ShowsEstablishment
            ? await _records.GetReferenceSetsAsync(
                recordIds, DimensionReferenceKinds.AllowedGrade, asAt, cancellationToken)
            : null;

        // Which organisation each row belongs to, when the screen is showing more than one type's
        // records. The same rule the menu uses for its brackets: a type on exactly one chart is that
        // chart's, and a type on several has no single answer, so the cell stays empty rather than
        // naming one of them.
        var organisationOfType = model.GroupTypes.ToDictionary(
            choice => choice.DimensionTypeId, choice => choice.Organisation, StringComparer.Ordinal);

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
                DimensionTypeId = record.DimensionTypeId,
                Organisation = organisationOfType.GetValueOrDefault(record.DimensionTypeId, string.Empty),
                Code = record.Code,
                NameEn = record.NameEn,
                NameAr = record.NameAr,
                AliasEn = record.AliasEn,
                AliasAr = record.AliasAr,
                IsEffective = record.EffectiveRange.Contains(asAt),
                From = record.EffectiveRange.From,
                To = record.EffectiveRange.To,
                IsActive = record.IsActive,
                EmployeeCount = counts.TryGetValue(record.RecordId, out var count) ? count : null,
                Placements = placements.GetValueOrDefault(record.RecordId) ?? [],
                AttributeValues = values,
                Establishment = establishment?.GetValueOrDefault(record.RecordId),
                AllowedGradeNames =
                [
                    .. (allowedGrades?.GetValueOrDefault(record.RecordId) ?? [])
                        .Select(grade => BilingualDisplay.Name(grade.NameEn, grade.NameAr)),
                ],
            });
        }

        return rows;
    }

    // ---- one value's own page ----------------------------------------------------------

    /// <summary>
    /// What one dimension value is: its code, names, alias, type, parent, status, head and
    /// headcount — with Edit, Move and Retire beside the facts. Decision D23 C.
    /// </summary>
    /// <remarks>
    /// <b>Where the Organisation menu's values land.</b> The menu expands to a dimension's own
    /// values, and clicking <em>Civil</em> opens this. Nothing else in the module answered "what is
    /// this unit" without first being navigated to: the list is about a dimension, the editor is a
    /// form, and the designer is a chart.
    ///
    /// <b>Read-only, and every action is a link to a screen that already owns its rules.</b> Edit is
    /// this controller's own editor; Move and Retire are the designer's, with their dated dry runs
    /// intact. There is no write here, so there is no second write path to keep in step — the same
    /// promise the list screen makes.
    ///
    /// <b>Dated, like everything else.</b> <paramref name="asAt"/> defaults to today in the tenant's
    /// time zone, and the head, the headcount and the parent path are all resolved on it, so the
    /// page answers "what was this in March" by changing one query-string value.
    /// </remarks>
    /// <param name="structureId">
    /// Which chart the parent, the head and the headcount are about. Defaulted when the type is a
    /// level of exactly one, which is the ordinary tenant; left unanswered rather than guessed when
    /// it is a level of several.
    /// </param>
    /// <param name="includeRetired">
    /// Whether the <em>Units under this one</em> tree shows units that are no longer in use. Off by
    /// default: a chart is normally a question about today.
    /// </param>
    /// <param name="view">
    /// Which of the three renderings of the <em>Units under this one</em> section to draw —
    /// hierarchy, tree or list (decision D25). On the query string rather than in a cookie alone,
    /// so a view somebody is looking at is a URL they can send and the back button returns to the
    /// one they were on. Unrecognised or absent means <see cref="UnitsView.Tree"/>, which is what
    /// the section already was.
    /// </param>
    /// <param name="sort">
    /// Which column the <b>list</b> view is sorted by: <c>code</c>, <c>name</c>,
    /// <c>dimension</c>, <c>parent</c>, <c>status</c> or <c>employees</c>. Absent means tree
    /// order, which is what the other two views show.
    /// </param>
    public async Task<IActionResult> Details(
        string? id,
        string? structureId,
        string? asAt = null,
        bool includeRetired = false,
        string? view = null,
        string? sort = null,
        bool desc = false,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(id))
        {
            return NotFound();
        }

        var record = await _records.GetAsync(id, null, cancellationToken);

        if (record is null)
        {
            return NotFound();
        }

        var type = await _types.GetAsync(record.DimensionTypeId, cancellationToken: cancellationToken);

        if (type is null)
        {
            return NotFound();
        }

        var date = IsoDate.TryParse(asAt, out var parsed) ? parsed : await _authorisation.TodayAsync();
        var structures = await StructuresForAsync(type, cancellationToken);
        var isPosition = type.StandardRole == DimensionTypeRole.Position;

        // A position's page is ALWAYS about its reporting line, whatever the URL says.
        //
        // A position is on two axes (ADR-0028), and only one of them is a tree it is the parent of:
        // on the organisation chart a post is a leaf under a department. A page scoped there has no
        // section at all, which is what manual testing found — Manager Technology and Site
        // Supervisor each have posts reporting to them and each drew nothing, because the reader
        // had arrived from a list carrying the organisation's structureId. Defaulting to the
        // reporting line only when nothing was asked for was not enough: every link into this page
        // asks for something.
        //
        // Where the post sits in the business is not lost — it is the Placements row above, which
        // names every axis. What is on the chart is who answers to it, which is the question this
        // page exists to answer.
        var scope = isPosition
            ? await ReportingAxisAsync(structures, cancellationToken)
            : ResolveStructure(structures, structureId);

        var siblings = await ResolveGroupAsync(type.DimensionTypeId, group: null, cancellationToken);

        var model = new DimensionRecordDetailsViewModel
        {
            RecordId = record.RecordId,
            Code = record.Code,
            NameEn = record.NameEn,
            NameAr = record.NameAr,
            AliasEn = record.AliasEn,
            AliasAr = record.AliasAr,
            DimensionTypeId = type.DimensionTypeId,
            TypeName = BilingualText.Display(type.Name.En, type.Name.Ar),
            Group = siblings?.GroupKey ?? string.Empty,
            GroupCaption = siblings?.Caption ?? BilingualText.Display(type.Name.En, type.Name.Ar),
            Organisation = siblings is { Siblings.Count: > 1 }
                ? (await GroupChoicesAsync(siblings.Siblings, cancellationToken))
                    .FirstOrDefault(choice => string.Equals(
                        choice.DimensionTypeId, type.DimensionTypeId, StringComparison.Ordinal))
                    ?.Organisation ?? string.Empty
                : string.Empty,
            IsEffective = record.EffectiveRange.Contains(date),
            IsActive = record.IsActive,
            From = record.EffectiveRange.From,
            To = record.EffectiveRange.To,
            AsAtIso = date.ToIso(),
            StructureId = scope?.StructureId,
            Structures = structures,
            IncludeRetired = includeRetired,
            UnitsView = Enum.TryParse<UnitsView>(view, ignoreCase: true, out var chosen)
                ? chosen
                : UnitsView.Tree,
            Sort = sort,
            SortDescending = desc,
            CanEdit = await CanEditAsync(),
            CanMove = await _authorizationService.AuthorizeAsync(User, Permissions.MoveDimensionRecords),
            CanViewEmployees = await CanViewEmployeesAsync(),

            // Set here rather than after the reads below, because what the page shows at all — the
            // Head row, the headcount, the Structure chooser, the section's heading — hangs off it.
            IsPosition = isPosition,
            IsGrade = type.StandardRole == DimensionTypeRole.Grade,
            IsReportingAxis = isPosition && scope is not null,
        };

        var placements = new List<DimensionRecordPlacementViewModel>();

        foreach (var structure in structures)
        {
            var ancestors = (await _graph.GetAncestorPathsAsync(
                structure.StructureId, [record.RecordId], date, cancellationToken))
                .GetValueOrDefault(record.RecordId) ?? [];

            var onIt = (await _graph.GetDepthsAsync(
                structure.StructureId, [record.RecordId], date, cancellationToken))
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

        // The head and the headcount are questions about one chart, so they are only asked when the
        // page is reporting on one. See the view model's StructureId for why that is a refusal to
        // guess rather than a gap.
        if (scope is not null)
        {
            // A head is a unit's, never a post's (ADR-0012): somebody heads Finance, and nobody
            // "heads" the HR Manager position. Asking at all on a reporting line would always answer
            // nothing, and the page would print "Head: Vacant" at a reader about a question that
            // does not apply to what they are looking at.
            if (!model.IsReportingAxis &&
                await _assignments.GetHeadAsync(scope.StructureId, record.RecordId, date, cancellationToken)
                    is { } head)
            {
                var employee = await ResolveEmployeeAsync(head.EmployeeId, cancellationToken);

                model.HeadName = employee?.ForCulture() ?? head.EmployeeId;
                model.HeadFrom = head.Range.From;
                model.HeadTo = head.Range.To;
            }

            await FillUnitsBelowAsync(model, scope.StructureId, record, date, includeRetired, cancellationToken);

            // The chain ABOVE, which the section below cannot show: it draws what is under this
            // post, and a leaf of the reporting line would otherwise say nothing about where on the
            // line it sits. One more ancestor-path read, on the axis the page is already about.
            if (model.IsReportingAxis)
            {
                model.ReportsTo = await PathAboveAsync(
                    scope.StructureId, record.RecordId, date, cancellationToken);
            }
        }

        if (isPosition)
        {
            await FillOrganisationPathAsync(model, structures, record.RecordId, date, cancellationToken);
        }

        var values = (await _records.GetAttributeValuesAsync(record.RecordId, cancellationToken))
            .ToDictionary(attribute => attribute.Name, StringComparer.Ordinal);

        model.Attributes =
        [
            // Approved headcount is not listed as an attribute on a position: it is the first
            // number of the Headcount row, and printing it again under its raw label would be one
            // figure twice under two names. It stays an ordinary attribute everywhere else,
            // including on the editor that sets it — this hides a row, not a field.
            .. type.AttributeSchema
                .Where(definition => !isPosition || !string.Equals(
                    definition.Name,
                    PositionEstablishment.ApprovedHeadcountAttribute,
                    StringComparison.Ordinal))
                .Select(definition => new DimensionRecordAttributeViewModel(
                    BilingualText.Display(definition.Label.En, definition.Label.Ar),
                    values.TryGetValue(definition.Name, out var value)
                        ? BilingualText.Display(value.Value, value.ValueAr)
                        : string.Empty)),
        ];

        // A grade's page says which positions allow it (D26 C). Off the type's declared role, never
        // off its code or its name — DimensionTypeRole sets out why both of those break. Both roles
        // were read onto the model above, where the page's shape is decided.
        if (model.IsGrade)
        {
            await FillUsedByPositionsAsync(model, record.RecordId, date, cancellationToken);
        }

        if (model.IsPosition)
        {
            // The same three numbers the Positions list draws, from the same read (D26 E), and the
            // grades beside them (D26 C) — on the page somebody opened to find out about this post
            // rather than only as a badge on a chart.
            model.Establishment = (await _positions.GetEstablishmentAsync(
                [record.RecordId], date, cancellationToken)).GetValueOrDefault(record.RecordId);

            model.AllowedGrades =
            [
                .. (await _positions.GetAllowedGradesAsync(record.RecordId, date, cancellationToken))
                    .Select(grade => new DimensionRecordReferrerViewModel(
                        grade.RecordId,
                        grade.Code,
                        BilingualDisplay.Name(grade.NameEn, grade.NameAr),
                        UnitName: string.Empty)),
            ];

            await FillHoldersAsync(model, record.RecordId, date, cancellationToken);
        }

        return View(model);
    }

    /// <summary>
    /// Who holds this position today: the employee, the grade and the day they took it.
    /// Decisions D26 D and D26 E.
    /// </summary>
    /// <remarks>
    /// <b>Three batched reads, whatever the headcount.</b> One holder read, one batched employee
    /// lookup across the module boundary, and one batched grade read — never one query per holder,
    /// which is the rule every read on this page already follows. <em>Labourer</em> has an approved
    /// headcount of 44 in the brief's own example.
    ///
    /// <b>An employee whose record will not resolve is still listed</b>, under their id. They hold
    /// the post; the count on the Positions list already counts them, and a list that quietly
    /// dropped them would disagree with it. That is the same judgement the designer card makes when
    /// a head's name will not resolve.
    /// </remarks>
    private async Task FillHoldersAsync(
        DimensionRecordDetailsViewModel model,
        string positionRecordId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var holders = await _positions.GetHoldersAsync(
            positionRecordId, date, cancellationToken: cancellationToken);

        if (holders.Items.Count == 0)
        {
            return;
        }

        var lookup = _employeeLookups.FirstOrDefault();

        var people = lookup is null
            ? new Dictionary<string, EmployeeRef>(StringComparer.Ordinal)
            : await lookup.GetManyAsync(
                [.. holders.Items.Select(holder => holder.EmployeeId)], cancellationToken);

        model.Holders =
        [
            .. holders.Items.Select(holder => new PositionHolderViewModel(
                holder.EmployeeId,
                people.TryGetValue(holder.EmployeeId, out var person)
                    ? person.ForCulture()
                    : holder.EmployeeId,
                holder.Grade is { } grade
                    ? BilingualDisplay.Name(grade.NameEn, grade.NameAr)
                    : string.Empty,
                holder.Since)),
        ];

        model.HoldersTotal = holders.Total;
    }

    /// <summary>One ancestor path, root first, as linked steps.</summary>
    private async Task<IReadOnlyList<DimensionRecordReportsToViewModel>> PathAboveAsync(
        string structureId,
        string recordId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        [
            .. ((await _graph.GetAncestorPathsAsync(
                structureId, [recordId], date, cancellationToken))
                .GetValueOrDefault(recordId) ?? [])
                .Select(above => new DimensionRecordReportsToViewModel(
                    above.RecordId, BilingualDisplay.Name(above.NameEn, above.NameAr))),
        ];

    /// <summary>
    /// Where a position sits in the business, and what to call the row that says so.
    /// </summary>
    /// <remarks>
    /// <b>The organisation axis, found by elimination.</b> A Position type is a level of its
    /// reporting line and of whatever chart a customer has allowed it on; the one that is not a
    /// reporting line is the one this row is about. Asked of each axis's shape rather than its code,
    /// for the reason <see cref="ReportingAxisAsync"/> gives.
    ///
    /// <b>The label is the parent's own dimension</b> — <em>Department</em> on Zenith — rather than
    /// a fixed caption. A structure decides what may contain a position, so another tenant's answer
    /// is Division or Section, and FRS Rule 6 lets any of them be renamed. A hard-coded
    /// "Department" would be right for the demo and wrong for a customer.
    /// </remarks>
    private async Task FillOrganisationPathAsync(
        DimensionRecordDetailsViewModel model,
        IReadOnlyList<StructureChoiceViewModel> structures,
        string recordId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        // The fallback caption, for a post that is placed nowhere and so has no parent dimension to
        // be named after. Deliberately not the bare word "Organisation": the list screen already
        // uses that msgid for the demo-company column and translates it المنظمة, and a second
        // context-less entry meaning "the org chart" is the duplicate-msgid collision that put the
        // wrong Arabic on the Organisation menu once already.
        model.OrganisationLabel = S["Organisation unit"].Value;

        foreach (var choice in structures)
        {
            if (await _positions.IsReportingLineAsync(choice.StructureId, cancellationToken))
            {
                continue;
            }

            var path = await PathAboveAsync(choice.StructureId, recordId, date, cancellationToken);

            if (path.Count == 0)
            {
                continue;
            }

            model.OrganisationPath = path;

            // The immediate parent is the last step, and its dimension is what the row is called.
            var parent = await _records.GetAsync(path[^1].RecordId, date, cancellationToken);

            if (parent is not null &&
                await _types.GetAsync(parent.DimensionTypeId, date, cancellationToken) is { } parentType)
            {
                model.OrganisationLabel = BilingualText.Display(parentType.Name.En, parentType.Name.Ar);
            }

            return;
        }
    }

    /// <summary>
    /// The reporting line among the charts a Position type is a level of, or null if it is on none.
    /// </summary>
    /// <remarks>
    /// Asked of each axis's <em>shape</em> rather than its code, which is what
    /// <see cref="IPositionService.IsReportingLineAsync"/> is for: the base recipe's axis is coded
    /// <c>reporting-line</c> and Zenith's is <c>zenith-reporting</c>, and a tenant may have renamed
    /// either. Null is a real answer — a tenant that has deleted its reporting line has positions
    /// with nothing to draw — and the page says so rather than falling back to the organisation
    /// chart, where every post is a leaf and the section would be silently empty.
    /// </remarks>
    private async Task<StructureChoiceViewModel?> ReportingAxisAsync(
        IReadOnlyList<StructureChoiceViewModel> structures,
        CancellationToken cancellationToken)
    {
        foreach (var choice in structures)
        {
            if (await _positions.IsReportingLineAsync(choice.StructureId, cancellationToken))
            {
                return choice;
            }
        }

        return null;
    }

    /// <summary>
    /// The positions that allow this grade, with the unit each sits in. Decision D26 C.
    /// </summary>
    /// <remarks>
    /// <b>Three reads, whatever the answer's size.</b> One indexed read of the references by target,
    /// one batched ancestor-path read for the positions' units, and whatever the reference read
    /// itself costs to hydrate — never one query per position, which is the rule every read on this
    /// page already follows.
    ///
    /// The unit comes from the <em>primary organisation</em> axis rather than from whichever chart
    /// the page happens to be scoped to: a grade is on the grades axis, so the page's own structure
    /// scope is the wrong one to ask, and "where does this position sit in the business" has one
    /// answer regardless of which screen is asking.
    /// </remarks>
    private async Task FillUsedByPositionsAsync(
        DimensionRecordDetailsViewModel model,
        string gradeRecordId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var positions = await _records.GetReferrersAsync(
            gradeRecordId, DimensionReferenceKinds.AllowedGrade, date, cancellationToken);

        if (positions.Count == 0)
        {
            return;
        }

        var organisation = await _structures.GetPrimaryOrganisationAsync(cancellationToken);

        var paths = organisation is null
            ? new Dictionary<string, IReadOnlyList<DimensionNodeRef>>(StringComparer.Ordinal)
            : await _graph.GetAncestorPathsAsync(
                organisation.StructureId,
                [.. positions.Select(position => position.RecordId)],
                date,
                cancellationToken);

        model.UsedByPositions =
        [
            .. positions.Select(position => new DimensionRecordReferrerViewModel(
                position.RecordId,
                position.Code,
                BilingualDisplay.Name(position.NameEn, position.NameAr),
                paths.TryGetValue(position.RecordId, out var ancestors) && ancestors.Count > 0
                    ? BilingualDisplay.Name(ancestors[^1].NameEn, ancestors[^1].NameAr)
                    : string.Empty)),
        ];
    }

    /// <summary>
    /// How many people are at this unit, how many are under it, and what the units under it are.
    /// Decision D24.
    /// </summary>
    /// <remarks>
    /// <b>A fixed handful of queries, however deep the subtree.</b> One batched direct headcount for
    /// every node at once, one closure read for the descendants, one batched ancestor-path read to
    /// learn each node's parent, and one read of the assignments under the root. Nothing is asked
    /// per node — which is the whole reason the closure index exists, and the rule the organisation
    /// designer's lazily-loaded children already follow.
    ///
    /// <b>"Including units below" is a distinct count, not a sum of the direct ones.</b> The
    /// assignment rows for the whole subtree are read once and rolled up as <em>sets</em> of
    /// employee ids, so one person assigned to two units below this one is counted once. Summing
    /// would have been one query cheaper and wrong in the way D1 names.
    ///
    /// <b>Two caps, and both are stated on screen rather than hidden.</b> The tree draws at most
    /// <see cref="TreeCap"/> units, because a tree cannot be paged — a node whose parent fell
    /// outside the slice would be drawn as a root, a wrong shape rather than a partial one. The
    /// roll-up reads at most <see cref="PeopleCap"/> assignment rows; past that the counts are
    /// marked as partial by the same truncation notice, because a headcount that is quietly short
    /// is worse than one that says it stopped counting.
    /// </remarks>
    private async Task FillUnitsBelowAsync(
        DimensionRecordDetailsViewModel model,
        string structureId,
        DimensionNodeRef record,
        DateOnly date,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        // Two different questions, not one question with a filter. Retiring a unit closes both its
        // record and its placement, so a retired department is not "placed here but ineffective" —
        // it is gone from today's subtree entirely. Showing it means asking the wider, undated
        // question instead. D24.
        var descendants = includeRetired
            ? await _graph.GetDescendantsEverAsync(structureId, record.RecordId, 0, TreeCap, cancellationToken)
            : await _graph.GetDescendantsAsync(structureId, record.RecordId, date, 0, TreeCap, cancellationToken);

        model.UnitsBelow = descendants.Total;
        model.TreeTruncated = descendants.Total > descendants.Items.Count;

        var shown = descendants.Items.ToList();

        var allIds = new List<string> { record.RecordId };
        allIds.AddRange(shown.Select(node => node.RecordId));

        var direct = await _assignments.CountEmployeesAtAsync(structureId, allIds, date, cancellationToken);

        model.EmployeeCount = direct.GetValueOrDefault(record.RecordId, 0);

        if (shown.Count == 0)
        {
            // A leaf. Nothing is below it, so "including units below" is the same number and the
            // section is absent rather than empty.
            model.EmployeeCountIncludingSubUnits = model.EmployeeCount;

            return;
        }

        // Each node's immediate parent, for the whole subtree in two queries. Chains come back
        // root-first, so the parent is the last entry; a node whose chain stops at the page's own
        // value is an immediate child of it.
        var paths = await _graph.GetAncestorPathsAsync(
            structureId, [.. shown.Select(node => node.RecordId)], date, cancellationToken);

        var present = shown.Select(node => node.RecordId).ToHashSet(StringComparer.Ordinal);

        var parentOf = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var node in shown)
        {
            var chain = paths.GetValueOrDefault(node.RecordId) ?? [];

            parentOf[node.RecordId] = chain.Count > 0 && present.Contains(chain[^1].RecordId)
                ? chain[^1].RecordId
                : null;
        }

        var people = await EmployeeSetsAsync(structureId, record.RecordId, date, cancellationToken);

        // Rolled up from the leaves: a node's set is its own people plus every child's set. Depth
        // descending guarantees a child is finished before its parent asks for it.
        var under = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var node in shown.OrderByDescending(node => node.Depth))
        {
            var set = under.TryGetValue(node.RecordId, out var own)
                ? own
                : under[node.RecordId] = new HashSet<string>(StringComparer.Ordinal);

            set.UnionWith(people.GetValueOrDefault(node.RecordId) ?? []);

            var parent = parentOf[node.RecordId] ?? record.RecordId;

            if (!under.TryGetValue(parent, out var above))
            {
                under[parent] = above = new HashSet<string>(StringComparer.Ordinal);
            }

            above.UnionWith(set);
        }

        if (!under.TryGetValue(record.RecordId, out var root))
        {
            under[record.RecordId] = root = new HashSet<string>(StringComparer.Ordinal);
        }

        root.UnionWith(people.GetValueOrDefault(record.RecordId) ?? []);

        model.EmployeeCountIncludingSubUnits = root.Count;

        var children = shown
            .GroupBy(node => parentOf[node.RecordId] ?? record.RecordId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var typeNames = (await _types.ListAsync(includeRetired: true, cancellationToken: cancellationToken))
            .ToDictionary(
                type => type.DimensionTypeId,
                type => BilingualText.Display(type.Name.En, type.Name.Ar),
                StringComparer.Ordinal);

        var names = shown.ToDictionary(
            node => node.RecordId,
            node => BilingualText.Display(node.NameEn, node.NameAr),
            StringComparer.Ordinal);

        var ownName = BilingualText.Display(record.NameEn, record.NameAr);

        model.Tree = Arrange(shown, parentOf, record.RecordId)
            .Select(row => new DimensionRecordTreeRowViewModel
            {
                RecordId = row.Node.RecordId,
                Code = row.Node.Code,
                NameEn = row.Node.NameEn,
                NameAr = row.Node.NameAr,
                TypeName = typeNames.GetValueOrDefault(row.Node.DimensionTypeId, string.Empty),
                Depth = row.Depth,
                ParentRecordId = parentOf[row.Node.RecordId],
                // The page's own value for an immediate child: that is what it sits under, and the
                // list view has no indentation to say so with.
                ParentName = parentOf[row.Node.RecordId] is { } parentId
                    ? names.GetValueOrDefault(parentId, string.Empty)
                    : ownName,
                ChildCount = children.GetValueOrDefault(row.Node.RecordId, 0),
                EmployeeCount = direct.GetValueOrDefault(row.Node.RecordId, 0),
                EmployeeCountIncludingSubUnits = under.GetValueOrDefault(row.Node.RecordId)?.Count ?? 0,
                IsEffective = row.Node.EffectiveRange.Contains(date),
            })
            .ToList();

        model.Hierarchy = Nest(model.Tree, record, model.EmployeeCount ?? 0, root.Count);

        // On a reporting line, the boxes carry filled/approved instead of a headcount (D26 E). An
        // assignment names the organisation axis, so every employee number on this chart is zero —
        // a badge reading 0 on every box would be worse than no badge, and the number a reader of a
        // reporting chart actually wants is how full each post is.
        if (model.Hierarchy is { } chart &&
            await _positions.IsReportingLineAsync(structureId, cancellationToken))
        {
            await FillEstablishmentAsync(model, chart, date, cancellationToken);
        }
    }

    /// <summary>
    /// Puts the approved and filled figures on a reporting chart — on all three views. Decision
    /// D26 E.
    /// </summary>
    /// <remarks>
    /// One batched read for the whole chart, walked iteratively — the depth of a customer's
    /// reporting line is their business, and a stack overflow on a deep one would be a crash rather
    /// than a message. The same rule <c>Nest</c> follows for the same reason.
    ///
    /// <b>The flat rows are filled from the same dictionary as the boxes.</b> The Tree and List
    /// views show a <em>Holders</em> column where an organisation chart shows employees, and a
    /// second read for it would be a second answer that could disagree with the chart's — the drift
    /// <c>Nest</c> exists to prevent, arriving by another route.
    /// </remarks>
    private async Task FillEstablishmentAsync(
        DimensionRecordDetailsViewModel model,
        DimensionRecordHierarchyNodeViewModel root,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var boxes = new List<DimensionRecordHierarchyNodeViewModel>();
        var pending = new Stack<DimensionRecordHierarchyNodeViewModel>([root]);

        while (pending.Count > 0)
        {
            var box = pending.Pop();

            boxes.Add(box);

            foreach (var child in box.Children)
            {
                pending.Push(child);
            }
        }

        var ids = boxes.Select(box => box.RecordId).ToList();

        var establishment = await _positions.GetEstablishmentAsync(ids, date, cancellationToken);

        // And where each post sits in the business, which is a question about the OTHER axis — on
        // a reporting chart every box's dimension reads "Position", so the unit is the only thing
        // telling two posts called Supervisor apart. ADR-0028 showing up on a screen.
        var units = await _positions.GetUnitsAsync(ids, date, cancellationToken);

        foreach (var box in boxes)
        {
            box.Establishment = establishment.GetValueOrDefault(box.RecordId);

            box.UnitName = units.TryGetValue(box.RecordId, out var unit)
                ? BilingualDisplay.Name(unit.NameEn, unit.NameAr)
                : string.Empty;
        }

        // And the same figures on the flat rows the Tree and List views draw, from the dictionary
        // that is already in hand.
        foreach (var row in model.Tree)
        {
            row.Establishment = establishment.GetValueOrDefault(row.RecordId);
        }
    }

    /// <summary>
    /// The same rows again, nested, with the page's own value as the root box. Decision D25.
    /// </summary>
    /// <remarks>
    /// <b>Built from <see cref="DimensionRecordDetailsViewModel.Tree"/> rather than from the
    /// subtree a second time.</b> The chart and the tree are two renderings of one answer, so they
    /// are shaped from one list — which is what stops them disagreeing about what is under what,
    /// the drift the designer's own two views were built to avoid.
    ///
    /// Iterative, like everything else that walks a customer's tree here: the depth of their
    /// organisation is their business, and a stack overflow on an imported cycle would be a crash
    /// rather than a message.
    /// </remarks>
    private static DimensionRecordHierarchyNodeViewModel Nest(
        IReadOnlyList<DimensionRecordTreeRowViewModel> rows,
        DimensionNodeRef record,
        int ownEmployees,
        int employeesBelow)
    {
        var nodes = rows.ToDictionary(
            row => row.RecordId,
            row => new DimensionRecordHierarchyNodeViewModel
            {
                RecordId = row.RecordId,
                Code = row.Code,
                NameEn = row.NameEn,
                NameAr = row.NameAr,
                TypeName = row.TypeName,
                EmployeeCount = row.EmployeeCount,
                EmployeeCountIncludingSubUnits = row.EmployeeCountIncludingSubUnits,
                IsEffective = row.IsEffective,
            },
            StringComparer.Ordinal);

        var root = new DimensionRecordHierarchyNodeViewModel
        {
            RecordId = record.RecordId,
            Code = record.Code,
            NameEn = record.NameEn,
            NameAr = record.NameAr,
            EmployeeCount = ownEmployees,
            EmployeeCountIncludingSubUnits = employeesBelow,
            IsEffective = true,
            IsRoot = true,
        };

        var childrenOf = new Dictionary<string, List<DimensionRecordHierarchyNodeViewModel>>(StringComparer.Ordinal);

        // Rows arrive in tree order — each node before the nodes under it — so a parent's list
        // exists by the time its children are read, and the children keep the order they were
        // arranged in.
        foreach (var row in rows)
        {
            var parent = row.ParentRecordId ?? record.RecordId;

            if (!childrenOf.TryGetValue(parent, out var list))
            {
                childrenOf[parent] = list = [];
            }

            list.Add(nodes[row.RecordId]);
        }

        foreach (var (id, list) in childrenOf)
        {
            if (string.Equals(id, record.RecordId, StringComparison.Ordinal))
            {
                root.Children = list;
            }
            else if (nodes.TryGetValue(id, out var node))
            {
                node.Children = list;
            }
        }

        return root;
    }

    /// <summary>
    /// The list view's rows, in the order the reader asked for. Decision D25.
    /// </summary>
    /// <remarks>
    /// <b>Sorted on the server, from the query string</b>, so a sorted list is a URL somebody can
    /// send and the column headings work with no JavaScript — the same choice every other list in
    /// this module makes. An unrecognised column leaves the rows in <b>tree order</b>, which is
    /// what the other two views show and therefore what a reader arriving from one of them expects,
    /// rather than an arbitrary alphabetisation nobody asked for.
    ///
    /// Names sort culture-aware and case-insensitive, because they are words a person reads; codes
    /// sort ordinally, because they are identifiers. Ties break on the code, so the order is total
    /// and a reader paging back and forth sees the same list.
    /// </remarks>
    internal static IReadOnlyList<DimensionRecordTreeRowViewModel> SortedForList(
        IReadOnlyList<DimensionRecordTreeRowViewModel> rows,
        string? sort,
        bool descending)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true);

        IOrderedEnumerable<DimensionRecordTreeRowViewModel> By<TKey>(
            Func<DimensionRecordTreeRowViewModel, TKey> key, IComparer<TKey>? comparer = null) =>
            descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

        IOrderedEnumerable<DimensionRecordTreeRowViewModel>? ordered =
            (sort ?? string.Empty).ToLowerInvariant() switch
            {
                "code" => By(row => row.Code, StringComparer.Ordinal),
                "name" => By(row => row.Name, byName),
                "dimension" => By(row => row.TypeName, byName),
                "parent" => By(row => row.ParentName, byName),
                "status" => By(row => row.IsEffective),
                "employees" => By(row => row.EmployeeCountIncludingSubUnits),
                _ => null,
            };

        return ordered is null
            ? rows
            : [.. ordered.ThenBy(row => row.Code, StringComparer.Ordinal)];
    }

    /// <summary>
    /// How many assignment rows the subtree roll-up will read before it stops counting.
    /// </summary>
    /// <remarks>
    /// Five thousand is the scale specification section 7 names for a tenant's records, and a
    /// subtree's people are bounded by that in any organisation anybody draws. Past it the page
    /// says the tree was truncated rather than printing a number that is quietly short.
    /// </remarks>
    internal const int PeopleCap = 5000;

    /// <summary>The distinct employees at each unit in the subtree, keyed by unit.</summary>
    /// <remarks>
    /// One read of the whole subtree's assignments through the closure index's correlated
    /// sub-select — not an id list, which would exceed SQL Server's parameter limit at five
    /// thousand records, and not a query per node.
    /// </remarks>
    private async Task<Dictionary<string, HashSet<string>>> EmployeeSetsAsync(
        string structureId,
        string recordId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var assignments = await _assignments.GetEmployeesUnderAsync(
            structureId, recordId, date, includeDescendants: true, 0, PeopleCap, cancellationToken);

        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var assignment in assignments.Items)
        {
            if (!sets.TryGetValue(assignment.RecordId, out var set))
            {
                sets[assignment.RecordId] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(assignment.EmployeeId);
        }

        return sets;
    }

    /// <summary>
    /// The subtree in reading order: each node immediately before the nodes under it.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive, for the reason the list screen's tree builder is: the depth
    /// of a customer's organisation is their business, and a stack overflow on an imported cycle
    /// would be a crash rather than a message. Cycles cannot reach here — the engine refuses them
    /// across every date — but a screen is the wrong place to rely on that.
    /// </remarks>
    /// <remarks>
    /// <b>The depth is the one this tree draws, not the one the closure index reported.</b> They
    /// differ when "Include retired" is off and a retired unit sits between two live ones: the
    /// grandchild is then drawn directly under the page's value, and indenting it by its closure
    /// depth would leave a gap under a parent that is not on screen.
    /// </remarks>
    private static List<(DimensionNodeRef Node, int Depth)> Arrange(
        List<DimensionNodeRef> nodes,
        Dictionary<string, string?> parentOf,
        string rootId)
    {
        var childrenOf = nodes
            .GroupBy(node => parentOf[node.RecordId] ?? rootId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => node.SortOrder)
                    .ThenBy(node => node.NameEn, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        var ordered = new List<(DimensionNodeRef Node, int Depth)>(nodes.Count);
        var pending = new Stack<(DimensionNodeRef Node, int Depth)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in Enumerable.Reverse(childrenOf.GetValueOrDefault(rootId) ?? []))
        {
            pending.Push((root, 1));
        }

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();

            if (!seen.Add(node.RecordId))
            {
                continue;
            }

            ordered.Add((node, depth));

            foreach (var child in Enumerable.Reverse(childrenOf.GetValueOrDefault(node.RecordId) ?? []))
            {
                pending.Push((child, depth + 1));
            }
        }

        return ordered;
    }

    /// <summary>
    /// The head's name, asked of the module that owns employees, or null on a tenant without it.
    /// </summary>
    /// <remarks>
    /// Through <see cref="IEmployeeLookup"/> rather than a reference to <c>WorkMate.Records</c>,
    /// which this module must not have — the same seam the designer card resolves its head name
    /// through. A tenant with the employee module disabled resolves nothing, and the page shows the
    /// appointment's employee id rather than a blank, which is at least something to act on.
    /// </remarks>
    private async Task<EmployeeRef?> ResolveEmployeeAsync(string employeeId, CancellationToken cancellationToken)
    {
        var lookup = _employeeLookups.FirstOrDefault();

        return lookup is null ? null : await lookup.GetAsync(employeeId, cancellationToken);
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

        // D31: a position must say what it reports to, and "nobody" is one of the answers. Asked
        // BEFORE the record is written, because a position created and then refused its reporting
        // line is exactly the half-finished state AddUnitAsync's own remarks exist to avoid.
        //
        // The rule is the validator's, not this screen's, so the recipe and any later import refuse
        // it in the same words — CLAUDE.md rule 5.
        if (model.IsPosition)
        {
            var reportsTo = await _validator.ValidateReportsToAsync(
                model.ReportingStructureId,
                ReportsToChoice.IntentOf(model.ReportsToRecordId),
                model.ReportsToRecordId,
                effectiveFrom,
                cancellationToken);

            if (reportsTo.Any(error => !error.IsAdvisory))
            {
                AddErrors(reportsTo);

                return View(model);
            }

            // A second post at the top of the line is advisory: unusual, worth naming, and an
            // organisation entitled to have two. Carried past the write and raised with the
            // success, so it reads as a remark rather than a failure.
            foreach (var advisory in reportsTo)
            {
                await _notifier.WarningAsync(new LocalizedHtmlString(
                    advisory.Message.Name, advisory.Message.Value));
            }
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
            model.Alias,
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

        // A position's second axis and its grades, both after the record exists because both name
        // it. Neither is part of AddUnitAsync's all-or-nothing write, and that is the honest shape:
        // a reporting line and a set of grades are facts about a post that already exists, where its
        // place in the business is the act of creating it. A failure in either is reported and
        // leaves the position created and placed, which is a state the screens can show and a reader
        // can fix — unlike a half-created record, which is why AddUnitAsync is one call.
        if (model.IsPosition)
        {
            await ApplyPositionFactsAsync(
                result.Value!.RecordId,
                model.ReportsToRecordId,
                model.AllowedGradeRecordIds,
                model.ReportingStructureId,
                effectiveFrom,
                cancellationToken);
        }

        await _notifier.SuccessAsync(H["{0} '{1}' was created.", model.TypeName, result.Value!.NameEn]);

        return RedirectToAction(nameof(Index), new
        {
            dimensionTypeId = model.DimensionTypeId,
            structureId = model.StructureId,
        });
    }

    /// <summary>
    /// Puts a new position on the reporting line and gives it its grades. Decisions D26 C and
    /// ADR-0028.
    /// </summary>
    /// <remarks>
    /// <b>Both are refusable and neither is fatal.</b> A reporting line that would close a loop is
    /// refused by the graph's existing cycle check — the same one that protects the organisation
    /// chart, running on a second axis and needing no new code — and a grade that is not a grade is
    /// refused by <c>IDimensionValidator.ValidateReferencesAsync</c>. Each failure is shown as a
    /// warning against the position that was created, rather than rolled back: the post exists and
    /// is placed, and telling somebody their position was not created would be untrue.
    /// </remarks>
    private async Task ApplyPositionFactsAsync(
        string recordId,
        string? reportsToRecordId,
        IReadOnlyList<string> allowedGradeRecordIds,
        string? reportingStructureId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken)
    {
        // THE POSITION GOES ON THE REPORTING LINE EITHER WAY, which is the D31 fix. "Nobody" is a
        // placement row with a null parent — exactly what the CEO has in the Zenith recipe — and
        // not the absence of a row. Before this, choosing "reports to nobody" wrote nothing at all:
        // the post was on that axis nowhere, so the reporting chart was quietly short a box and
        // every position beneath it answered "top of the line".
        var intent = ReportsToChoice.IntentOf(reportsToRecordId);

        if (!string.IsNullOrEmpty(reportingStructureId) && intent != ReportsToIntent.Unstated)
        {
            var placed = await _graph.PlaceAsync(
                reportingStructureId,
                recordId,
                intent == ReportsToIntent.Top ? null : reportsToRecordId,
                effectiveFrom,
                cancellationToken);

            if (!placed.Succeeded)
            {
                await _notifier.WarningAsync(H["The position was created, but its reporting line was not set: {0}",
                    string.Join(" ", placed.Errors.Select(error => error.Message.Value))]);
            }
        }

        var grades = allowedGradeRecordIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (grades.Count == 0)
        {
            return;
        }

        var set = await _records.SetReferencesAsync(
            recordId, DimensionReferenceKinds.AllowedGrade, grades, cancellationToken);

        if (!set.Succeeded)
        {
            await _notifier.WarningAsync(H["The position was created, but its grades were not set: {0}",
                string.Join(" ", set.Errors.Select(error => error.Message.Value))]);
        }
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
        model.IsPosition = type.StandardRole == DimensionTypeRole.Position;

        var all = await StructuresForAsync(type, cancellationToken);

        // The reporting axis THIS TYPE is on, not "the one called reporting-line": the demo
        // companies have their own, for the reason their types are prefixed, and a form that
        // reached for the base recipe's would try to place a Zenith position on an axis Zenith's
        // Position type is not a level of. ADR-0028; IsReportingLineAsync asks the axis's shape.
        if (model.IsPosition)
        {
            foreach (var choice in all)
            {
                if (await _positions.IsReportingLineAsync(choice.StructureId, cancellationToken))
                {
                    model.ReportingStructureId = choice.StructureId;

                    break;
                }
            }
        }

        // A position is on two axes (ADR-0028) and only one of them is a question this form asks.
        // "Place under" is the organisation chart; the reporting line has a control of its own, so
        // offering it in the structure chooser would be asking the same reader the same question
        // twice and letting them answer it in the wrong box.
        model.Structures = model.ReportingStructureId is null
            ? all
            : [.. all.Where(choice => !string.Equals(
                choice.StructureId, model.ReportingStructureId, StringComparison.Ordinal))];

        if (model.IsPosition)
        {
            var readiness = await _positions.GetPlacementReadinessAsync(
                type.DimensionTypeId, asAt: null, cancellationToken);

            model.PositionsCanBePlaced = readiness.HasSomewhereToGo;
        }

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

    /// <param name="dimensionTypeId">
    /// Carried only so that an Edit pressed with no row ticked can get back to the list it came
    /// from. The record's own type is read from the record everywhere else.
    /// </param>
    public async Task<IActionResult> Edit(
        string? id,
        string? structureId,
        string? dimensionTypeId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        // The toolbar's Edit acts on the row the reader ticked, and a form can be submitted with
        // nothing ticked. A sentence beats a 404 on an empty id.
        if (string.IsNullOrEmpty(id))
        {
            await _notifier.WarningAsync(H["Choose a unit first, then press Edit."]);

            return RedirectToAction(nameof(Index), new { dimensionTypeId, structureId });
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
            AliasEn = record.AliasEn,
            AliasAr = record.AliasAr,
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

        // Undated, and only when it changed. The alias has no history and no two kinds of rename,
        // so unlike the name above it there is no date to apply it from and no question to ask.
        if (model.AliasChanged)
        {
            var aliased = await _records.SetAliasAsync(record.RecordId, model.Alias, cancellationToken);

            if (!aliased.IsAuthorised)
            {
                return Forbid();
            }

            if (!aliased.Succeeded)
            {
                AddErrors(aliased.Errors);

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

        // The position's grades, undated and only when the posted set differs from the stored one —
        // the same rule the alias above follows, and for the same reason: a form posts every field
        // it shows whether or not anybody touched it, and a write that happened anyway would leave
        // an audit entry saying somebody re-graded a post on a form they opened to fix a name.
        if (model.IsPosition && model.AllowedGradesChanged)
        {
            var grades = model.AllowedGradeRecordIds
                .Where(gradeId => !string.IsNullOrEmpty(gradeId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var set = await _records.SetReferencesAsync(
                record.RecordId, DimensionReferenceKinds.AllowedGrade, grades, cancellationToken);

            if (!set.IsAuthorised)
            {
                return Forbid();
            }

            if (!set.Succeeded)
            {
                AddErrors(set.Errors);

                return View(model);
            }

            // D26 G: removing a grade an employee holds is permitted and never breaks their record,
            // so this warns rather than refusing. Their own dated grade is untouched; what changes
            // is what is offered next time.
            var removed = model.CurrentAllowedGradeRecordIds
                .Except(grades, StringComparer.Ordinal)
                .ToList();

            if (removed.Count > 0)
            {
                await _notifier.WarningAsync(H.Plural(
                    removed.Count,
                    "{0} grade was removed from this position. Anybody already on it keeps it; it is no longer offered.",
                    "{0} grades were removed from this position. Anybody already on them keeps them; they are no longer offered.",
                    removed.Count));
            }
        }

        await _notifier.SuccessAsync(H["'{0}' was updated.", model.Name.En]);

        return RedirectToAction(nameof(Index), new
        {
            dimensionTypeId = model.DimensionTypeId,
            structureId = model.StructureId,
        });
    }

    /// <summary>
    /// Where a position sits on the reporting line today, for the Edit form to show. Decision D31.
    /// </summary>
    /// <remarks>
    /// Read rather than offered. Changing it is a reparenting on a second axis — a dated move with
    /// its own permission and dry run — and the Edit form has neither a date for it nor
    /// <c>MoveDimensionRecords</c>; the designer, opened on the reporting line, is where that move
    /// already lives.
    ///
    /// Asked across every reporting axis the tenant has, not just <c>base</c>'s, for the reason
    /// <see cref="IPositionService.GetReportingAxesAsync"/> gives: the demo tenant has Zenith's as
    /// well, and a read that asked only the shipped one answered "top of the line" for every post
    /// in the company.
    /// </remarks>
    private async Task FillReportsToAsync(
        DimensionRecordEditViewModel model,
        string recordId,
        CancellationToken cancellationToken)
    {
        var today = await _authorisation.TodayAsync();

        foreach (var axis in await _positions.GetReportingAxesAsync(cancellationToken))
        {
            model.ReportingStructureId ??= axis.StructureId;

            var ancestors = await _graph.GetAncestorsAsync(
                axis.StructureId, recordId, today, cancellationToken);

            // Ancestors come root-first, so the post directly above is the last of them. An empty
            // list is ambiguous on its own — it means both "top of this line" and "not on this
            // line" — so membership is settled separately, below.
            if (ancestors.Count > 0)
            {
                model.IsOnTheReportingLine = true;
                model.ReportsToName = BilingualDisplay.Name(
                    ancestors[^1].NameEn, ancestors[^1].NameAr);
                model.ReportingStructureId = axis.StructureId;

                return;
            }

            // No ancestors is ambiguous on its own — it means both "the top of this line" and "not
            // on this line at all" — and the whole point of showing this is to tell those apart.
            // The roots of the axis settle it: a top-of-line post is one of them, and a post that
            // was never placed is nowhere.
            //
            // GetAncestorPathsAsync looked like the natural read and is not: it answers for every
            // id it is given, so an absent record came back as a root and every unplaced post read
            // as the top of the line.
            var roots = await _graph.GetRootsAsync(axis.StructureId, today, cancellationToken);

            if (roots.Any(root => string.Equals(root.RecordId, recordId, StringComparison.Ordinal)))
            {
                model.IsOnTheReportingLine = true;
                model.ReportsToName = null;
                model.ReportingStructureId = axis.StructureId;

                return;
            }
        }
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
        model.CurrentAliasEn = record.AliasEn;
        model.CurrentAliasAr = record.AliasAr;
        model.IsPosition = type.StandardRole == DimensionTypeRole.Position;

        if (model.IsPosition)
        {
            // Undated on purpose. The form is editing the set the position has <em>now</em>, and a
            // reference has no effective range — so the grades it shows are the ones effective
            // today, which is the only set anybody can sensibly be editing.
            var allowed = await _records.GetReferencesAsync(
                record.RecordId, DimensionReferenceKinds.AllowedGrade,
                asAt: null, cancellationToken);

            model.CurrentAllowedGradeRecordIds = [.. allowed.Select(grade => grade.RecordId)];

            await FillReportsToAsync(model, record.RecordId, cancellationToken);

            // A freshly opened form shows what is stored; a post shows what was posted, including
            // the empty set. The two are told apart by the hidden empty value the Edit form renders
            // beside the control: a multi-select with nothing chosen posts nothing at all, so
            // without it "clear every grade" and "this form never asked" are the same request.
            if (model.AllowedGradeRecordIds.Count == 0)
            {
                model.AllowedGradeRecordIds = [.. model.CurrentAllowedGradeRecordIds];
            }
        }

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

    // ---- deleting one ------------------------------------------------------------------

    /// <summary>
    /// The toolbar's <b>Delete</b>, FRS v2.5 §4.1 — which for now takes the reader to the dated
    /// retirement screen and says so on the way.
    /// </summary>
    /// <remarks>
    /// <b>This is not D4 yet, and it does not pretend to be.</b> Decision D4 and ADR-0025 give
    /// Delete two outcomes: a value nothing has ever used is really deleted, and one that is or was
    /// used is made inactive with an end date and the reader is told why. The second half already
    /// exists in full — <c>PlanRetireAsync</c> names what is still attached on the day the unit
    /// would close, and <c>RetireAsync</c> will not commit until somebody has said what happens to
    /// the units underneath — and the first half does not, because a real delete is the one
    /// operation in this module that destroys a record and it needs its own audit event, its own
    /// confirmation and its own second review. That lands in slice F9.
    ///
    /// So the control the FRS asks for is on the screen, it is reachable, and what it does is
    /// stated before it happens rather than discovered afterwards. The alternative — holding the
    /// button back until F9 — would leave the toolbar four buttons for four slices, and the
    /// alternative to <em>that</em>, labelling it "Retire", would quietly answer a question the
    /// product owner has already answered the other way.
    ///
    /// <b>Why it is a GET that redirects rather than a screen of its own.</b> A screen here would be
    /// a second place that explains retirement, and F9 would then have to delete it. One sentence
    /// and a redirect leaves exactly one screen that explains it, and leaves this action as the
    /// place F9's real behaviour goes.
    /// </remarks>
    public async Task<IActionResult> Delete(
        string? id,
        string? dimensionTypeId,
        string? structureId,
        string? asAt)
    {
        if (!await CanEditAsync())
        {
            return Forbid();
        }

        // The toolbar's buttons act on the row the reader ticked, and a form can be submitted with
        // nothing ticked. Saying so beats a 404 on an empty id.
        if (string.IsNullOrEmpty(id))
        {
            await _notifier.WarningAsync(H["Choose a unit first, then press Delete."]);

            return RedirectToAction(nameof(Index), new { dimensionTypeId, structureId, asAt });
        }

        if (string.IsNullOrEmpty(structureId))
        {
            // Retirement closes the record on every structure at once, and the screen that does it
            // needs one to show the branch from. A type on no structure cannot be retired from here.
            await _notifier.WarningAsync(H["Choose a structure before deleting a unit."]);

            return RedirectToAction(nameof(Index), new { dimensionTypeId, structureId, asAt });
        }

        // One string literal, not a concatenation. The localisation gate scans the source for
        // localiser calls and reads the literal that follows, so a sentence split across `+` is
        // extracted as its first fragment and the rest is never translated.
        await _notifier.InformationAsync(H["Deleting a unit that is in use is not available yet. This screen ends it on a date instead: it stops being offered, and every report for a period before that date still resolves it."]);

        return RedirectToAction(
            nameof(OrganisationDesignerAdminController.Retire),
            "OrganisationDesignerAdmin",
            new { structureId, recordId = id, asAt });
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
    private Task<IReadOnlyList<StructureChoiceViewModel>> StructuresForAsync(
        DimensionTypeDocument type,
        CancellationToken cancellationToken) =>
        StructuresForAsync([type], cancellationToken);

    /// <inheritdoc cref="StructuresForAsync(DimensionTypeDocument, CancellationToken)"/>
    /// <remarks>
    /// The union across a group's types, for the <em>All Departments</em> screen: a chart that any
    /// of them is a level of is a chart this screen can report on. Distinct, because two types in a
    /// group can perfectly well be levels of the same chart.
    /// </remarks>
    private async Task<IReadOnlyList<StructureChoiceViewModel>> StructuresForAsync(
        IReadOnlyList<DimensionTypeDocument> types,
        CancellationToken cancellationToken) =>
        [
            .. (await _structures.ListAsync(cancellationToken))
                .Where(structure => types.Any(type => structure.OrdinalOf(type.DimensionTypeId) is not null))
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
