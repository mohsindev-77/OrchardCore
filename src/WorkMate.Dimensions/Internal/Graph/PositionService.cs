using System.Globalization;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Dimensions.Internal.Graph;

/// <inheritdoc />
/// <remarks>
/// <b>A composition of existing batched reads, and deliberately nothing more.</b> Every query here
/// is one the module already had: <c>SearchRecordsOfTypesAsync</c> for the options,
/// <c>GetAncestorPathsAsync</c> for the units, <c>CountEmployeesAtAsync</c> for the filled figures,
/// <c>GetAttributeValueSetsAsync</c> for the approved ones and <c>GetReferenceSetsAsync</c> for the
/// grades. What this adds is that there is one place they are put together, so the Positions list,
/// the two pickers and F6's employee form cannot compose them three slightly different ways.
///
/// Internal, with only the interface public, like every other service in this module.
/// </remarks>
internal sealed class PositionService : IPositionService
{
    /// <summary>
    /// The code of the axis a position's reporting line is on. ADR-0028;
    /// <c>base.recipe.json</c> ships it.
    /// </summary>
    public const string ReportingLineStructureCode = "reporting-line";

    private readonly IDimensionTypeService _types;
    private readonly IStructureService _structures;
    private readonly IDimensionService _records;
    private readonly IDimensionGraphService _graph;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionAuthorisation _authorisation;

    public PositionService(
        IDimensionTypeService types,
        IStructureService structures,
        IDimensionService records,
        IDimensionGraphService graph,
        IEmployeeAssignmentService assignments,
        IDimensionAuthorisation authorisation)
    {
        _types = types;
        _structures = structures;
        _records = records;
        _graph = graph;
        _assignments = assignments;
        _authorisation = authorisation;
    }

    public async Task<IReadOnlyList<DimensionTypeDocument>> GetTypesInRoleAsync(
        DimensionTypeRole role,
        CancellationToken cancellationToken = default)
    {
        var types = await _types.ListAsync(cancellationToken: cancellationToken);

        return
        [
            .. types
                .Where(type => type.StandardRole == role && type.RetiredOn is null)
                .OrderBy(type => type.MenuOrder)
                .ThenBy(type => type.Code, StringComparer.Ordinal),
        ];
    }

    public Task<StructureDocument?> GetReportingLineAsync(CancellationToken cancellationToken = default) =>
        _structures.GetByCodeAsync(ReportingLineStructureCode, cancellationToken);

    public async Task<Page<PositionOption>> SearchSelectableAsync(
        string? searchText,
        DateOnly? asAt = null,
        int take = 20,
        string? nearUnitRecordId = null,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var positionTypes = await GetTypesInRoleAsync(DimensionTypeRole.Position, cancellationToken);

        if (positionTypes.Count == 0)
        {
            return new Page<PositionOption>([], 0, 0, take);
        }

        var typeIds = positionTypes.Select(type => type.DimensionTypeId).ToArray();

        // Active only (D26 F). A position that has closed is not a post anybody can be put into, and
        // offering it greyed out is offering it.
        var page = await _graph.SearchRecordsOfTypesAsync(
            typeIds,
            searchText,
            structureId: null,
            withinRecordId: null,
            DimensionRecordStatus.Active,
            date,
            skip: 0,
            take,
            cancellationToken);

        var positions = page.Items.ToList();

        // SLICE F6c: THE POSTS IN THE SAME DEPARTMENT FIRST. A reader choosing what a new post
        // reports to is almost always looking inside the department they are building it in, and
        // the first page is alphabetical across the whole company — so the obvious answer was
        // routinely not on it.
        //
        // Fetched rather than re-sorted. Re-ordering the page would only move the same-department
        // posts that happened to land in the first twenty, which is the half of the problem that
        // did not need solving. The total stays the unscoped one, because "how many posts are
        // there" is still the question the caption answers.
        // "Within this unit" is a question about one axis — the closure index is per structure —
        // so the axis the unit is actually on has to be found first. Asking with no structure
        // returns the unscoped list, which is how this first shipped and why its test caught it.
        var nearAxis = string.IsNullOrEmpty(nearUnitRecordId)
            ? null
            : await AxisOfAsync(nearUnitRecordId, date, cancellationToken);

        if (!string.IsNullOrEmpty(nearUnitRecordId) && nearAxis is not null)
        {
            var near = await _graph.SearchRecordsOfTypesAsync(
                typeIds,
                searchText,
                nearAxis,
                withinRecordId: nearUnitRecordId,
                DimensionRecordStatus.Active,
                date,
                skip: 0,
                take,
                cancellationToken);

            var alreadyNear = near.Items.Select(item => item.RecordId).ToHashSet(StringComparer.Ordinal);

            positions =
            [
                .. near.Items,
                .. positions.Where(position => !alreadyNear.Contains(position.RecordId)),
            ];

            positions = [.. positions.Take(take)];
        }

        var ids = positions.Select(position => position.RecordId).ToList();

        var units = await UnitPathsAsync(positions, date, cancellationToken);
        var establishment = await GetEstablishmentAsync(ids, date, cancellationToken);

        return new Page<PositionOption>(
            [
                .. positions.Select(position => new PositionOption(
                    position,
                    units.GetValueOrDefault(position.RecordId, string.Empty),
                    establishment.GetValueOrDefault(position.RecordId, PositionEstablishment.Unfilled))),
            ],
            page.Total,
            page.Skip,
            page.Take);
    }

