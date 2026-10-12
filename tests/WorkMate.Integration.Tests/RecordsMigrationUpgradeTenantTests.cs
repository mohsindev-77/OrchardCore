using System.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Data.Migration;
using OrchardCore.Data.Migration.Records;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// ADR-0009's upgrade test for <c>WorkMate.Records</c>: a tenant stuck before a schema change
/// catches up on restart, and a fresh tenant's tables match the index classes that describe them.
/// </summary>
/// <remarks>
/// The rule is that every schema change ships with a test that builds the tables as the version
/// before it left them, runs the migration forward, and asserts the result matches a fresh install
/// — and then performs the real operation, not merely a check that a column exists. ADR-0009 was
/// written from a defect where the column existed on the class and not in one tenant's table, and
/// every test passed because every test builds a tenant from nothing on the current code.
///
/// <c>WorkMate.Dimensions</c> has carried this since the defect; this is the same pair of tests for
/// the module that has just grown its second table.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class RecordsMigrationUpgradeTenantTests
{
    private const string MigrationClass = "WorkMate.Records.Migrations";

    private readonly BaseTenantFixture _fixture;

    public RecordsMigrationUpgradeTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A tenant at version 3 — everything up to session A3, before lookup lists existed — upgrades
    /// and the table becomes usable.
    /// </summary>
    /// <remarks>
    /// The reproduction is deliberately faithful: roll the recorded version back <em>and</em> drop
    /// the physical table, because a real tenant of that vintage had neither. Rolling back only the
    /// number would leave the table in place, and <c>UpdateFrom3Async</c> would then fail on a
    /// table that already exists — which is a different defect from the one this guards, and one
    /// that would mask it.
    ///
    /// The assertion at the end is the point. It is not "the table exists": it creates and reads a
    /// list with a real Arabic name through <see cref="ILookupListService"/>, which is the
    /// operation a customer would perform and the one that would have thrown.
    /// </remarks>
    [Fact]
    public async Task ATenantStuckBeforeTheLookupListTableUpgradesAndTheTableBecomesUsable()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            record.Should().NotBeNull();

            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            // Whatever the head is today, not a literal. This test is about rolling back and
            // catching up; pinning the number would break it on every future step that has nothing
            // to say about it.
            var head = migration.Version;

            head.Should().BeGreaterThanOrEqualTo(4, "this test's premise is a schema that includes the lookup list table");

            migration.Version = 3;
            session.Save(record);
            await session.SaveChangesAsync();

            await DropLookupListTableAsync(services);
        });

        await InTenantAsSystemAsync(async services =>
        {
            // Exactly what restarting the application does, rather than a direct call to the step:
            // this exercises the same discovery-and-catch-up path a real restart uses.
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            migration.Version.Should().BeGreaterThanOrEqualTo(
                4, "the upgrade must run every step the tenant had not yet reached");

            var lists = services.GetRequiredService<ILookupListService>();

            var created = await lists.CreateAsync(
                "upgrade-test-grade",
                new BilingualText("Upgrade test grade", "درجة اختبار الترقية"),
                [new LookupListItem("G1", new BilingualText("Grade 1", "الدرجة ١"), SortOrder: 10)]);

            created.Succeeded.Should().BeTrue(created.Describe());

            var reread = await lists.GetByCodeAsync("upgrade-test-grade");

            reread.Should().NotBeNull();
            reread!.Name.Ar.Should().Be("درجة اختبار الترقية");
            reread.Items.Single().Name.Ar.Should().Be("الدرجة ١");
        });
    }

    /// <summary>
    /// Re-entering the step against a tenant that already has the table succeeds rather than
    /// aborting the whole feature's migration.
    /// </summary>
    /// <remarks>
    /// <b>This is the case that was found the hard way, and it is not hypothetical.</b> A recorded
    /// migration version can go backwards: an operator repairing a half-migrated tenant, a restore
    /// that brings back an older migration document beside a newer schema, or — how this surfaced —
    /// another test rolling this module back to version 1 to exercise the display-text repair in
    /// <c>UpdateFrom1Async</c>. The runner then re-runs every step from there, including this one.
    ///
    /// Without the guard, <c>CreateMapIndexTableAsync</c> throws on a table that already exists and
    /// Orchard abandons the rest of the feature's migration. The symptom is the worst kind: the
    /// failure appears in whichever test or screen next depends on a step that never ran, a long
    /// way from the step that actually threw.
    ///
    /// The version is restored afterwards, because this collection shares one tenant and leaving it
    /// rolled back would hand the next test a database it did not ask for.
    /// </remarks>
    [Fact]
    public async Task TheStepIsSafeToReEnterWhenTheTableIsAlreadyThere()
    {
        int? head = null;

        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            head = migration.Version;

            // Below the step, and deliberately without dropping the table: this is the state a
            // rolled-back version leaves, as distinct from the never-migrated state above.
            migration.Version = 1;
            session.Save(record);
            await session.SaveChangesAsync();
        });

        var failure = await Record.ExceptionAsync(() => InTenantAsSystemAsync(async services =>
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync()));

        failure.Should().BeNull("a step that creates a table must survive being re-entered");

        await InTenantAsSystemAsync(async services =>
        {
            var record = await services.GetRequiredService<ISession>()
                .Query<DataMigrationRecord>().FirstOrDefaultAsync();

            record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass).Version
                .Should().Be(head, "every step from 1 upwards ran again and reached the head");

            // And the table is still usable, rather than present and somehow damaged.
            var lists = services.GetRequiredService<ILookupListService>();

            var created = await lists.CreateAsync(
                "reentry-test",
                new BilingualText("Re-entry test", "اختبار إعادة الدخول"),
                []);

            created.Succeeded.Should().BeTrue(created.Describe());
        });
    }

    /// <summary>
    /// For every index table this module defines, a freshly migrated tenant's columns are exactly
    /// the public settable properties the C# class declares — no more, no less.
    /// </summary>
    /// <remarks>
    /// The general safety net rather than a reproduction of one defect: if a column is ever added
    /// to an index class without a matching migration step, a fresh tenant's schema stops matching
    /// the class and this fails immediately, rather than waiting for a tenant stuck on an old
    /// schema to hit it at runtime.
    /// </remarks>
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

                actualOwnColumns.Should().BeEquivalentTo(
                    expectedColumns,
                    $"{indexType.Name}'s table must have exactly the columns the class declares");
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        });
    }

    /// <summary>Every index this module defines, found rather than listed.</summary>
    /// <remarks>
    /// By reflection so that an index added without a line in this file is still covered. A list of
    /// examples is a list somebody has to remember to add to, which is the failure mode the whole
    /// of ADR-0009 is about.
    /// </remarks>
    public static TheoryData<Type> IndexTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(EmployeeIndex).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(YesSql.Indexes.MapIndex).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    /// <summary>
    /// Removes the table as a tenant that had never run <c>UpdateFrom3Async</c> would have it.
    /// </summary>
    /// <remarks>
    /// Raw SQL, which CLAUDE.md permits inside a migration or a clearly named maintenance command —
    /// and, by the same reasoning, in the test whose whole job is to reproduce a schema state the
    /// migration API cannot express. YesSql's schema builder can drop a table, but it would do so
    /// through the same migration machinery this test is trying to put back into the past.
    /// </remarks>
    private static async Task DropLookupListTableAsync(IServiceProvider services)
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
                + store.Configuration.TableNameConvention.GetIndexTable(typeof(LookupListIndex), string.Empty);

            using var command = connection.CreateCommand();

            // The table name is built from nameof-derived metadata, never from input.
            command.CommandText = $"DROP TABLE IF EXISTS '{tableName}'";

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("records migration upgrade test"))
            {
                await work(services);
            }
        });
}
