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
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var positionTypes = await GetTypesInRoleAsync(DimensionTypeRole.Position, cancellationToken);

        if (positionTypes.Count == 0)
        {
            return new Page<PositionOption>([], 0, 0, take);
        }

        // Active only (D26 F). A position that has closed is not a post anybody can be put into, and
        // offering it greyed out is offering it.
        var page = await _graph.SearchRecordsOfTypesAsync(
            [.. positionTypes.Select(type => type.DimensionTypeId)],
            searchText,
            structureId: null,
            withinRecordId: null,
            DimensionRecordStatus.Active,
            date,
            skip: 0,
            take,
            cancellationToken);

        var positions = page.Items.ToList();
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
