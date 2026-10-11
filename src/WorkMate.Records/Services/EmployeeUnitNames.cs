using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Records.Services;

/// <summary>One of an employee's current placements, named and located.</summary>
/// <param name="Name">The unit's own name, for a column that has room for one line.</param>
/// <param name="Path">
/// The whole chain — "Crescent › Retail Banking › Credit" — for anywhere a reader has to tell two
/// units of the same name apart. Crescent has two departments called "Credit", so a bare name is
/// genuinely ambiguous rather than merely terse.
/// </param>
/// <param name="IsMain">
/// Whether this is the placement that answers "where does this person work". Exactly one of a
/// concurrent set is.
/// </param>
public sealed record EmployeePlacementLine(
    string RecordId,
    string Name,
    string Path,
    decimal AllocationPercent,
    bool IsMain);

/// <summary>Where one employee sits on one structure right now: the main row, and the rest.</summary>
/// <remarks>
/// <b>Main and others, not "a placement".</b> Somebody split 60/40 across two units has two true
/// answers to "where do they sit", and a screen that picked one of them arbitrarily was the defect
/// this shape was introduced for — the employee list showed whichever row the engine happened to
/// return first. The main row leads because that is the one the question has an unambiguous answer
/// on; the others are offered beside it rather than hidden.
/// </remarks>
public sealed record EmployeePlacementSummary(
    EmployeePlacementLine? Main,
    IReadOnlyList<EmployeePlacementLine> Others)
{
    /// <summary>How many placements beyond the main one — the "+N more" in the list.</summary>
    public int OtherCount => Others.Count;
}

/// <summary>
/// Where each of a set of people sits, on one structure or on the best one available.
/// </summary>
/// <remarks>
/// <b>One answer to one question, in one place.</b> Two screens ask it — the employee list's
/// placement column and the picker's option text — and they answered it differently. The picker
/// fell back to another structure when the primary organisation had nobody on it; the list did not.
/// So the same person read "Mechanical" in the picker and "Not placed" in the list, which is not a
/// difference anybody could explain and not one either screen meant to make.
///
/// <b>The primary organisation first, then the others by code.</b> An employee sits on several
/// structures at once, so "where do they work" has one unambiguous answer only on the structure the
/// tenant has nominated as its organisation. Falling back matters because that nomination is
/// routinely made before anything is built on it — the base recipe ships an empty
/// <c>organisation</c> structure — and a column that stayed blank until somebody populated the right
/// structure would be a feature nobody ever saw working. The fallback order is fixed so the same
/// person gets the same answer on every screen rather than whichever structure came back first.
///
/// <b>Batched.</b> Per structure: one query for everybody's placements, one name lookup per
/// <em>distinct unit</em> rather than per person, and two more for the paths of those units. The
/// list screen used to issue one query per row, which is fifty round trips to draw a column.
/// </remarks>
public sealed class EmployeeUnitNames
{
    private readonly IStructureService _structures;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionService _records;
    private readonly IDimensionGraphService _graph;

    public EmployeeUnitNames(
        IStructureService structures,
        IEmployeeAssignmentService assignments,
        IDimensionService records,
        IDimensionGraphService graph)
    {
        _structures = structures;
        _assignments = assignments;
        _records = records;
        _graph = graph;
    }

    /// <summary>
    /// The main unit's name for each of these people — what a one-line column shows.
    /// </summary>
    /// <remarks>
    /// A projection of <see cref="SummariesAsync"/> rather than a second query path, so the picker
    /// and the list cannot answer the question differently again.
    /// </remarks>
    /// <param name="onStructureId">
    /// The structure to answer on, when the caller is already looking at one. Null asks for the
    /// best available, which is what a list of people from across the tenant wants.
    /// </param>
    public async Task<Dictionary<string, string>> ForAsync(
        IReadOnlyList<string> employeeIds,
        string? onStructureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var summaries = await SummariesAsync(employeeIds, onStructureId, asAt, cancellationToken);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (employeeId, summary) in summaries)
        {
            if (summary.Main is { } main)
            {
                names[employeeId] = main.Name;
            }
        }

