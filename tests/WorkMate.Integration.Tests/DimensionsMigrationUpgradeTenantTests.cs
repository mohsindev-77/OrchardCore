using System.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Data.Migration;
using OrchardCore.Data.Migration.Records;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Internal.Graph;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Reproduces the real defect found in production use: a tenant whose
/// <c>DimensionRecordPartIndex</c> table was created before <c>NameAr</c> was added to it — because
/// the column was first added by editing the already-shipped <c>UpdateFrom1Async</c> in place
/// rather than by a new step, so a tenant already past that step never re-ran it. See
/// <c>Migrations.UpdateFrom3Async</c>'s remarks and ADR note on append-only migrations.
///
/// This test does not merely check the column exists after upgrading: it writes a dimension record
/// with a real Arabic name through <see cref="IDimensionService"/> and reads it back, which is
/// exactly the operation that threw "SQLite Error 1: 'table DimensionRecordPartIndex has no column
/// named NameAr'" in the field.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionsMigrationUpgradeTenantTests
{
    private const string MigrationClass = "WorkMate.Dimensions.Migrations";

    private readonly BaseTenantFixture _fixture;

    public DimensionsMigrationUpgradeTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    [Fact]
    public async Task ATenantStuckBeforeTheNameArColumnUpgradesCleanlyAndTheColumnBecomesUsable()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            // Every WorkMate.Dimensions.Migrations.Version before the fix landed. A brand-new test
            // tenant is already fully migrated (to version 4, as of this step) the moment it is
            // created, so reproducing "stuck before the fix" means rolling both the recorded
            // version and the physical schema back by hand — the same state a real tenant that had
            // only ever run up to UpdateFrom1Async, before it was edited, would be in.
            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            record.Should().NotBeNull();

            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);
            migration.Version.Should().Be(4, "this test's premise is the current, fixed schema; it rolls back from here");

            migration.Version = 2;
            session.Save(record);
            await session.SaveChangesAsync();

            // Version 2 means only CreateAsync and UpdateFrom1Async have ever run. UpdateFrom2Async
            // (the graph layer) and UpdateFrom3Async (this fix) have not, so their tables must not
            // exist either — a real tenant stuck at version 2 never had them — or re-running
            // UpdateFrom2Async's CreateMapIndexTableAsync calls would fail on tables that already
            // exist, which is not the defect this test reproduces.
            await RevertSchemaToVersion2Async(services);

            // The upgrade: exactly what restarting the application does, not a direct call to the
            // migration step — this exercises the same discovery-and-catch-up path a real restart
            // uses.
            var migrationManager = services.GetRequiredService<IDataMigrationManager>();
            await migrationManager.UpdateAllFeaturesAsync();

            var recordAfterUpgrade = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migrationAfterUpgrade = recordAfterUpgrade!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);
            migrationAfterUpgrade.Version.Should().Be(4, "the upgrade must run every step the tenant had not yet reached");

            // The real reproduction: write and read a record with an Arabic name through the same
            // service the recipe step uses, exactly the operation that failed in the field.
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            var type = await types.CreateAsync(
                "upgrade-test-division", new BilingualText("Upgrade Test Division", "قسم"), [], allowsSelfNesting: false);
            type.Succeeded.Should().BeTrue(string.Join("; ", type.Errors.Select(e => e.Message.Value)));

            var created = await records.CreateAsync(
                type.Value!.DimensionTypeId,
                "upgrade-test-div-1",
                new BilingualText("Upgrade Test Division One", "القسم الأول"),
                new EffectiveRange(new DateOnly(2024, 1, 1), null));

            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

            var reread = await records.GetByCodeAsync("upgrade-test-div-1");
            reread.Should().NotBeNull();
            reread!.NameAr.Should().Be("القسم الأول");
        });
    }

    /// <summary>
    /// For every index table this module defines, the live database columns on a freshly migrated
    /// tenant are exactly the public, settable properties the C# index class declares — no more,
    /// no less. This is the general safety net for the whole class of defect
    /// <see cref="ATenantStuckBeforeTheNameArColumnUpgradesCleanlyAndTheColumnBecomesUsable"/>
    /// reproduces for <c>DimensionRecordPartIndex.NameAr</c> specifically: if a future column is
    /// ever added to an index class without a matching migration step — append-only or not — a
    /// fresh tenant's schema stops matching the class, and this fails immediately rather than
    /// waiting for a tenant stuck on an old schema to hit it at runtime.
    /// </summary>
    [Theory]
    [MemberData(nameof(IndexTypes))]
    public async Task AFreshTenantsTableColumnsMatchTheIndexClassExactly(Type indexType)
    {
        await InTenantAsSystemAsync(async services =>
        {
            var store = services.GetRequiredService<IStore>();
            var connection = store.Configuration.ConnectionFactory.CreateConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            try
            {
                var tableName = store.Configuration.TablePrefix
                    + store.Configuration.TableNameConvention.GetIndexTable(indexType, string.Empty);

                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info('{tableName}')";

                var actualColumns = new List<string>();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        actualColumns.Add(reader.GetString(reader.GetOrdinal("name")));
                    }
                }

                // Id and DocumentId come from YesSql's MapIndex base, not from this module's own
                // properties; every column after them must be declared on the class itself.
                actualColumns.Should().Contain(["Id", "DocumentId"]);
                var actualOwnColumns = actualColumns.Except(["Id", "DocumentId"], StringComparer.Ordinal);

                var expectedColumns = indexType
                    .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(property => property.CanWrite && property.DeclaringType == indexType)
                    .Select(property => property.Name);

                actualOwnColumns.Should().BeEquivalentTo(expectedColumns,
                    $"{indexType.Name}'s table must have exactly the columns the class declares");
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        });
    }

    public static TheoryData<Type> IndexTypes() =>
    [
        typeof(DimensionTypeIndex),
        typeof(StructureIndex),
        typeof(DimensionRecordPartIndex),
        typeof(DimensionNameIndex),
        typeof(DimensionLinkIndex),
        typeof(DimensionClosureIndex),
        typeof(EmployeeAssignmentIndex),
    ];

    private static async Task RevertSchemaToVersion2Async(IServiceProvider services)
    {
        var store = services.GetRequiredService<IStore>();
        var connection = store.Configuration.ConnectionFactory.CreateConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
            string TableFor(Type indexType) =>
                store.Configuration.TablePrefix
                    + store.Configuration.TableNameConvention.GetIndexTable(indexType, string.Empty);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"ALTER TABLE {TableFor(typeof(DimensionRecordPartIndex))} DROP COLUMN NameAr";
                await command.ExecuteNonQueryAsync();
            }

            foreach (var indexType in new[] { typeof(DimensionLinkIndex), typeof(DimensionClosureIndex), typeof(EmployeeAssignmentIndex) })
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE {TableFor(indexType)}";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }
}
