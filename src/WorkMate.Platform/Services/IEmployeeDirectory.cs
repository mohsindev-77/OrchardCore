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
/// <c>ContentPickerField</c> served by an <c>IContentPickerResultProvider</c>, and it is what
/// session B's form designer generates. This is for the handful of plain admin forms that are not
/// content items at all — setting a unit's head, naming a line manager, placing somebody.
/// </remarks>
public interface IEmployeeDirectory
{
    /// <summary>
    /// A page of the people the current caller may choose from, matching the query.
    /// </summary>
    /// <remarks>
    /// Paged rather than capped. The old signature took a <c>take</c> and nothing else, which made
    /// every caller a list of the first hundred people in the tenant: on the Zenith demo
    /// organisation that is a hundred of a hundred and fifty-eight, and the other fifty-eight could
    /// not be chosen at all, by any means, from any screen. A page plus a total plus a search is
    /// the shape that has no such hole — anybody can be reached by narrowing, and
    /// <see cref="EmployeeChoices.Total"/> says how much narrowing is left to do.
    /// </remarks>
    Task<EmployeeChoices> SearchAsync(
        EmployeeDirectoryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>One person, named, or null. For showing what a stored id currently points at.</summary>
    Task<EmployeeChoice?> GetAsync(string employeeId, CancellationToken cancellationToken = default);
}

/// <summary>What a picker is asking the directory for.</summary>
/// <param name="Search">
/// Matched against the employee code and both halves of the name, so an Arabic reader can find
/// somebody a colleague named in English and the other way round.
/// </param>
/// <param name="IncludeExited">
/// Whether people who have left are offered.
/// </param>
/// <remarks>
/// <b>Why leavers are a parameter and not a policy.</b> A form recording something dated in the past
/// must be able to name somebody who has since left; a form appointing a head must not, because the
/// service would refuse it. Both are right, so the caller says which it is — and the screen that
/// excludes them says so in its hint, because a picker that silently withholds a person looks like
/// a missing record rather than a rule.
/// </remarks>
public sealed record EmployeeDirectoryQuery(
    string? Search = null,
    int Skip = 0,
    int Take = 20,
    bool IncludeExited = true)
{
    /// <summary>The largest page this will hand out, however much is asked for.</summary>
    /// <remarks>
    /// Not a cap on who can be found — search reaches everybody — but a cap on one response, so a
    /// caller that passes <c>int.MaxValue</c> gets a page rather than the tenant.
    /// </remarks>
    public const int MaximumTake = 50;

    public int PageSize => Math.Clamp(Take, 1, MaximumTake);
}

/// <summary>One person, as a picker offers them.</summary>
/// <param name="Name">Already resolved for the reader's language; a picker never shows a blank.</param>
/// <param name="Where">
/// The unit they primarily sit in on the primary organisation structure today, or null when they
/// are not placed anywhere.
/// </param>
/// <remarks>
/// <see cref="Where"/> is on the option because a code and a name are not always enough to tell two
/// people apart, and "which Ahmed Khan" is the question somebody is actually asking when they stop
/// and look at the list. Null renders as nothing rather than as a dash: not being placed is an
/// ordinary state for a new starter, and a dash would read like a missing lookup.
/// </remarks>
public sealed record EmployeeChoice(string EmployeeId, string Code, string Name, string? Where = null);

/// <summary>
/// What a picker found, and whether that was all of it.
/// </summary>
/// <param name="Total">
/// How many matched in total, which is not <c>Items.Count</c> when the page is smaller.
/// </param>
/// <remarks>
/// <see cref="Total"/> is carried rather than left to be inferred because a control that shows the
/// first twenty of four hundred and says nothing is a control that will be used to pick the wrong
/// person. The view says so, and tells the reader to keep typing.
/// </remarks>
public sealed record EmployeeChoices(IReadOnlyList<EmployeeChoice> Items, int Total)
{
    /// <summary>Whether more matched than are being offered on this page.</summary>
    public bool IsTruncated => Total > Items.Count;

    public static EmployeeChoices None { get; } = new([], 0);
}
