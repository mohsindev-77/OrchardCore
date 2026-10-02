using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using Xunit;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// Two administrators editing the same thing at once lose nothing silently.
/// </summary>
/// <remarks>
/// ADR-0005 makes optimistic concurrency part of the storage decision rather than an optional
/// extra, because the alternative is the failure mode nobody notices: two people open the same
/// structure, both save, and the second write quietly erases the first. There is no error, no
/// log line and no way to tell afterwards which change was lost.
///
/// YesSql offers this two ways: a store-level registration through
/// <c>IConfiguration.CheckConcurrentUpdates(Type)</c>, and a per-call
/// <c>SaveAsync(document, checkConcurrency: true)</c>. The store-level route is not open to a
/// module — Orchard builds the store's configuration itself and <c>YesSqlOptions</c> exposes no
/// hook for it — so this module takes the per-call route on every write. These tests prove the
/// per-call route actually works on the pinned version, and that the documents carry the
/// <c>Version</c> property it needs.
/// </remarks>
public sealed class OptimisticConcurrencyTests : IAsyncLifetime
{
    private readonly string _databaseFile =
        Path.Combine(Path.GetTempPath(), $"workmate-concurrency-{Guid.NewGuid():n}.db");

    private IStore _store = default!;

    public async Task InitializeAsync()
    {
        var configuration = new Configuration().UseSqLite($"Data Source={_databaseFile};Cache=Shared");

        _store = await StoreFactory.CreateAndInitializeAsync(configuration);

        await using var connection = _store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync(_store.Configuration.IsolationLevel);

        var builder = new SchemaBuilder(_store.Configuration, transaction);

        await builder.CreateMapIndexTableAsync<StructureIndex>(table => table
            .Column<string>(nameof(StructureIndex.StructureId))
            .Column<string>(nameof(StructureIndex.Code))
            .Column<bool>(nameof(StructureIndex.IsPrimaryOrganisation))
            .Column<bool>(nameof(StructureIndex.AllowSkipLevel))
            .Column<bool>(nameof(StructureIndex.IsStrict))
            .Column<int>(nameof(StructureIndex.LevelCount)));

        await transaction.CommitAsync();

        _store.RegisterIndexes<StructureIndexProvider>();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();

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

    [Fact]
    public async Task TheSecondOfTwoConcurrentWritesIsRefusedRatherThanOverwritingTheFirst()
    {
        await GivenAStructureAsync();

        await using var first = _store.CreateSession();
        await using var second = _store.CreateSession();

        var asFirstSawIt = await LoadAsync(first);
        var asSecondSawIt = await LoadAsync(second);

        asFirstSawIt.Name = new BilingualText("Organisation", "التنظيم");
        await first.SaveAsync(asFirstSawIt, checkConcurrency: true);
        await first.SaveChangesAsync();

        asSecondSawIt.Name = new BilingualText("Reporting line", "خط التقارير");

        var act = async () =>
        {
            await second.SaveAsync(asSecondSawIt, checkConcurrency: true);
            await second.SaveChangesAsync();
        };

        await act.Should().ThrowAsync<ConcurrencyException>(
            "the second administrator loaded the structure before the first one saved, so their "
            + "write would erase a change they never saw");
    }

    [Fact]
    public async Task TheFirstWriteIsWhatTheNextReaderSees()
    {
        await GivenAStructureAsync();

        await using (var first = _store.CreateSession())
        await using (var second = _store.CreateSession())
        {
            var asFirstSawIt = await LoadAsync(first);
            var asSecondSawIt = await LoadAsync(second);

            asFirstSawIt.Name = new BilingualText("Organisation", "التنظيم");
            await first.SaveAsync(asFirstSawIt, checkConcurrency: true);
            await first.SaveChangesAsync();

            asSecondSawIt.Name = new BilingualText("Reporting line", "خط التقارير");

            try
            {
                await second.SaveAsync(asSecondSawIt, checkConcurrency: true);
                await second.SaveChangesAsync();
            }
            catch (ConcurrencyException)
            {
                // The point of the next assertion is what survived, not that this threw.
            }
        }

        await using var reading = _store.CreateSession();

        (await LoadAsync(reading)).Name.En.Should().Be(
            "Organisation",
            "the write that was accepted is the one that stands; the refused one changed nothing");
    }

    [Fact]
    public async Task AWriteOnTopOfTheCurrentVersionSucceeds()
    {
        await GivenAStructureAsync();

        // The check must not be so eager that ordinary sequential editing fails. One
        // administrator saving twice is not a conflict.
        for (var pass = 0; pass < 3; pass++)
        {
            await using var session = _store.CreateSession();

            var document = await LoadAsync(session);
            document.AllowSkipLevel = !document.AllowSkipLevel;

            await session.SaveAsync(document, checkConcurrency: true);
            await session.SaveChangesAsync();
        }

        await using var reading = _store.CreateSession();

        (await LoadAsync(reading)).Version.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ADocumentCarriesTheVersionTheCheckDependsOn()
    {
        // YesSql finds the token by convention, on a property called Version. If one of the
        // documents ever loses it, every checkConcurrency call becomes a no-op and the tests
        // above would be the only thing to notice. This says so directly.
        foreach (var type in new[] { typeof(StructureDocument), typeof(DimensionTypeDocument) })
        {
            var version = type.GetProperty("Version");

            version.Should().NotBeNull("{0} is written with checkConcurrency: true", type.Name);
            version!.PropertyType.Should().Be<long>();
            version.CanWrite.Should().BeTrue("YesSql assigns the token after a successful write");
        }

        await Task.CompletedTask;
    }

    private async Task GivenAStructureAsync()
    {
        await using var session = _store.CreateSession();

        await session.SaveAsync(new StructureDocument
        {
            StructureId = "org",
            Code = "ORG",
            Name = new BilingualText("Organisation", "الهيكل التنظيمي"),
            Levels = [new StructureLevel(0, "business-unit")],
            IsPrimaryOrganisation = true,
        });

        await session.SaveChangesAsync();
    }

    private static async Task<StructureDocument> LoadAsync(ISession session) =>
        await session.Query<StructureDocument, StructureIndex>(index => index.Code == "ORG")
            .FirstOrDefaultAsync()
        ?? throw new InvalidOperationException("The structure the test set up is not there.");
}
