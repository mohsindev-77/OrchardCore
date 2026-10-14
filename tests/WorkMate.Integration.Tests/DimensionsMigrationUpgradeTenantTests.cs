using System.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Data.Migration;
using OrchardCore.Data.Migration.Records;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Internal.Graph;
using WorkMate.Dimensions.Models;
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

            // A tenant at version 4 does not have the tables the later steps create, and the catch-up
            // re-runs those steps. See TablesByVersion.
            await DropTablesCreatedAfterAsync(services, 4);

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
        typeof(UnitHeadIndex),
    ];

    /// <summary>
    /// A tenant that predates the unit head catches up, and the table becomes usable.
    /// </summary>
    /// <remarks>
    /// ADR-0012 added <c>UnitHeadIndex</c> in <c>UpdateFrom5Async</c> rather than by editing
    /// <see cref="WorkMate.Dimensions.Migrations.UpdateFrom2Async"/>, which is where the other three
    /// graph tables were created. That is the append-only rule, and the reason it exists is in
    /// <see cref="ATenantStuckBeforeTheNameArColumnUpgradesCleanlyAndTheColumnBecomesUsable"/>: a
    /// tenant already past a shipped step never runs it again, so an edit to it reaches nobody who
    /// has already upgraded.
    ///
    /// Like that test, this does not stop at "the table exists". It appoints a head through the same
    /// service the designer and the recipe step use and reads the appointment back, which is the
    /// operation that would throw "no such table" on a tenant the migration had missed.
    /// </remarks>
    [Fact]
    public async Task ATenantStuckBeforeTheUnitHeadTableUpgradesCleanlyAndCanAppointAHead()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);
            var head = migration.Version;

            head.Should().BeGreaterThanOrEqualTo(
                6, "this test's premise is a schema that already includes the unit head table");

            migration.Version = 5;
            session.Save(record);
            await session.SaveChangesAsync();

            // Version 5 means UpdateFrom5Async has never run, so a real tenant at that version has
            // no UnitHeadIndex table at all. Dropping it is what makes this the same state rather
            // than a tenant with the table and a lower version number — and re-running
            // CreateMapIndexTableAsync over an existing table would fail for a reason that has
            // nothing to do with the defect being reproduced.
            await DropTablesCreatedAfterAsync(services, 5);

            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();

            var after = (await session.Query<DataMigrationRecord>().FirstOrDefaultAsync())!
                .DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            after.Version.Should().Be(head, "the upgrade runs every step the tenant had not reached");
        });

        // A second scope, because the migration above committed its schema change in the first one
        // and the appointment has to be written through a session that opened after it.
        await InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "upgrade-head");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var opened = new DateOnly(2024, 1, 1);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "upgrade-head-dep", opened);
            var employee = await EmployeeScenario.ActiveEmployeeAsync(services, "upgrade-head-emp");

            var appointed = await assignments.SetHeadAsync(structure, department, employee, opened);

            appointed.Succeeded.Should().BeTrue(
                string.Join("; ", appointed.Errors.Select(error => error.Message.Value)));

            (await assignments.GetHeadAsync(structure, department, opened))!
                .EmployeeId.Should().Be(employee);
        });
    }


    /// <summary>
    /// A tenant that predates the alias columns catches up, and they become usable.
    /// </summary>
    /// <remarks>
    /// The third of the append-only upgrade tests, and the same shape as the two above it: roll the
    /// recorded version back, put the schema into the state a tenant at that version really had,
    /// let the application catch up, and then do the real operation through the real service rather
    /// than checking that a column exists.
    ///
    /// <b>What a tenant at version 9 actually looks like.</b> It has
    /// <c>DimensionRecordPartIndex</c> — <c>UpdateFrom1Async</c> created it — and does not have
    /// <c>AliasEn</c> or <c>AliasAr</c>, because nothing before <c>UpdateFrom9Async</c> creates
    /// them. That is also exactly where a brand-new tenant arrives, which is why that step adds the
    /// columns unguarded by the "is this a fresh tenant or an upgrading one" question
    /// <c>UpdateFrom3Async</c> and <c>UpdateFrom4Async</c> both have to ask.
    ///
    /// The operation at the end is the one that would throw <c>SQLite Error 1: 'table
    /// DimensionRecordPartIndex has no column named AliasEn'</c> on a tenant the migration had
    /// missed — the same failure, on the same table, that ADR-0009 exists because of.
    /// </remarks>
    [Fact]
    public async Task ATenantStuckBeforeTheAliasColumnsUpgradesCleanlyAndCanStoreAnAlias()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);
            var head = migration.Version;

            head.Should().BeGreaterThanOrEqualTo(
                10, "this test's premise is a schema that already includes the alias columns");

            migration.Version = 9;
            session.Save(record);
            await session.SaveChangesAsync();

            await DropAliasColumnsAsync(services);

            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();

            var after = (await session.Query<DataMigrationRecord>().FirstOrDefaultAsync())!
                .DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            after.Version.Should().Be(head, "the upgrade runs every step the tenant had not reached");
        });

        // A second scope, because the migration above committed its schema change in the first one
        // and the record has to be written through a session that opened after it.
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            var type = await types.CreateAsync(
                "upgrade-alias-division", new BilingualText("Upgrade Alias Division", "قسم"), [], allowsSelfNesting: false);

            type.Succeeded.Should().BeTrue(string.Join("; ", type.Errors.Select(e => e.Message.Value)));

            var created = await records.CreateAsync(
                type.Value!.DimensionTypeId,
                "upgrade-alias-div-1",
                new BilingualText("Upgrade Alias Division One", "القسم الأول"),
                new EffectiveRange(new DateOnly(2024, 1, 1), null),
                batch: null,
                sortOrder: null,
                new BilingualText("Head Office", "المكتب الرئيسي"));

            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

            // Read back through the index, which is the path that needs the columns: GetByCodeAsync
            // resolves a record from DimensionRecordPartIndex without loading the content item.
            var reread = await records.GetByCodeAsync("upgrade-alias-div-1");

            reread.Should().NotBeNull();
            reread!.AliasEn.Should().Be("Head Office");
            reread.AliasAr.Should().Be("المكتب الرئيسي");
        });
    }

    /// <summary>
    /// ADR-0024: a tenant that upgraded through the withdrawn per-type menu has its guessed plurals
    /// replaced by the singular, and anything a person authored is left alone.
    /// </summary>
    /// <remarks>
    /// <b>The defect this guards.</b> <c>UpdateFrom7Async</c> shipped with the per-type menu and
    /// wrote a plural onto every dimension type a tenant had: the English name plus an "s", and the
    /// Arabic name unchanged. The menu was withdrawn, the fields went unread, and FRS v2.5 brought
    /// the menu back. Reading the stored value without <c>UpdateFrom10Async</c> would put
    /// <em>Branchs</em> back in the menu of every tenant that had upgraded through version 8 — the
    /// exact word that got the feature pulled.
    ///
    /// <b>Both halves are asserted, and the second is the one worth having.</b> A guess is replaced;
    /// a plural that differs from what the frozen derivation would have produced is something
    /// somebody typed on session D's editor before it was withdrawn, and survives. A migration that
    /// cleared both would be destroying data, which ADR-0009 forbids.
    ///
    /// Not a schema change, so there is no column to drop: this rolls the recorded version back and
    /// writes the documents into the state <c>UpdateFrom7Async</c> left them in, which is what a
    /// tenant at version 10 actually looks like.
    /// </remarks>
    [Fact]
    public async Task ATenantWithGuessedPluralsHasThemReplacedAndAuthoredOnesLeftAlone()
    {
        const string guessedCode = "plural-guessed-branch";
        const string authoredCode = "plural-authored-branch";

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            foreach (var code in new[] { guessedCode, authoredCode })
            {
                var created = await types.CreateAsync(
                    code, new BilingualText("Branch", "فرع"), [], allowsSelfNesting: false);

                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
            }
        });

        // The state UpdateFrom7Async left a tenant in: one type carrying its derived plural, one
        // carrying a plural a person typed on the editor that shipped with it.
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            foreach (var document in await session.Query<DimensionTypeDocument, DimensionTypeIndex>().ListAsync())
            {
                if (document.Code == guessedCode)
                {
                    document.PluralName = DimensionTypeMenu.DefaultFor(document.Name).PluralName;
                }
                else if (document.Code == authoredCode)
                {
                    document.PluralName = new BilingualText("Branches", "الفروع");
                }
                else
                {
                    continue;
                }

                await session.SaveCheckedAsync(document);
            }

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            migration.Version.Should().BeGreaterThanOrEqualTo(
                11, "this test's premise is a schema that already includes the plural repair");

            migration.Version = 10;
            session.Save(record);
            await session.SaveChangesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var guessed = await types.GetByCodeAsync(guessedCode);

            // The chain, not one step: a tenant rolled back to 10 runs UpdateFrom10 and then
            // UpdateFrom11. The first replaces the guess with the SINGULAR — never leaving
            // "Branchs" — and the second recognises that singular as a plural nobody authored and
            // fills it from the shipped table (D24). What a tenant ends up with is the right word.
            guessed!.PluralName.En.Should().Be("Branches", "and never Branchs, which is what got the menu pulled");
            guessed.PluralName.Ar.Should().Be("الفروع");
            guessed.MenuCaption(System.Globalization.CultureInfo.InvariantCulture).Should().Be("Branches");

            var authored = await types.GetByCodeAsync(authoredCode);

            authored!.PluralName.En.Should().Be(
                "Branches", "a plural somebody typed is data, and a migration never destroys data");
            authored.PluralName.Ar.Should().Be("الفروع");
        });
    }

    /// <summary>
    /// Decision D24: a tenant whose dimensions have no plural gets the shipped ones, and anything a
    /// person authored survives.
    /// </summary>
    /// <remarks>
    /// <b>The complaint this answers.</b> A tenant created before the dimension type editor had a
    /// plural field reads <em>Department</em>, <em>Division</em> and <em>All Department</em> all the
    /// way down its Organisation menu. Every one of those is the correct fallback and every one of
    /// them looks wrong, and asking a customer to edit twelve types by hand to fix a regression
    /// they did not cause is not a migration strategy.
    ///
    /// <b>Three cases, because the third is the one a careless version would get wrong.</b> A blank
    /// plural is filled. A plural that is the singular repeated is also filled — that is what
    /// <c>UpdateFrom10Async</c> wrote as a placeholder, so it is the state every upgraded tenant is
    /// actually in, and a step that only filled blanks would have reached none of them. A plural
    /// somebody typed is left exactly as it is, however odd, because ADR-0009 does not let a
    /// migration overwrite data.
    ///
    /// <b>And a fourth: a name the product does not ship gets nothing.</b> Nothing here derives a
    /// word — the whole of ADR-0024 — so a Workstream stays without a plural and the menu phrases
    /// itself around the singular instead.
    /// </remarks>
    [Fact]
    public async Task ATenantWithNoPluralsGetsTheShippedOnesAndKeepsWhatWasAuthored()
    {
        const string blankCode = "d24-blank-division";
        const string placeholderCode = "d24-placeholder-department";
        const string authoredCode = "d24-authored-branch";
        const string unknownCode = "d24-unknown-workstream";

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var names = new Dictionary<string, BilingualText>(StringComparer.Ordinal)
            {
                [blankCode] = new BilingualText("Division", "قطاع"),
                [placeholderCode] = new BilingualText("Department", "إدارة"),
                [authoredCode] = new BilingualText("Branch", "فرع"),
                [unknownCode] = new BilingualText("Workstream", "مسار العمل"),
            };

            foreach (var (code, name) in names)
            {
                var created = await types.CreateAsync(code, name, [], allowsSelfNesting: false);

                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
            }
        });

        // The three states a tenant can be in when it reaches this step.
        await InTenantAsSystemAsync(async services =>
        {
            var session = services.GetRequiredService<ISession>();

            foreach (var document in await session.Query<DimensionTypeDocument, DimensionTypeIndex>().ListAsync())
            {
                switch (document.Code)
                {
                    case blankCode:
                    case unknownCode:
                        // Never edited: created before the field existed.
                        document.PluralName = new BilingualText(string.Empty, string.Empty);
                        break;

                    case placeholderCode:
                        // What UpdateFrom10Async writes, and so what every upgraded tenant holds.
                        document.PluralName = new BilingualText(document.Name.En, document.Name.Ar);
                        break;

                    case authoredCode:
                        document.PluralName = new BilingualText("Outlets", "المنافذ");
                        break;

                    default:
                        continue;
                }

                await session.SaveCheckedAsync(document);
            }

            var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
            var migration = record!.DataMigrations.Single(m => m.DataMigrationClass == MigrationClass);

            migration.Version.Should().BeGreaterThanOrEqualTo(
                12, "this test's premise is a schema that already includes the standard plurals");

            migration.Version = 11;
            session.Save(record);
            await session.SaveChangesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            await services.GetRequiredService<IDataMigrationManager>().UpdateAllFeaturesAsync();
        });

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var blank = await types.GetByCodeAsync(blankCode);

            blank!.PluralName.En.Should().Be("Divisions", "a blank plural is filled from the shipped table");
            blank.PluralName.Ar.Should().Be("القطاعات", "and the Arabic half from the Arabic name");

            var placeholder = await types.GetByCodeAsync(placeholderCode);

            placeholder!.PluralName.En.Should().Be(
                "Departments", "the singular repeated is a placeholder, not a word anybody chose");
            placeholder.PluralName.Ar.Should().Be("الإدارات");

            var authored = await types.GetByCodeAsync(authoredCode);

            authored!.PluralName.En.Should().Be("Outlets", "a migration never overwrites what a person typed");
            authored.PluralName.Ar.Should().Be("المنافذ");

            var unknown = await types.GetByCodeAsync(unknownCode);

            unknown!.PluralName.En.Should().BeEmpty("nothing derives a plural for a name the product does not ship");
            unknown.PluralName.Ar.Should().BeEmpty();
        });
    }

    /// <summary>
    /// Puts <c>DimensionRecordPartIndex</c> back into the state a tenant recorded at version 9 was
    /// in: the table, without the two alias columns.
    /// </summary>
    private static async Task DropAliasColumnsAsync(IServiceProvider services)
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
                + store.Configuration.TableNameConvention.GetIndexTable(
                    typeof(DimensionRecordPartIndex), string.Empty);

            foreach (var column in new[]
            {
                nameof(DimensionRecordPartIndex.AliasEn),
                nameof(DimensionRecordPartIndex.AliasAr),
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE {tableName} DROP COLUMN {column}";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// The index tables this module creates, and the schema version each one first appears at.
    /// </summary>
    /// <remarks>
    /// Rolling the recorded version back to N is only half of simulating a tenant at version N: that
    /// tenant also does not have the tables the steps above N create, and
    /// <c>CreateMapIndexTableAsync</c> fails on one that is already there. When it fails the whole
    /// catch-up fails, which rolls back the document rewrites <c>UpdateFrom4Async</c> had already
    /// made and leaves the shared tenant stuck below head — so the test that forgot it fails, and so
    /// does every test in the collection that runs after it.
    ///
    /// <b>A new <c>UpdateFromNAsync</c> that creates a table has to be added here.</b> This map is
    /// the one place that knowledge lives, so adding a row is the whole of it.
    /// </remarks>
    private static readonly IReadOnlyList<(Type IndexType, int FirstVersion)> TablesByVersion =
    [
        (typeof(DimensionLinkIndex), 3),
        (typeof(DimensionClosureIndex), 3),
        (typeof(EmployeeAssignmentIndex), 3),
        (typeof(UnitHeadIndex), 6),
    ];

    /// <summary>
    /// Drops every index table a tenant recorded at <paramref name="version"/> would not yet have.
    /// </summary>
    private static async Task DropTablesCreatedAfterAsync(IServiceProvider services, int version)
    {
        var store = services.GetRequiredService<IStore>();
        var connection = store.Configuration.ConnectionFactory.CreateConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
            foreach (var (indexType, _) in TablesByVersion.Where(entry => entry.FirstVersion > version))
            {
                var tableName = store.Configuration.TablePrefix
                    + store.Configuration.TableNameConvention.GetIndexTable(indexType, string.Empty);

                using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {tableName}";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }

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

        }
        finally
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }

        // Everything the steps above version 2 create. Kept in one place — see TablesByVersion —
        // because the failure when a new table is missed from it is loud and misleading: this test
        // fails on "table already exists", and then so does every other test in the collection,
        // because the tenant they share is left stuck below head with a half-reverted schema.
        await DropTablesCreatedAfterAsync(services, 2);
    }
}
