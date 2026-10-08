using WorkMate.Core;
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
/// <b>Leavers are offered, and that is deliberate.</b> A picker that showed only active employees
/// would be right for "who should head this department" and wrong for every form that is recording
/// something dated in the past — and the service behind each form already has the rule that
/// actually matters (a head may not be appointed from a date after they left, for instance). A
/// picker that silently withholds somebody makes that rule look like a missing person rather than
/// a refusal.
/// </remarks>
internal sealed class EmployeeDirectory : IEmployeeDirectory
{
    private readonly IEmployeeService _employees;
    private readonly IVisibilityService _visibility;

    public EmployeeDirectory(IEmployeeService employees, IVisibilityService visibility)
    {
        _employees = employees;
        _visibility = visibility;
    }

    public async Task<EmployeeChoices> SearchAsync(
        string? query,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        // One more than asked for is NOT how the cap is detected: ListAsync returns the true total
        // alongside the page, so the control can say "100 of 400" rather than "100, and there may
        // be more".
        var page = await _employees.ListAsync(query, status: null, skip: 0, take: take, cancellationToken);

        var visible = await _visibility.FilterEmployeesAsync(
            [.. page.Items.Select(employee => employee.EmployeeId)], cancellationToken);

        var allowed = visible.ToHashSet(StringComparer.Ordinal);

        return new EmployeeChoices(
            [.. page.Items
                .Where(employee => allowed.Contains(employee.EmployeeId))
                .Select(ToChoice)],
            page.Total);
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

        return employee is null ? null : ToChoice(employee);
    }

    /// <summary>
    /// The name in the reader's language, falling back rather than to nothing — ADR-0003's
    /// addendum. A picker is the worst place to show a blank: there is nothing else on the row to
    /// tell one person from another.
    /// </summary>
    private static EmployeeChoice ToChoice(EmployeeRecord employee) => new(
        employee.EmployeeId,
        employee.Code,
        BilingualText.Display(employee.NameEn, employee.NameAr));
}
