namespace WorkMate.Dimensions.Indexes;

/// <summary>
/// The one place a calendar date crosses into an index column and back.
/// </summary>
/// <remarks>
/// YesSql 5.4.7 has no mapping for <c>DateOnly</c>: both the SQLite and the SQL Server dialect
/// resolve <c>typeof(DateOnly)</c> to <c>DbType.Object</c>, and <c>GetTypeName</c> then throws
/// <c>DbType not found for: Object</c>, so a migration declaring such a column fails at tenant
/// setup. ADR-0005 records the decision that follows: <c>DateOnly</c> and
/// <see cref="WorkMate.Core.EffectiveRange"/> stay the currency of every service signature and
/// every domain model, and only the index columns are <c>DateTime</c>.
///
/// Keeping both directions here is what makes that safe. A date written through
/// <see cref="ToColumn(DateOnly)"/> and read through <see cref="FromColumn"/> is the same date,
/// and the open-ended sentinel has exactly one definition rather than one per index class.
/// </remarks>
public static class EffectiveDates
{
    /// <summary>
    /// What an open-ended effective range stores as its inclusive end.
    /// </summary>
    /// <remarks>
    /// 9999-12-31 at midnight, not <c>DateTime.MaxValue</c>. SQL Server's <c>datetime</c> stops at
    /// 9999-12-31 23:59:59.997 and <c>DateTime.MaxValue</c> — 9999-12-31 23:59:59.9999999 —
    /// overflows it. Using a sentinel rather than a null keeps the dated range query a plain
    /// <c>From &lt;= date &amp;&amp; date &lt;= To</c> against a two-column index, which is what
    /// holds the 200 ms descendant gate.
    /// </remarks>
    public static readonly DateTime OpenEnded = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>The same date as <see cref="OpenEnded"/>, for comparing against a domain value.</summary>
    public static readonly DateOnly OpenEndedDate = DateOnly.FromDateTime(OpenEnded);

    /// <summary>A calendar date as the midnight <c>DateTime</c> an index column holds.</summary>
    /// <remarks>
    /// <c>DateTimeKind.Unspecified</c>, deliberately. These are calendar dates, not instants: the
    /// day an employee transfers is the same day in every time zone the tenant operates in, and
    /// tagging it Utc or Local would invite a conversion that moves it.
    /// </remarks>
    public static DateTime ToColumn(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>A nullable calendar date as a nullable column value.</summary>
    public static DateTime? ToColumn(DateOnly? date) => date is null ? null : ToColumn(date.Value);

    /// <summary>An index column back to the calendar date it holds.</summary>
    public static DateOnly FromColumn(DateTime value) => DateOnly.FromDateTime(value);

    /// <summary>A nullable index column back to a nullable calendar date.</summary>
    public static DateOnly? FromColumn(DateTime? value) => value is null ? null : FromColumn(value.Value);

    /// <summary>
    /// The inclusive end of an effective range as a column value, substituting
    /// <see cref="OpenEnded"/> for an open end.
    /// </summary>
    public static DateTime ToInclusiveEndColumn(DateOnly? inclusiveEnd) =>
        inclusiveEnd is null ? OpenEnded : ToColumn(inclusiveEnd.Value);

    /// <summary>
    /// The inclusive end of an effective range back from a column value, turning
    /// <see cref="OpenEnded"/> back into an open end.
    /// </summary>
    /// <remarks>
    /// A range written with an explicit end of <see cref="OpenEndedDate"/> reads back as open
    /// rather than as that date. The two are the same statement — a range that ends on the last
    /// day the calendar has is a range with no end — so collapsing them loses nothing a caller
    /// could act on, and the alternative would be a second column carrying a flag that says the
    /// same thing. <c>EffectiveDatesRoundTripThroughSqliteTests</c> asserts this deliberately
    /// rather than leaving it to be discovered.
    /// </remarks>
    public static DateOnly? FromInclusiveEndColumn(DateTime value) =>
        value >= OpenEnded ? null : FromColumn(value);
}
