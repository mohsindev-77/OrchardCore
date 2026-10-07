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

            // Whatever the head is today, not a literal. This test is about rolling back to
            // version 2 and catching up again; pinning the number here only means that every
            // future UpdateFromNAsync breaks a test that has nothing to say about it.
            var head = migration.Version;

            head.Should().BeGreaterThanOrEqualTo(
                4, "this test's premise is a schema that already includes the NameAr fix");

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
            migrationAfterUpgrade.Version.Should().Be(head, "the upgrade must run every step the tenant had not yet reached");

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
    /// ADR-0010's upgrade, proved lossless: for every ordered pair of a structure's types, the
    /// derived containment map answers exactly what the level arithmetic it replaced answered.
    /// </summary>
    /// <remarks>
    /// The whole claim the migration rests on. An upgrade that widened the rules would silently
    /// permit placements a customer had deliberately forbidden; one that narrowed them would make
    /// live placements invalid and the next edit of that structure refuse to save. Neither shows
    /// up as an error at upgrade time, so the only way to know is to ask both implementations the
    /// same question about every pair.
    ///
    /// Run for both values of the flag, because the two halves of the old rule — adjacent-only and
    /// anything-below — are different derivations and only one of them is exercised by either.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheDerivedContainmentMapPermitsExactlyWhatTheLevelArithmeticDid(bool allowSkipLevel)
    {
        var code = $"upgrade-containment-{allowSkipLevel}";
        var levelTypeIds = new List<string>();
        var selfNesting = new HashSet<string>(StringComparer.Ordinal);

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            // Four types, one of which nests inside itself: enough for adjacent pairs, skipped
            // pairs and a self-pair to all be distinguishable in the result.
            foreach (var (suffix, nests) in new[] { ("a", false), ("b", false), ("c", true), ("d", false) })
            {
                var created = await types.CreateAsync(
                    $"{code}-{suffix}", new BilingualText($"{code}-{suffix}", "ت"), [], nests);

                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
                levelTypeIds.Add(created.Value!.DimensionTypeId);

                if (nests)
                {
                    selfNesting.Add(created.Value.DimensionTypeId);
                }
            }

            // A structure exactly as version 4 left one: levels and the flag, and no map at all.
            // Built by hand rather than through IStructureService, which now always writes one.
            var session = services.GetRequiredService<ISession>();

            await session.SaveAsync(new WorkMate.Dimensions.Models.StructureDocument
            {
                StructureId = code,
                Code = code,
                Name = new BilingualText(code, code),
                Levels = [.. levelTypeIds.Select((id, ordinal) => new WorkMate.Dimensions.Models.StructureLevel(ordinal, id))],
                AllowSkipLevel = allowSkipLevel,
                IsStrict = true,
            });

            await session.SaveChangesAsync();
        });

        // Roll the recorded version back one step and let the application catch up, which is what
        // a real tenant upgrading into this release does.
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();
            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            migration.Version = 4;
            session.Save(record);
            await session.SaveChangesAsync();

            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            var upgraded = await services.GetRequiredService<IStructureService>().GetByCodeAsync(code);

            upgraded.Should().NotBeNull();
            upgraded!.RootDimensionTypeIds.Should().Equal([levelTypeIds[0]], "the chain's root was its first level");

            var differences = new List<string>();

            foreach (var parent in levelTypeIds)
            {
                foreach (var child in levelTypeIds)
                {
                    // Version 4's rule, written out: at or below is refused, more than one level
                    // apart is refused unless skipping was on, and same-type is the type's flag.
                    var parentOrdinal = levelTypeIds.IndexOf(parent);
                    var childOrdinal = levelTypeIds.IndexOf(child);

                    var before = parent == child
                        ? selfNesting.Contains(child)
                        : childOrdinal > parentOrdinal &&
                            (childOrdinal - parentOrdinal == 1 || allowSkipLevel);

                    // The map, and only the map. This clause used to read
                    // "&& (parent != child || selfNesting.Contains(child))", because the dimension
                    // type held a veto over its own diagonal that was applied on top of whatever
                    // the structure said. ADR-0010's addendum removed that second authority, so
                    // the question is now whether the derived map on its own still permits exactly
                    // what version 4's arithmetic plus that veto did — diagonal included. It does,
                    // because the derivation wrote an X → X pair for precisely the types whose flag
                    // was on, which is what makes the addendum a change of authority rather than a
                    // change of behaviour for any tenant that already upgraded.
                    var after = upgraded.Permits(parent, child);

                    if (before != after)
                    {
                        differences.Add($"{parentOrdinal} > {childOrdinal}: was {before}, is {after}");
                    }
                }
            }

            differences.Should().BeEmpty(
                "the upgrade must neither widen nor narrow what the structure permitted");
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
