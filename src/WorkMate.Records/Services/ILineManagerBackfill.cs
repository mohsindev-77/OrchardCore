namespace WorkMate.Records.Services;

/// <summary>
/// The one-time repair that frees every line manager the reporting line already answers.
/// D31 addendum 2.
/// </summary>
/// <remarks>
/// <b>Why this is a service and not ten lines inside the migration step.</b> The step that runs it
/// has to be append-only and is never edited once shipped, so the logic it calls has to be somewhere
/// a test can reach, call twice, and assert counts on. <c>Migrations.UpdateFrom5Async</c> is three
/// lines that resolve this and log what it reports.
///
/// <b>It moves nobody and touches one field.</b> No placement, no assignment, no date, no other
/// property — only <c>EmployeePart.LineManagerEmployeeId</c>, and only where the stored person is
/// the same person the live derivation gives today. ADR-0029's rule, applied to a field instead of
/// to a row.
/// </remarks>
public interface ILineManagerBackfill
{
    /// <summary>
    /// Clears every stored line manager that the chart derives anyway, and reports what it did.
    /// </summary>
    /// <remarks>
    /// Safe to re-enter: it writes nothing where the comparison does not match, so a second run
    /// clears nothing and reports the same population under different headings.
    /// </remarks>
    Task<LineManagerBackfillCounts> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What the repair did, by the four outcomes a stored line manager can have. D31 addendum 2.
/// </summary>
/// <param name="Cleared">
/// Employees who hold a position whose stored manager was exactly what the chart derives. The
/// answer they show does not change; it simply starts being recomputed.
/// </param>
/// <param name="KeptAsChosen">
/// Employees who hold a position and whose stored manager is somebody's pick among several holders
/// — including a pick that has since gone stale and now asks for a new one.
/// </param>
/// <param name="KeptAsOverride">
/// Employees who hold a position and whose stored manager is off the reporting line entirely.
/// </param>
/// <param name="Untouched">
/// Employees who hold no position. Every pre-F6 record and every no-positions tenant: there is no
/// reporting line to derive from, so the stored value is the only answer there is.
/// </param>
public sealed record LineManagerBackfillCounts(
    int Cleared,
    int KeptAsChosen,
    int KeptAsOverride,
    int Untouched)
{
    /// <summary>Nothing to do, from a tenant where nobody has a stored line manager at all.</summary>
    public static readonly LineManagerBackfillCounts Nothing = new(0, 0, 0, 0);

    /// <summary>Every employee the repair looked at.</summary>
    public int Examined => Cleared + KeptAsChosen + KeptAsOverride + Untouched;
}