    /// <summary>
    /// The structure <paramref name="recordId"/> is placed on, or null if it is on none. F6c.
    /// </summary>
    /// <remarks>
    /// <b>Why this is needed at all.</b> The closure index is per structure, so "the positions
    /// within this unit" cannot be asked without naming the axis — a search with no structure and
    /// a <c>withinRecordId</c> quietly returns the unscoped list, which is exactly the shape of
    /// bug that looks like a working feature.
    ///
    /// Organisation axes only, in their fixed order, so the same unit always resolves to the same
    /// axis. A unit placed on two would otherwise order the picker differently on different
    /// requests for no reason anybody could explain.
    /// </remarks>
    private async Task<string?> AxisOfAsync(
        string recordId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        foreach (var axis in await OrganisationAxesAsync(cancellationToken))
        {
            var depths = await _graph.GetDepthsAsync(
                axis.StructureId, [recordId], asAt, cancellationToken);

            if (depths.ContainsKey(recordId))
            {
                return axis.StructureId;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<DimensionNodeRef>> GetAllowedGradesAsync(
        string positionRecordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default) =>
        await _records.GetReferencesAsync(
            positionRecordId, DimensionReferenceKinds.AllowedGrade, asAt, cancellationToken);

    public async Task<Page<DimensionNodeRef>> SearchGradesAsync(
        string? searchText,
        DateOnly? asAt = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var gradeTypes = await GetTypesInRoleAsync(DimensionTypeRole.Grade, cancellationToken);

        if (gradeTypes.Count == 0)
        {
            return new Page<DimensionNodeRef>([], 0, 0, take);
        }

        return await _graph.SearchRecordsOfTypesAsync(
            [.. gradeTypes.Select(type => type.DimensionTypeId)],
            searchText,
            structureId: null,
            withinRecordId: null,
            DimensionRecordStatus.Active,
            date,
            skip: 0,
            take,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, PositionEstablishment>> GetEstablishmentAsync(
        IReadOnlyList<string> positionRecordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionRecordIds);

        var establishment = new Dictionary<string, PositionEstablishment>(StringComparer.Ordinal);

        if (positionRecordIds.Count == 0)
        {
            return establishment;
        }

        var date = asAt ?? await _authorisation.TodayAsync();
        var ids = positionRecordIds.Distinct(StringComparer.Ordinal).ToList();

        // One content-item read for every approved figure. Undated, like every attribute (D15).
        var attributes = await _records.GetAttributeValueSetsAsync(ids, cancellationToken);

        // And one headcount read per organisation axis positions live on — one on every real
        // tenant, and one per company on the demo one. Never one per position.
        var filled = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var axis in await OrganisationAxesAsync(cancellationToken))
        {
            var counts = await _assignments.CountEmployeesAtAsync(
                axis.StructureId, ids, date, cancellationToken);

            foreach (var (recordId, count) in counts)
            {
                // A position is placed on exactly one organisation axis, so these sets do not
                // overlap and the larger of two readings is the real one rather than a sum. Summing
                // would double count a record that somehow appeared on two.
                filled[recordId] = Math.Max(filled.GetValueOrDefault(recordId), count);
            }
        }

        foreach (var id in ids)
        {
            establishment[id] = new PositionEstablishment(
                ApprovedFrom(attributes.GetValueOrDefault(id)),
                filled.GetValueOrDefault(id));
        }

        return establishment;
    }

    public async Task<IReadOnlyDictionary<string, DimensionNodeRef>> GetUnitsAsync(
        IReadOnlyList<string> positionRecordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionRecordIds);

        var units = new Dictionary<string, DimensionNodeRef>(StringComparer.Ordinal);

        if (positionRecordIds.Count == 0)
        {
            return units;
        }

        var date = asAt ?? await _authorisation.TodayAsync();
        var ids = positionRecordIds.Distinct(StringComparer.Ordinal).ToList();

        // One ancestor read per organisation axis — one on a real tenant, one per company on the
        // demo one. Never one per position.
        foreach (var axis in await OrganisationAxesAsync(cancellationToken))
        {
            var paths = await _graph.GetAncestorPathsAsync(axis.StructureId, ids, date, cancellationToken);

            foreach (var (recordId, path) in paths)
            {
                // Nearest last: GetAncestorPathsAsync returns root first, so the unit a position
                // sits directly in is the end of the list.
                if (path.Count > 0 && !units.ContainsKey(recordId))
                {
                    units[recordId] = path[^1];
                }
            }
        }

        return units;
    }

    public async Task<bool> IsReportingLineAsync(
        string structureId,
        CancellationToken cancellationToken = default)
    {
        var structure = await _structures.GetAsync(structureId, cancellationToken);

        if (structure is null || structure.Levels.Count == 0)
        {
            return false;
        }

        var positionTypeIds = (await GetTypesInRoleAsync(DimensionTypeRole.Position, cancellationToken))
            .Select(type => type.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        // Every level, not "any": an axis with departments and positions on it is the organisation
        // chart, which is the one this must not be mistaken for.
        return structure.DimensionTypeIds.All(positionTypeIds.Contains);
    }

    public async Task<PositionPlacementReadiness> GetPlacementReadinessAsync(
        string positionDimensionTypeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var reporting = await GetReportingLineAsync(cancellationToken);
        var axes = new List<StructureDocument>();

        foreach (var structure in await _structures.ListAsync(cancellationToken))
        {
            if (reporting is not null &&
                string.Equals(structure.StructureId, reporting.StructureId, StringComparison.Ordinal))
            {
                continue;
            }

            if (structure.DimensionTypeIds.Contains(positionDimensionTypeId, StringComparer.Ordinal))
            {
                axes.Add(structure);
            }
        }

        if (axes.Count == 0)
        {
            return PositionPlacementReadiness.NotReady;
        }

        foreach (var axis in axes)
        {
            if (axis.PermitsRoot(positionDimensionTypeId))
            {
                return new PositionPlacementReadiness(axes, HasSomewhereToGo: true);
            }

            // Not "does the map declare a pairing" but "is there a unit it would actually offer":
            // an axis that permits Department → Position and has no departments yet is a picker with
            // nothing in it, and the screen should say the same thing about both.
            var parents = await _graph.SearchPermittedParentsAsync(
                axis.StructureId,
                positionDimensionTypeId,
                searchText: null,
                withinRecordId: null,
                date,
                take: 1,
                cancellationToken);

            if (parents.Total > 0)
            {
                return new PositionPlacementReadiness(axes, HasSomewhereToGo: true);
            }
        }

        return new PositionPlacementReadiness(axes, HasSomewhereToGo: false);
    }

    /// <summary>
    /// The organisation axes positions sit on: every structure except the reporting line.
    /// </summary>
    /// <remarks>
    /// ADR-0028's two axes, read from the one end that needs telling them apart. The reporting line
    /// is excluded by <em>code</em> because that is what identifies it — it is shipped by a recipe
    /// and a tenant may have more than one organisation axis, so "the other one" is not a rule.
    /// </remarks>
    public async Task<Page<PositionHolder>> GetHoldersAsync(
        string positionRecordId,
        DateOnly? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();

        // Every organisation axis, because a position sits on exactly one and which one is the
        // tenant's business — the same loop GetEstablishmentAsync and GetUnitsAsync make, so the
        // count and the list below it are drawn from the same rows.
        var rows = new List<EmployeeAssignment>();

        foreach (var axis in await OrganisationAxesAsync(cancellationToken))
        {
            var found = await _assignments.GetEmployeesUnderAsync(
                axis.StructureId,
                positionRecordId,
                date,
                // At the position itself. A position has nothing under it on an organisation axis
                // today, but asking for descendants would make this answer change the day somebody
                // puts a dimension beneath Position on their chart — and "who holds this post" must
                // not start including who holds the posts below it.
                includeDescendants: false,
                skip: 0,
                take: HolderCap,
                cancellationToken);

            rows.AddRange(found.Items);
        }

        // One holder per employee, however many rows they have here. A split allocation is several
        // rows for one person at one node (D1, architecture §2.5), and the row kept is the one with
        // the largest allocation — the one that most nearly answers what they do here.
        var kept = rows
            .GroupBy(row => row.EmployeeId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(row => row.AllocationPercent).First())
            .OrderBy(row => row.Range.From)
            .ThenBy(row => row.EmployeeId, StringComparer.Ordinal)
            .ToList();

        // Paged before the grades are resolved, so the read below is one query for a page rather
        // than one for every holder a position has ever had.
        var page = kept.Skip(skip).Take(take).ToList();

        var gradeIds = page
            .Select(row => row.GradeRecordId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var grades = new Dictionary<string, DimensionNodeRef>(StringComparer.Ordinal);

        foreach (var gradeId in gradeIds)
        {
            // Undated on purpose: a grade removed from the position, or retired outright, still has
            // to name itself on a row that already carries it. D26 G — removing a grade "never
            // breaks that employee's record", and a holder list showing a blank where a grade is
            // recorded would be exactly that break.
            if (await _records.GetAsync(gradeId!, DateOnly.MaxValue, cancellationToken) is { } grade)
            {
                grades[gradeId!] = grade;
            }
        }

        return new Page<PositionHolder>(
            [
                .. page.Select(row => new PositionHolder(
                    row.EmployeeId,
                    row.GradeRecordId is { } gradeId ? grades.GetValueOrDefault(gradeId) : null,
                    row.Range.From,
                    row.AllocationPercent)),
            ],
            kept.Count,
            skip,
            take);
    }

    /// <summary>
    /// How many assignment rows at one position are read before the holder list stops being exact.
    /// </summary>
    /// <remarks>
    /// The paging above is over the <em>deduplicated</em> holders, so the page has to be computed
    /// from a read that is not itself paged — which is why this cap exists and why it is well above
    /// any real approved headcount. <em>Labourer</em> is 44 in the brief's own example. A position
    /// that genuinely exceeded this would need the deduplication pushed into the query, which is a
    /// change to <c>IEmployeeAssignmentService</c> rather than to this method.
    /// </remarks>
    private const int HolderCap = 500;

    public async Task<IReadOnlyList<StructureDocument>> GetReportingAxesAsync(
        CancellationToken cancellationToken = default)
    {
        var positionTypes = (await GetTypesInRoleAsync(DimensionTypeRole.Position, cancellationToken))
            .Select(type => type.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        if (positionTypes.Count == 0)
        {
            return [];
        }

        return
        [
            .. (await _structures.ListAsync(cancellationToken))
                .Where(structure =>
                    structure.DimensionTypeIds.Count > 0 &&
                    structure.DimensionTypeIds.All(positionTypes.Contains)),
        ];
    }

    public async Task<IReadOnlyList<EmployeeAxis>> GetEmployeeAxesAsync(
        CancellationToken cancellationToken = default)
    {
        var positionTypes = (await GetTypesInRoleAsync(DimensionTypeRole.Position, cancellationToken))
            .Select(type => type.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        var gradeTypes = (await GetTypesInRoleAsync(DimensionTypeRole.Grade, cancellationToken))
            .Select(type => type.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        var axes = new List<EmployeeAxis>();

        foreach (var structure in await _structures.ListAsync(cancellationToken))
        {
            var levels = structure.DimensionTypeIds;

            // The one test, shared with PermitsEmployeesAtAsync and with the validator: an axis
            // with no levels, a reporting line and a grade ladder are not places a person goes.
            if (!EmployeeAxes.HoldsEmployees(levels, positionTypes, gradeTypes))
            {
                continue;
            }

            axes.Add(new EmployeeAxis(structure, levels.Any(positionTypes.Contains)));
        }

        return axes;
    }

    /// <inheritdoc />
    public async Task<bool> PermitsEmployeesAtAsync(
        StructureDocument axis,
        string dimensionTypeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(axis);

        // The tenant's own attachable rule first. An axis that declares none is unconstrained,
        // which is the normal state of one being set up.
        if (!axis.PermitsEmployeesAt(dimensionTypeId))
        {
            return false;
        }

        var positionTypes = (await GetTypesInRoleAsync(DimensionTypeRole.Position, cancellationToken))
            .Select(type => type.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        var gradeTypes = (await GetTypesInRoleAsync(DimensionTypeRole.Grade, cancellationToken))
            .Select(type => type.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        // IS THIS A PLACE AT ALL? Asked first, and it was the question missing from here.
        //
        // The reporting line is positions all the way down, so the D30 test below — "is the node a
        // position?" — said YES for every one of its cards, and the designer offered "Place an
        // employee here…" on a structure that is a post's answer-to chain rather than anywhere
        // somebody sits. The grade ladder got there by the other door: no Position level at all,
        // so the old code returned true for an axis D26 C says no employee rolls up to.
        //
        // The shape test, shared with GetEmployeeAxesAsync and with DimensionValidator, so the
        // card, the screen, the recipe step and the import answer this identically.
        if (!EmployeeAxes.HoldsEmployees(axis.DimensionTypeIds, positionTypes, gradeTypes))
        {
            return false;
        }

        // Asked of the axis's shape and never of a code or a name, the same judgement
        // GetEmployeeAxesAsync makes: a tenant may rename anything, and the demo's are prefixed.
        if (!axis.DimensionTypeIds.Any(positionTypes.Contains))
        {
            // No Position level, so D30 says nothing about this axis and nothing changes for it.
            return true;
        }

        // D30: on an axis that has positions, a person joins by holding one.
        return positionTypes.Contains(dimensionTypeId);
    }

    private async Task<IReadOnlyList<StructureDocument>> OrganisationAxesAsync(
        CancellationToken cancellationToken)
    {
        var reporting = await GetReportingLineAsync(cancellationToken);
        var structures = await _structures.ListAsync(cancellationToken);

        return
        [
            .. structures.Where(structure =>
                reporting is null ||
                !string.Equals(structure.StructureId, reporting.StructureId, StringComparison.Ordinal)),
        ];
    }

    /// <summary>
    /// Where each position sits in the business, as a displayable path. Empty for one that is not
    /// placed anywhere, which is a real state and is shown as such.
    /// </summary>
    private async Task<Dictionary<string, string>> UnitPathsAsync(
        List<DimensionNodeRef> positions,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);

        if (positions.Count == 0)
        {
            return paths;
        }

        var ids = positions.Select(position => position.RecordId).ToList();

        foreach (var axis in await OrganisationAxesAsync(cancellationToken))
        {
            var ancestors = await _graph.GetAncestorPathsAsync(
                axis.StructureId, ids, date, cancellationToken);

            foreach (var (recordId, path) in ancestors)
            {
                if (path.Count == 0 || paths.ContainsKey(recordId))
                {
                    continue;
                }

                paths[recordId] = string.Join(
                    BilingualDisplay.PathSeparator,
                    path.Select(unit => BilingualDisplay.Name(unit.NameEn, unit.NameAr)));
            }
        }

        return paths;
    }

    /// <summary>
    /// The approved headcount off the record's attributes, or the default when nobody has stated
    /// one.
    /// </summary>
    /// <remarks>
    /// <b>Parsed invariantly</b>, because the value is stored as text by Orchard's
    /// <c>NumericField</c> and a culture-aware parse would read "1,5" differently for two readers of
    /// the same tenant. A value that is not a number at all, or is negative, falls back to the
    /// default rather than throwing: this is a read behind a list screen, and a single malformed
    /// attribute must not take the page down.
    /// </remarks>
    private static int ApprovedFrom(IReadOnlyList<DimensionAttributeValue>? attributes)
    {
        var stated = attributes?.FirstOrDefault(attribute => string.Equals(
            attribute.Name,
            PositionEstablishment.ApprovedHeadcountAttribute,
            StringComparison.Ordinal));

        if (stated is null || string.IsNullOrWhiteSpace(stated.Value))
        {
            return PositionEstablishment.DefaultApproved;
        }

        return decimal.TryParse(
            stated.Value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed) && parsed >= 0
            ? (int)Math.Round(parsed, MidpointRounding.AwayFromZero)
            : PositionEstablishment.DefaultApproved;
    }
}
