using FluentAssertions;
using WorkMate.Dimensions.Indexes;
using Xunit;
using YesSql;
using YesSql.Indexes;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The date encoding, taken all the way to a real database and back.
/// </summary>
/// <remarks>
/// <see cref="EffectiveDatesTests"/> asserts the conversion against itself and against the
/// dialects' type mapping. That is not the whole question: a column can be declared correctly and
/// still lose a value on the way through, and the one thing that would be catastrophic here —
/// the open-ended sentinel coming back as something other than open — only shows up once a
/// provider has actually stored and re-read it.
///
/// SQLite is used because it is what the integration suite and a developer's tenant run on and
/// it needs no server. SQL Server is not covered here for the same reason; its half of the
/// question is answered by <see cref="EffectiveDatesTests.TheSentinelIsWithinWhatSqlServerDatetimeHolds"/>,
/// which pins the sentinel inside what its <c>datetime</c> can hold, and by the pipeline when it
/// runs against a SQL Server tenant.
/// </remarks>
public sealed class EffectiveDatesRoundTripThroughSqliteTests : IAsyncLifetime
{
    private readonly string _databaseFile =
        Path.Combine(Path.GetTempPath(), $"workmate-dates-{Guid.NewGuid():n}.db");

    private IStore _store = default!;

    public async Task InitializeAsync()
    {
        var configuration = new Configuration().UseSqLite($"Data Source={_databaseFile};Cache=Shared");

        _store = await StoreFactory.CreateAndInitializeAsync(configuration);

        await using var connection = _store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync(_store.Configuration.IsolationLevel);

        var builder = new SchemaBuilder(_store.Configuration, transaction);

        await builder.CreateMapIndexTableAsync<DatedThingIndex>(table => table
            .Column<DateTime>(nameof(DatedThingIndex.EffectiveFrom))
            .Column<DateTime>(nameof(DatedThingIndex.EffectiveToInclusive)));

        await transaction.CommitAsync();

        _store.RegisterIndexes<DatedThingIndexProvider>();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();

        // Best effort: a left-behind temp database costs disk, not correctness, and SQLite can
        // still hold the file at this point.
        try
        {
            File.Delete(_databaseFile);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// A range to store, and the end it should read back as.
    /// </summary>
    /// <remarks>
    /// The two differ in exactly one case, and it is deliberate: a range written with an explicit
    /// end of 9999-12-31 reads back as open. That date is how "no end" is stored, and a range
    /// ending on the last day the calendar has is the same statement as a range with no end.
    /// </remarks>
    public static TheoryData<DateOnly, DateOnly?, DateOnly?> Ranges()
    {
        var data = new TheoryData<DateOnly, DateOnly?, DateOnly?>();

        data.Add(new DateOnly(2026, 3, 16), null, null);                                    // open ended
        data.Add(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 15)); // the worked transfer
        data.Add(DateOnly.MinValue, new DateOnly(1900, 1, 1), new DateOnly(1900, 1, 1));    // an early date
        data.Add(new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29)); // one leap day long
        data.Add(new DateOnly(9999, 12, 29), new DateOnly(9999, 12, 30), new DateOnly(9999, 12, 30));
        data.Add(new DateOnly(9999, 12, 30), EffectiveDates.OpenEndedDate, null);

        return data;
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public async Task ARangeSurvivesBeingStoredAndReadBack(
        DateOnly from,
        DateOnly? toInclusive,
        DateOnly? expectedToInclusive)
    {
        await using (var session = _store.CreateSession())
        {
            await session.SaveAsync(new DatedThing { From = from, ToInclusive = toInclusive });
            await session.SaveChangesAsync();
        }

        await using var reading = _store.CreateSession();

        var row = await reading.QueryIndex<DatedThingIndex>().FirstOrDefaultAsync();

        row.Should().NotBeNull();
        EffectiveDates.FromColumn(row!.EffectiveFrom).Should().Be(from);
        EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive).Should().Be(expectedToInclusive);
    }

    [Fact]
    public async Task ADateIsFoundByTheRangeQueryOnItsFirstAndLastDay()
    {
        var from = new DateOnly(2026, 3, 1);
        var toInclusive = new DateOnly(2026, 3, 15);

        await using (var session = _store.CreateSession())
        {
            await session.SaveAsync(new DatedThing { From = from, ToInclusive = toInclusive });
            await session.SaveChangesAsync();
        }

        // The inclusive end is the assumption the whole engine is built on, per the comment on
        // EffectiveRange: the old assignment runs to 15 March and the new one from 16 March. If
        // that ever changes, this is one of the tests that has to change with it.
        (await CountOn(from)).Should().Be(1, "the range includes its first day");
        (await CountOn(toInclusive)).Should().Be(1, "the range includes its last day");
        (await CountOn(from.AddDays(-1))).Should().Be(0, "the day before is outside the range");
        (await CountOn(toInclusive.AddDays(1))).Should().Be(0, "the day after is outside the range");
    }

    [Fact]
    public async Task AnOpenEndedRangeIsStillFoundFarInTheFuture()
    {
        await using (var session = _store.CreateSession())
        {
            await session.SaveAsync(new DatedThing { From = new DateOnly(2026, 3, 16), ToInclusive = null });
            await session.SaveChangesAsync();
        }

        (await CountOn(new DateOnly(2099, 12, 31))).Should().Be(
            1,
            "an open-ended range is stored with the sentinel as its end, so it never expires");
    }

    private async Task<int> CountOn(DateOnly date)
    {
        var asAt = EffectiveDates.ToColumn(date);

        await using var session = _store.CreateSession();

        return await session
            .QueryIndex<DatedThingIndex>(index =>
                index.EffectiveFrom <= asAt && asAt <= index.EffectiveToInclusive)
            .CountAsync();
    }

    /// <summary>A stand-in for the dated documents the graph layer stores.</summary>
    public sealed class DatedThing
    {
        public long Id { get; set; }

        public DateOnly From { get; set; }

        public DateOnly? ToInclusive { get; set; }
    }

    public sealed class DatedThingIndex : MapIndex
    {
        public DateTime EffectiveFrom { get; set; }

        public DateTime EffectiveToInclusive { get; set; }
    }

    public sealed class DatedThingIndexProvider : IndexProvider<DatedThing>
    {
        public override void Describe(DescribeContext<DatedThing> context) =>
            context.For<DatedThingIndex>()
                .Map(thing => new DatedThingIndex
                {
                    EffectiveFrom = EffectiveDates.ToColumn(thing.From),
                    EffectiveToInclusive = EffectiveDates.ToInclusiveEndColumn(thing.ToInclusive),
                });
    }
}
