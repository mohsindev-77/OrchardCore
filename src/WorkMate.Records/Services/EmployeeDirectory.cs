using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// This module's answer to "who can I choose from", for the plain admin forms in the shared
/// component set.
/// </summary>
/// <remarks>
/// The implementation of <see cref="IEmployeeDirectory"/>, which <c>WorkMate.Platform</c> declares
/// because <c>workmate-employee-picker</c> lives there and employees live here.
///
/// <b>Three things every option carries</b> — name, code and where they sit — because two of them
/// are routinely not enough. A tenant has several people with the same name and a code nobody has
/// memorised; "Ahmed Khan (zenith-civil-01) — Civil" is a row somebody can act on, and the other
/// two are a guess.
///
/// Where they sit is read in one batched query for the whole page, never one per row: a picker
/// re-runs on every keystroke, and a lookup per option would be twenty queries per character.
/// </remarks>
internal sealed class EmployeeDirectory : IEmployeeDirectory
{
    private readonly IEmployeeService _employees;
    private readonly IVisibilityService _visibility;
    private readonly EmployeeUnitNames _units;

    public EmployeeDirectory(
        IEmployeeService employees,
        IVisibilityService visibility,
        EmployeeUnitNames units)
    {
        _employees = employees;
        _visibility = visibility;
        _units = units;
    }

    public async Task<EmployeeChoices> SearchAsync(
        EmployeeDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Over-read, because visibility filters afterwards and the page would otherwise come back
        // short — a reader who may see half the tenant would get ten rows where twenty were asked
        // for, and would reasonably conclude there were only ten.
        var window = Math.Min(query.PageSize * 4, 200);
        var kept = new List<EmployeeRecord>();
        var skip = query.Skip;
        var total = 0;

        while (kept.Count < query.PageSize)
        {
            var page = await _employees.ListAsync(
                query.Search, status: null, skip: skip, take: window, cancellationToken: cancellationToken);

            total = page.Total;

            if (page.Items.Count == 0)
            {
                break;
            }

            var candidates = query.IncludeExited
                ? page.Items
                : [.. page.Items.Where(employee => employee.Status != EmploymentStatus.Exited)];

            var visible = (await _visibility.FilterEmployeesAsync(
                [.. candidates.Select(employee => employee.EmployeeId)], cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            kept.AddRange(candidates.Where(employee => visible.Contains(employee.EmployeeId)));

            skip += page.Items.Count;

            if (skip >= page.Total)
            {
                break;
            }
        }

        var offered = kept.Take(query.PageSize).ToList();
        var where = await _units.ForAsync(
            [.. offered.Select(employee => employee.EmployeeId)], onStructureId: null, asAt: null, cancellationToken);

        return new EmployeeChoices(
            [.. offered.Select(employee => ToChoice(employee, where.GetValueOrDefault(employee.EmployeeId)))],
            // The total the list reports, which counts people this reader may not see and people
            // this query excluded. Deliberately not corrected: it is used to say "keep typing",
            // and an exact figure would cost a scan of the tenant to produce a number nobody acts
            // on. The caption says "about".
            total);
    }

    public async Task<EmployeeChoice?> GetAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (!await _visibility.CanSeeEmployeeAsync(employeeId, cancellationToken))
        {
            return null;
        }

        var employee = await _employees.GetAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return null;
        }

        var where = await _units.ForAsync(
            [employee.EmployeeId], onStructureId: null, asAt: null, cancellationToken);

        return ToChoice(employee, where.GetValueOrDefault(employee.EmployeeId));
    }


    /// <summary>
    /// The name in the reader's language, falling back rather than to nothing — ADR-0003's
    /// addendum. A picker is the worst place to show a blank: there is nothing else on the row to
    /// tell one person from another.
    /// </summary>
    private static EmployeeChoice ToChoice(EmployeeRecord employee, string? where) => new(
        employee.EmployeeId,
        employee.Code,
        BilingualText.Display(employee.NameEn, employee.NameAr),
        where);
}