        return names;
    }

    /// <inheritdoc cref="ForAsync"/>
    public async Task<Dictionary<string, EmployeePlacementSummary>> SummariesAsync(
        IReadOnlyList<string> employeeIds,
        string? onStructureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        var found = new Dictionary<string, EmployeePlacementSummary>(StringComparer.Ordinal);

        if (employeeIds.Count == 0)
        {
            return found;
        }

        var structures = await StructuresAsync(onStructureId, cancellationToken);

        if (structures.Count == 0)
        {
            return found;
        }

        var outstanding = employeeIds.Distinct(StringComparer.Ordinal).ToList();

        foreach (var structureId in structures)
        {
            if (outstanding.Count == 0)
            {
                break;
            }

            var placements = await _assignments.GetPlacementsAsync(
                structureId, outstanding, asAt, cancellationToken);

            if (placements.Count == 0)
            {
                continue;
            }

            var lines = await LinesAsync(structureId, placements, asAt, cancellationToken);

            foreach (var (employeeId, rows) in placements)
            {
                var resolved = rows
                    .Select(row => lines.GetValueOrDefault(row.RecordId) is { } line
                        ? line with { AllocationPercent = row.AllocationPercent, IsMain = row.IsPrimary }
                        : null)
                    .Where(line => line is not null)
                    .Select(line => line!)
                    .ToList();

                if (resolved.Count == 0)
                {
                    continue;
                }

                // Primary first, and the engine already orders them that way. Taking the first
                // regardless of the flag would be the arbitrary choice this type exists to stop.
                //
                // Falling back to the first row when none is marked is for a tenant whose data
                // predates the rule or has been corrupted: exactly one primary is what the
                // validator enforces, so none is a defect somebody else has to fix, and a column
                // reading "Not placed" beside a "+2 more" would describe it wrongly rather than
                // report it.
                var main = resolved.FirstOrDefault(line => line.IsMain) ?? resolved[0];

                found[employeeId] = new EmployeePlacementSummary(
                    main,
                    [.. resolved.Where(line => !ReferenceEquals(line, main))]);
            }

            outstanding = [.. outstanding.Where(employeeId => !found.ContainsKey(employeeId))];
        }

        return found;
    }

    /// <summary>
    /// Every distinct unit in a structure's placements, named and with its full path. Two queries
    /// for the page rather than two per person.
    /// </summary>
    private async Task<Dictionary<string, EmployeePlacementLine>> LinesAsync(
        string structureId,
        IReadOnlyDictionary<string, IReadOnlyList<EmployeeAssignment>> placements,
        DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        var recordIds = placements.Values
            .SelectMany(rows => rows.Select(row => row.RecordId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var paths = await _graph.GetAncestorPathsAsync(structureId, recordIds, asAt, cancellationToken);
        var lines = new Dictionary<string, EmployeePlacementLine>(StringComparer.Ordinal);

        foreach (var recordId in recordIds)
        {
            var record = await _records.GetAsync(recordId, asAt, cancellationToken);

            if (record is null)
            {
                continue;
            }

            lines[recordId] = new EmployeePlacementLine(
                recordId,
                BilingualText.Display(record.NameEn, record.NameAr),
                BilingualDisplay.Path(paths.GetValueOrDefault(recordId), record),

                // Filled in per employee by the caller: the same unit is 60% of one person and
                // 100% of the next, so the allocation is not a property of the unit.
                AllocationPercent: 0m,
                IsMain: false);
        }

        return lines;
    }

    /// <summary>The structure asked for, or every structure with the primary organisation first.</summary>
    private async Task<IReadOnlyList<string>> StructuresAsync(
        string? onStructureId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(onStructureId))
        {
            return [onStructureId];
        }

        var organisation = await _structures.GetPrimaryOrganisationAsync(cancellationToken);

        return
        [
            .. (await _structures.ListAsync(cancellationToken))
                .OrderByDescending(structure =>
                    organisation is not null &&
                    string.Equals(structure.StructureId, organisation.StructureId, StringComparison.Ordinal))
                .ThenBy(structure => structure.Code, StringComparer.Ordinal)
                .Select(structure => structure.StructureId),
        ];
    }
}
