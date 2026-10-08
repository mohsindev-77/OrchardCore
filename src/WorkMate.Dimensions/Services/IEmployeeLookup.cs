using WorkMate.Core;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// How this module resolves an employee without depending on the module that owns them.
/// </summary>
/// <remarks>
/// <c>WorkMate.Records</c> owns the employee record and depends on this module; this module cannot
/// depend back. The same shape as <see cref="IDimensionDeletionBlockerProvider"/>, and for the same
/// reason: the dimension engine needs an answer about something another module holds, so it
/// declares the question and the owning module answers it.
///
/// Three callers need it. <c>unit-heads</c> and <c>employee-assignments</c> resolve an
/// <c>employeeCode</c> to an id, because a recipe references employees by code and never by a
/// generated id. <c>IDimensionValidator.ValidateHeadAppointmentAsync</c> checks that a head is a
/// real employee who has not left. The organisation designer resolves a head's name for its card.
///
/// Resolved as <c>IEnumerable&lt;IEmployeeLookup&gt;</c> rather than as a single service, so that
/// this module still starts when <c>WorkMate.Records</c> is disabled. A caller that finds none says
/// so by name — "the employee record is not enabled on this tenant" — rather than reporting every
/// employee as missing, which is the same failure wearing a misleading message.
/// </remarks>
public interface IEmployeeLookup
{
    /// <summary>The employee with this code, or null. The way a recipe names one.</summary>
    Task<EmployeeRef?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>The employee with this content item id, or null.</summary>
    Task<EmployeeRef?> GetAsync(string employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Several employees at once, keyed by id, absent when one does not resolve.
    /// </summary>
    /// <remarks>
    /// Batched for the same reason <c>CountEmployeesAtAsync</c> is: the caller is a row of designer
    /// cards resolving one head name each, and a query per card is a query per card on every expand.
    /// </remarks>
    Task<IReadOnlyDictionary<string, EmployeeRef>> GetManyAsync(
        IReadOnlyList<string> employeeIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An employee as the dimension engine sees one: enough to name them, check they exist and check
/// they have not left.
/// </summary>
/// <remarks>
/// Deliberately not the employee record. This module has no business knowing an employee's date of
/// birth, and a type that carried it would make every change to the employee record a change to the
/// dimension engine's surface.
/// </remarks>
/// <param name="ExitedOn">
/// The day the employee left, or null while they have not. A head appointment may not run past it,
/// which is the one employment fact the dimension engine has to be able to check for itself.
/// </param>
public sealed record EmployeeRef(
    string EmployeeId,
    string Code,
    string NameEn,
    string NameAr,
    DateOnly? ExitedOn)
{
    /// <summary>Whether the employee had left by <paramref name="asAt"/>.</summary>
    public bool HasLeftBy(DateOnly asAt) => ExitedOn is not null && ExitedOn < asAt;

    /// <summary>The name to show in <paramref name="culture"/>, falling back rather than to nothing.</summary>
    /// <remarks>
    /// ADR-0003's addendum: a reader is never shown a blank name, and
    /// <see cref="BilingualText.Display(string?, string?, System.Globalization.CultureInfo?)"/> is
    /// the single implementation of that fallback.
    /// </remarks>
    public string ForCulture(System.Globalization.CultureInfo? culture = null) =>
        BilingualText.Display(NameEn, NameAr, culture);
}
