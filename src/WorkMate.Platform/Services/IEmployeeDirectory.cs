namespace WorkMate.Platform.Services;

/// <summary>
/// Enough of the employee list for a plain admin form to offer a choice of people.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> <c>workmate-employee-picker</c> is a tag helper in this module —
/// the shared component set lives here — and the employees it would offer live in
/// <c>WorkMate.Records</c>, which depends on this module and cannot be depended on back. So the
/// question is declared here and answered there, the same shape as
/// <c>WorkMate.Dimensions.Services.IEmployeeLookup</c> and for the same reason.
///
/// Resolved as a collection, so this module still works on a tenant where the employee record is
/// disabled: the tag helper then renders the placeholder it rendered before employees existed,
/// rather than failing to resolve a service.
///
/// <b>What this is not.</b> It is not the employee picker <em>field</em> — specification section 5's
/// "content picker restricted to Employee with visibility filtering". That is an Orchard
/// <c>ContentPickerField</c> served by an <c>IContentPickerResultProvider</c>, it scales by
/// searching rather than by listing, and it is what session B's form designer generates. This is
/// for the handful of plain admin forms that are not content items at all — setting a unit's head,
/// naming a line manager — where a server-rendered control that works with no script is the right
/// answer and an Orchard content picker is not available.
/// </remarks>
public interface IEmployeeDirectory
{
    /// <summary>
    /// People the current caller may choose from, newest search surface first: code or name.
    /// </summary>
    /// <param name="take">
    /// A hard cap. The control is a list, so it has to stop somewhere, and stopping silently is
    /// what makes a picker quietly wrong on a large tenant — see <see cref="EmployeeChoices"/>.
    /// </param>
    Task<EmployeeChoices> SearchAsync(
        string? query,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>One person, named, or null. For showing what a stored id currently points at.</summary>
    Task<EmployeeChoice?> GetAsync(string employeeId, CancellationToken cancellationToken = default);
}

/// <summary>One person, as a picker offers them.</summary>
/// <param name="Name">Already resolved for the reader's language; a picker never shows a blank.</param>
public sealed record EmployeeChoice(string EmployeeId, string Code, string Name);

/// <summary>
/// What a picker found, and whether that was all of it.
/// </summary>
/// <param name="Total">
/// How many matched in total, which is not <c>Items.Count</c> when the cap bit.
/// </param>
/// <remarks>
/// <see cref="Total"/> is carried rather than left to be inferred because a control that shows the
/// first hundred of four hundred and says nothing is a control that will be used to pick the wrong
/// person. The view says so, and tells the reader to narrow the search.
/// </remarks>
public sealed record EmployeeChoices(IReadOnlyList<EmployeeChoice> Items, int Total)
{
    /// <summary>Whether more matched than are being offered.</summary>
    public bool IsTruncated => Total > Items.Count;

    public static EmployeeChoices None { get; } = new([], 0);
}
