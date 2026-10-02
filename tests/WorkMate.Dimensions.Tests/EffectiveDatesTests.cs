using FluentAssertions;
using WorkMate.Dimensions.Indexes;
using Xunit;
using YesSql;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The date encoding, asserted both ways and against the providers it has to survive.
/// </summary>
/// <remarks>
/// This exists because of a specific trap. YesSql 5.4.7 has no mapping for <c>DateOnly</c>, so
/// every effective date in this module is converted to a <c>DateTime</c> on the way into an
/// index column and back on the way out — and once a date is a <c>DateTime</c> a reviewer
/// reading an index class cannot see whether the conversion was right. A silent off-by-one on
/// the open-ended sentinel would make every open assignment look closed, with no error anywhere.
/// ADR-0005 records the decision; this is the test that holds it.
/// </remarks>
public sealed class EffectiveDatesTests
{
    public static TheoryData<DateOnly> BoundaryDates()
    {
        var data = new TheoryData<DateOnly>();

        foreach (var date in new[]
        {
            DateOnly.MinValue,
            new DateOnly(1900, 1, 1),
            new DateOnly(2026, 3, 15),   // the day before the worked transfer in the architecture
            new DateOnly(2026, 3, 16),   // the day of it
            new DateOnly(2024, 2, 29),   // a leap day
            new DateOnly(9999, 12, 30),  // the day before the sentinel
            EffectiveDates.OpenEndedDate,
        })
        {
            data.Add(date);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BoundaryDates))]
    public void ADateSurvivesTheRoundTripThroughAnIndexColumn(DateOnly date) =>
        EffectiveDates.FromColumn(EffectiveDates.ToColumn(date)).Should().Be(date);

    [Fact]
    public void AnOpenEndedRangeStoresTheSentinelAndReadsBackAsOpen()
    {
        var stored = EffectiveDates.ToInclusiveEndColumn(null);

        stored.Should().Be(EffectiveDates.OpenEnded);
        EffectiveDates.FromInclusiveEndColumn(stored).Should().BeNull();
    }

    [Fact]
    public void AClosedRangeKeepsItsEndDate()
    {
        var end = new DateOnly(2026, 3, 15);

        EffectiveDates.FromInclusiveEndColumn(EffectiveDates.ToInclusiveEndColumn(end)).Should().Be(end);
    }

    [Fact]
    public void TheSentinelIsWithinWhatSqlServerDatetimeHolds()
    {
        // SQL Server's datetime stops at 9999-12-31 23:59:59.997, so DateTime.MaxValue —
        // 9999-12-31 23:59:59.9999999 — overflows it and the insert fails at runtime rather than
        // at review. The sentinel is the date at midnight for exactly that reason.
        EffectiveDates.OpenEnded.Should().BeBefore(DateTime.MaxValue);
        EffectiveDates.OpenEnded.TimeOfDay.Should().Be(TimeSpan.Zero);
        EffectiveDates.OpenEnded.Should().Be(new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void ACalendarDateIsNotTaggedWithATimeZone() =>
        // Unspecified, not Utc or Local. The day an employee transfers is the same day in every
        // time zone the tenant operates in, and a Kind would invite a conversion that moves it.
        EffectiveDates.ToColumn(new DateOnly(2026, 3, 16)).Kind.Should().Be(DateTimeKind.Unspecified);

    /// <summary>
    /// The reason every date in this module is a <c>DateTime</c> in the database, asserted
    /// against the pinned providers rather than taken from a comment.
    /// </summary>
    /// <remarks>
    /// If a future YesSql gains a <c>DateOnly</c> mapping this test fails, which is the signal to
    /// revisit ADR-0005 rather than to discover the capability by accident years later.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Dialects))]
    public void TheProviderStillHasNoMappingForDateOnly(string name, ISqlDialect dialect)
    {
        name.Should().NotBeEmpty();

        var dateOnly = dialect.ToDbType(typeof(DateOnly));
        var act = () => dialect.GetTypeName(dateOnly, null, null, null);

        act.Should().Throw<Exception>(
            "YesSql has no column type for DateOnly, which is why EffectiveDates exists");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void TheProviderHasAColumnTypeForWhatEffectiveDatesProduces(string name, ISqlDialect dialect)
    {
        name.Should().NotBeEmpty();

        var stored = EffectiveDates.ToColumn(new DateOnly(2026, 3, 16));
        var dbType = dialect.ToDbType(stored.GetType());

        dialect.GetTypeName(dbType, null, null, null).Should().NotBeNullOrWhiteSpace();
    }

    public static TheoryData<string, ISqlDialect> Dialects()
    {
        var data = new TheoryData<string, ISqlDialect>();

        // SQLite is what the integration suite and a developer's tenant run on; SQL Server is
        // what a customer deployment runs on. The encoding has to hold on both.
        data.Add("Sqlite", new YesSql.Provider.Sqlite.SqliteDialect());
        data.Add("SqlServer", new YesSql.Provider.SqlServer.SqlServerDialect());

        return data;
    }
}
