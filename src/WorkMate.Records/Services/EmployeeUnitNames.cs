using WorkMate.Core;
using WorkMate.Dimensions.Services;

namespace WorkMate.Records.Services;

/// <summary>
/// Where each of a set of people sits, by unit name, on one axis or on the best one available.
/// </summary>
/// <remarks>
/// <b>One answer to one question, in one place.</b> Two screens ask it — the employee list's "Where
/// they sit" column and the picker's option text — and they answered it differently. The picker fell
/// back to another axis when the primary organisation had nobody on it; the list did not. So the
/// same person read "Mechanical" in the picker and "Not placed" in the list, which is not a
/// difference anybody could explain and not one either screen meant to make.
///
/// <b>The primary organisation first, then the others by code.</b> An employee sits on several axes
/// at once, so "where do they work" has one unambiguous answer only on the axis the tenant has
/// nominated as its organisation. Falling back matters because that nomination is routinely made
/// before anything is built on it — the base recipe ships an empty <c>organisation</c> structure —
/// and a column that stayed blank until somebody populated the right axis would be a feature nobody
/// ever saw working. The fallback order is fixed so the same person gets the same answer on every
/// screen rather than whichever axis came back first.
///
/// <b>Batched.</b> One query per axis for the whole page, stopping as soon as everybody is accounted
/// for, and one name lookup per <em>distinct unit</em> rather than per person. The list screen used
/// to issue one query per row, which is fifty round trips to draw a column.
/// </remarks>
public sealed class EmployeeUnitNames
{
    private readonly IStructureService _structures;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionService _records;

    public EmployeeUnitNames(
        IStructureService structures,
        IEmployeeAssignmentService assignments,
        IDimensionService records)
    {
        _structures = structures;
        _assignments = assignments;
        _records = records;
    }

    /// <param name="onStructureId">
    /// The axis to answer on, when the caller is already looking at one. Null asks for the best
    /// available, which is what a list of people from across the tenant wants.
    /// </param>
    public async Task<Dictionary<string, string>> ForAsync(
        IReadOnlyList<string> employeeIds,
        string? onStructureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        if (employeeIds.Count == 0)
        {
            return found;
        }

        var axes = await AxesAsync(onStructureId, cancellationToken);

        if (axes.Count == 0)
        {
            return found;
        }

        var outstanding = employeeIds.Distinct(StringComparer.Ordinal).ToList();
        var units = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var structureId in axes)
        {
            if (outstanding.Count == 0)
            {
                break;
            }

            var placements = await _assignments.GetPrimaryPlacementsAsync(
                structureId, outstanding, asAt, cancellationToken);

            if (placements.Count == 0)
            {
                continue;
            }

            foreach (var recordId in placements.Values
                .Select(placement => placement.RecordId)
                .Distinct(StringComparer.Ordinal)
                .Where(recordId => !units.ContainsKey(recordId)))
            {
                var record = await _records.GetAsync(recordId, asAt, cancellationToken);

                if (record is not null)
                {
                    units[recordId] = BilingualText.Display(record.NameEn, record.NameAr);
                }
            }

            foreach (var (employeeId, placement) in placements)
            {
                if (units.TryGetValue(placement.RecordId, out var name))
                {
                    found[employeeId] = name;
                }
            }

            outstanding = [.. outstanding.Where(employeeId => !found.ContainsKey(employeeId))];
        }

        return found;
    }

    /// <summary>The axis asked for, or every axis with the primary organisation first.</summary>
    private async Task<IReadOnlyList<string>> AxesAsync(
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
