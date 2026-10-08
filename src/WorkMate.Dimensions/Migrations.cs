using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Data.Migration;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Internal.Graph;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using YesSql;
using YesSql.Sql;

namespace WorkMate.Dimensions;

/// <summary>
/// Schema for WorkMate.Dimensions. Migrations are additive: a migration never destroys data, and
/// a deprecation is a new migration that marks and later removes.
/// </summary>
/// <remarks>
/// Only the configuration layer's tables are here. The record part definition and the three
/// graph tables arrive in the migrations that land with those layers, each as its own
/// <c>UpdateFromNAsync</c>, so that a tenant created between the two gets the second by upgrade
/// rather than by rebuild.
///
/// Every date column is a <c>DateTime</c> rather than a <c>DateOnly</c>: YesSql 5.4.7 has no
/// mapping for <c>DateOnly</c> and the schema builder throws on one. ADR-0005 records why, and
/// <see cref="EffectiveDates"/> is the only code that converts.
/// </remarks>
public sealed class Migrations : DataMigration
{
    /// <summary>Column length for a generated identifier, matching Orchard's own content item ids.</summary>
    private const int IdentifierLength = 26;

    /// <summary>Column length for a customer-facing code; <c>DimensionCodes</c> enforces the same bound.</summary>
    private const int CodeLength = 50;

    /// <summary>Column length for a derived content type name, which cannot exceed the code it comes from.</summary>
    private const int ContentTypeNameLength = 50;

    /// <summary>
    /// Column length for a name. Indexed, so it is bounded rather than unlimited: several
    /// providers refuse to index a column wider than a page, and a unit name that needs more
    /// than this is a description.
    /// </summary>
    private const int NameLength = 255;

    private readonly IContentDefinitionManager _contentDefinitionManager;

    /// <summary>
    /// For the one step that migrates data rather than schema. <see cref="UpdateFrom4Async"/> has
    /// to read and rewrite every structure document, which <c>SchemaBuilder</c> cannot do.
    /// </summary>
    private readonly ISession _session;

    public Migrations(IContentDefinitionManager contentDefinitionManager, ISession session)
    {
        _contentDefinitionManager = contentDefinitionManager;
        _session = session;
    }

    public async Task<int> CreateAsync()
    {
        await SchemaBuilder.CreateMapIndexTableAsync<DimensionTypeIndex>(table => table
            .Column<string>(nameof(DimensionTypeIndex.DimensionTypeId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionTypeIndex.Code), column => column.WithLength(CodeLength))
            .Column<string>(nameof(DimensionTypeIndex.ContentTypeName), column => column.WithLength(ContentTypeNameLength))
            .Column<bool>(nameof(DimensionTypeIndex.IsSystemDefined))
            .Column<bool>(nameof(DimensionTypeIndex.AllowsSelfNesting))
            .Column<DateTime>(nameof(DimensionTypeIndex.RetiredOn), column => column.Nullable()));

        await SchemaBuilder.AlterIndexTableAsync<DimensionTypeIndex>(table =>
        {
            // Resolving a type by code happens on every record write and every recipe import
            // row; resolving by id happens on every structure read.
            table.CreateIndex(
                $"IDX_{nameof(DimensionTypeIndex)}_Code",
                nameof(DimensionTypeIndex.Code));

            table.CreateIndex(
                $"IDX_{nameof(DimensionTypeIndex)}_DimensionTypeId",
                nameof(DimensionTypeIndex.DimensionTypeId));

            // The record handler asks "is this content type one of ours" for every content item
            // saved on the tenant, including items that have nothing to do with this module.
            table.CreateIndex(
                $"IDX_{nameof(DimensionTypeIndex)}_ContentTypeName",
                nameof(DimensionTypeIndex.ContentTypeName));
        });

        await SchemaBuilder.CreateMapIndexTableAsync<StructureIndex>(table => table
            .Column<string>(nameof(StructureIndex.StructureId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(StructureIndex.Code), column => column.WithLength(CodeLength))
            .Column<bool>(nameof(StructureIndex.IsPrimaryOrganisation))
            .Column<bool>(nameof(StructureIndex.AllowSkipLevel))
            .Column<bool>(nameof(StructureIndex.IsStrict))
            .Column<int>(nameof(StructureIndex.LevelCount))
            // Added for a new tenant here and for an existing one in UpdateFrom4Async, per the
            // append-only rule: CreateAsync keeps producing the current schema, it never carries
            // the upgrade.
            .Column<int>(nameof(StructureIndex.ContainmentRuleCount)));

        await SchemaBuilder.AlterIndexTableAsync<StructureIndex>(table =>
        {
            table.CreateIndex(
                $"IDX_{nameof(StructureIndex)}_Code",
                nameof(StructureIndex.Code));

            table.CreateIndex(
                $"IDX_{nameof(StructureIndex)}_StructureId",
                nameof(StructureIndex.StructureId));

            // Approvals and data visibility resolve the primary organisation axis on almost
            // every request, so it gets its own index rather than a scan of a small table that
            // will not stay small on a tenant with many axes.
            table.CreateIndex(
                $"IDX_{nameof(StructureIndex)}_IsPrimaryOrganisation",
                nameof(StructureIndex.IsPrimaryOrganisation));
        });

        return 1;
    }

    /// <summary>
    /// The record layer: the shared part definition, the record index and the dated name
    /// history.
    /// </summary>
    /// <remarks>
    /// A separate migration rather than an edit to <see cref="CreateAsync"/>, so that a tenant
    /// created between the two releases is brought forward by upgrade rather than needing a
    /// rebuild. Additive: it creates, and destroys nothing.
    /// </remarks>
    public async Task<int> UpdateFrom1Async()
    {
        // The part itself. Declared here rather than by the service that attaches it, because a
        // part definition is schema: it is the same on every tenant and it exists whether or not
        // any dimension type has been created yet.
        await _contentDefinitionManager.AlterPartDefinitionAsync(
            DimensionTypeService.DimensionRecordPartName,
            part => part
                .Attachable()
                .WithDescription(
                    "The standard fields every dimension record carries: code, bilingual name, "
                    + "dimension type, effective dates, active flag, sort order, cost centre, "
                    + "GL account and head of unit."));

        await SchemaBuilder.CreateMapIndexTableAsync<DimensionRecordPartIndex>(table => table
            .Column<string>(nameof(DimensionRecordPartIndex.ContentItemId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionRecordPartIndex.ContentType), column => column.WithLength(ContentTypeNameLength))
            .Column<string>(nameof(DimensionRecordPartIndex.Code), column => column.WithLength(CodeLength))
            .Column<string>(nameof(DimensionRecordPartIndex.DimensionTypeId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionRecordPartIndex.NameEn), column => column.WithLength(NameLength))
            .Column<string>(nameof(DimensionRecordPartIndex.NameAr), column => column.WithLength(NameLength))
            .Column<bool>(nameof(DimensionRecordPartIndex.IsActive))
            .Column<int>(nameof(DimensionRecordPartIndex.SortOrder))
            .Column<DateTime>(nameof(DimensionRecordPartIndex.EffectiveFrom))
            .Column<DateTime>(nameof(DimensionRecordPartIndex.EffectiveToInclusive))
            .Column<bool>(nameof(DimensionRecordPartIndex.Latest))
            .Column<bool>(nameof(DimensionRecordPartIndex.Published)));

        await SchemaBuilder.AlterIndexTableAsync<DimensionRecordPartIndex>(table =>
        {
            // Code uniqueness is checked on every record write, including every row of an
            // import, so it is the hottest lookup on this table.
            table.CreateIndex(
                $"IDX_{nameof(DimensionRecordPartIndex)}_Code",
                nameof(DimensionRecordPartIndex.Code));

            table.CreateIndex(
                $"IDX_{nameof(DimensionRecordPartIndex)}_ContentItemId",
                nameof(DimensionRecordPartIndex.ContentItemId));

            // "Every live record of this type, as at this date" — what a picker and the
            // designer's tree both ask. The column order is the order the query filters in.
            table.CreateIndex(
                $"IDX_{nameof(DimensionRecordPartIndex)}_TypeAndDates",
                nameof(DimensionRecordPartIndex.DimensionTypeId),
                nameof(DimensionRecordPartIndex.EffectiveFrom),
                nameof(DimensionRecordPartIndex.EffectiveToInclusive));
        });

        await SchemaBuilder.CreateMapIndexTableAsync<DimensionNameIndex>(table => table
            .Column<string>(nameof(DimensionNameIndex.RecordId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionNameIndex.NameEn), column => column.WithLength(NameLength))
            .Column<string>(nameof(DimensionNameIndex.NameAr), column => column.WithLength(NameLength))
            .Column<DateTime>(nameof(DimensionNameIndex.EffectiveFrom))
            .Column<DateTime>(nameof(DimensionNameIndex.EffectiveToInclusive)));

        await SchemaBuilder.AlterIndexTableAsync<DimensionNameIndex>(table =>
            // "What was this called on that date" — one row, from one indexed seek. A historical
            // report asks it once per node it prints.
            table.CreateIndex(
                $"IDX_{nameof(DimensionNameIndex)}_RecordAndDates",
                nameof(DimensionNameIndex.RecordId),
                nameof(DimensionNameIndex.EffectiveFrom),
                nameof(DimensionNameIndex.EffectiveToInclusive)));

        return 2;
    }

    /// <summary>
    /// The graph layer: links, the dated closure index, and employee assignments.
    /// </summary>
    /// <remarks>
    /// ADR-0005 is the contract for these three. The closure table carries an effective range as
    /// well as a depth, which is where this departs from the architecture's four-column
    /// description, and the index column order below is the order the dated descendant query
    /// filters in — structure, ancestor, then the range — because that query is the one the
    /// 200 ms acceptance criterion is about.
    /// </remarks>
    public async Task<int> UpdateFrom2Async()
    {
        await SchemaBuilder.CreateMapIndexTableAsync<DimensionLinkIndex>(table => table
            .Column<string>(nameof(DimensionLinkIndex.StructureId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionLinkIndex.ChildId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionLinkIndex.ParentId), column => column.WithLength(IdentifierLength))
            .Column<DateTime>(nameof(DimensionLinkIndex.EffectiveFrom))
            .Column<DateTime>(nameof(DimensionLinkIndex.EffectiveToInclusive)));

        await SchemaBuilder.AlterIndexTableAsync<DimensionLinkIndex>(table =>
        {
            table.CreateIndex(
                $"IDX_{nameof(DimensionLinkIndex)}_Child",
                nameof(DimensionLinkIndex.StructureId),
                nameof(DimensionLinkIndex.ChildId));

            // Walking down to find a moved subtree, and the verification walk.
            table.CreateIndex(
                $"IDX_{nameof(DimensionLinkIndex)}_Parent",
                nameof(DimensionLinkIndex.StructureId),
                nameof(DimensionLinkIndex.ParentId));
        });

        await SchemaBuilder.CreateMapIndexTableAsync<DimensionClosureIndex>(table => table
            .Column<string>(nameof(DimensionClosureIndex.StructureId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionClosureIndex.AncestorId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(DimensionClosureIndex.DescendantId), column => column.WithLength(IdentifierLength))
            .Column<int>(nameof(DimensionClosureIndex.Depth))
            .Column<DateTime>(nameof(DimensionClosureIndex.EffectiveFrom))
            .Column<DateTime>(nameof(DimensionClosureIndex.EffectiveToInclusive)));

        await SchemaBuilder.AlterIndexTableAsync<DimensionClosureIndex>(table =>
        {
            // "Everyone under this node, as at this date" — the hot path, and the one the
            // performance gate is written against.
            table.CreateIndex(
                $"IDX_{nameof(DimensionClosureIndex)}_Descendants",
                nameof(DimensionClosureIndex.StructureId),
                nameof(DimensionClosureIndex.AncestorId),
                nameof(DimensionClosureIndex.EffectiveFrom),
                nameof(DimensionClosureIndex.EffectiveToInclusive));

            // "Everyone above this node" — approvals walking up a chain.
            table.CreateIndex(
                $"IDX_{nameof(DimensionClosureIndex)}_Ancestors",
                nameof(DimensionClosureIndex.StructureId),
                nameof(DimensionClosureIndex.DescendantId),
                nameof(DimensionClosureIndex.EffectiveFrom),
                nameof(DimensionClosureIndex.EffectiveToInclusive));
        });

        await SchemaBuilder.CreateMapIndexTableAsync<EmployeeAssignmentIndex>(table => table
            .Column<string>(nameof(EmployeeAssignmentIndex.EmployeeId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(EmployeeAssignmentIndex.StructureId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(EmployeeAssignmentIndex.NodeId), column => column.WithLength(IdentifierLength))
            .Column<decimal>(nameof(EmployeeAssignmentIndex.AllocationPercent))
            .Column<bool>(nameof(EmployeeAssignmentIndex.IsPrimary))
            .Column<DateTime>(nameof(EmployeeAssignmentIndex.EffectiveFrom))
            .Column<DateTime>(nameof(EmployeeAssignmentIndex.EffectiveToInclusive)));

        await SchemaBuilder.AlterIndexTableAsync<EmployeeAssignmentIndex>(table =>
        {
            // The outer half of "every employee under this node as at a date": filter by
            // structure and date here, and let the closure sub-select narrow the node.
            table.CreateIndex(
                $"IDX_{nameof(EmployeeAssignmentIndex)}_Node",
                nameof(EmployeeAssignmentIndex.StructureId),
                nameof(EmployeeAssignmentIndex.NodeId),
                nameof(EmployeeAssignmentIndex.EffectiveFrom),
                nameof(EmployeeAssignmentIndex.EffectiveToInclusive));

            // "Where does this person work", which self-service asks on every page.
            table.CreateIndex(
                $"IDX_{nameof(EmployeeAssignmentIndex)}_Employee",
                nameof(EmployeeAssignmentIndex.EmployeeId),
                nameof(EmployeeAssignmentIndex.StructureId),
                nameof(EmployeeAssignmentIndex.EffectiveFrom),
                nameof(EmployeeAssignmentIndex.EffectiveToInclusive));
        });

        return 3;
    }

    /// <summary>
    /// Adds <see cref="DimensionRecordPartIndex.NameAr"/> to a tenant whose
    /// <c>DimensionRecordPartIndex</c> table predates it.
    /// </summary>
    /// <remarks>
    /// <b>The bug this repairs.</b> The column was added to this module's record layer by editing
    /// the already-shipped <see cref="UpdateFrom1Async"/> in place, rather than by adding a new
    /// step — a tenant that had already run <c>UpdateFrom1Async</c> before that edit landed was
    /// recorded as being at version 2 and so never ran it again, and was left with a
    /// <c>DimensionRecordPartIndex</c> table permanently missing the column. The first symptom was
    /// SQLite error 1, "table DimensionRecordPartIndex has no column named NameAr", the moment the
    /// <c>dimension-records</c> recipe step tried to create a record. That mistake is exactly what
    /// CLAUDE.md's "migrations are append-only" rule now exists to rule out.
    ///
    /// <b>Why this checks before adding, unlike every other step here.</b> The edit this repairs
    /// was not reverted — <see cref="UpdateFrom1Async"/>'s body still creates the column, because a
    /// brand-new tenant must still get the complete, current schema from it without a second step
    /// patching the first. That means a brand-new tenant reaches this step with the column already
    /// present, while only a tenant stuck at version 2 from before the edit is missing it. Both
    /// must be handled by one step without erroring on either, which an unconditional
    /// <c>AddColumn</c> cannot do — SQLite and SQL Server both refuse to add a column that is
    /// already there — so this is the one place in this module's migrations that inspects the
    /// database before writing to it.
    /// </remarks>
    public async Task<int> UpdateFrom3Async()
    {
        if (!await ColumnExistsAsync(nameof(DimensionRecordPartIndex), nameof(DimensionRecordPartIndex.NameAr)))
        {
            await SchemaBuilder.AlterIndexTableAsync<DimensionRecordPartIndex>(table =>
                table.AddColumn<string>(nameof(DimensionRecordPartIndex.NameAr), column => column.WithLength(NameLength)));
        }

        return 4;
    }

    /// <summary>
    /// ADR-0010: gives every structure an explicit containment map, derived losslessly from the
    /// chain of levels and the skip-level flag it already had.
    /// </summary>
    /// <remarks>
    /// Two steps, in this order. The column first, so that saving a document below writes a
    /// complete index row rather than one missing a column that does not exist yet. Then the
    /// documents, through <see cref="StructureContainmentDerivation.FromChain"/> — the same
    /// function the <c>structures</c> recipe step uses for a row still written in the old form, so
    /// an upgraded tenant and a freshly seeded one cannot end up with different rules from the
    /// same description.
    ///
    /// Self-nesting is read from <see cref="DimensionTypeIndex"/> rather than from the type
    /// documents: it is one column on an index that already exists, and a migration that loads
    /// every dimension type to read one boolean is a migration that gets slower with the tenant.
    ///
    /// Idempotent in the way that matters here. A structure that already carries a map — which can
    /// only happen if this ran, since nothing else writes one — is left alone rather than
    /// re-derived, so a half-finished run that is retried does not overwrite a map a customer has
    /// since edited.
    /// </remarks>
    public async Task<int> UpdateFrom4Async()
    {
        // Guarded for the reason UpdateFrom3Async spells out at length: a brand-new tenant runs
        // CreateAsync and then every UpdateFromNAsync in turn, so it arrives here with the column
        // CreateAsync just made, while a tenant upgrading from version 4 does not. CreateAsync has
        // to keep producing the current schema — it is never edited to carry an upgrade — so one
        // of the two must look before it writes, and it has to be this one.
        if (!await ColumnExistsAsync(nameof(StructureIndex), nameof(StructureIndex.ContainmentRuleCount)))
        {
            await SchemaBuilder.AlterIndexTableAsync<StructureIndex>(table =>
                table.AddColumn<int>(nameof(StructureIndex.ContainmentRuleCount)));
        }

        var selfNesting = (await _session
                .QueryIndex<DimensionTypeIndex>(index => index.AllowsSelfNesting)
                .ListAsync())
            .Select(index => index.DimensionTypeId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var structure in await _session.Query<StructureDocument, StructureIndex>().ListAsync())
        {
            if (structure.Containment.Count > 0 || structure.RootDimensionTypeIds.Count > 0)
            {
                continue;
            }

            var (roots, containment) = StructureContainmentDerivation.FromChain(
                structure.DimensionTypeIds, structure.AllowSkipLevel, selfNesting);

            structure.RootDimensionTypeIds = roots;
            structure.Containment = containment;

            await _session.SaveCheckedAsync(structure);
        }

        return 5;
    }

    /// <summary>
    /// ADR-0012: the unit head, as a dated record of its own rather than a flag on an assignment.
    /// </summary>
    /// <remarks>
    /// A new table, so this step creates it and <see cref="CreateAsync"/> does not — the same shape
    /// as <see cref="UpdateFrom1Async"/> and <see cref="UpdateFrom2Async"/>, which created the
    /// record and graph layers' tables without ever being folded back into
    /// <see cref="CreateAsync"/>. A brand-new tenant runs <see cref="CreateAsync"/> and then every
    /// <c>UpdateFromNAsync</c> in order, so it arrives here with no table, exactly as a tenant
    /// upgrading from version 5 does. That is why this step needs none of the
    /// <see cref="ColumnExistsAsync"/> guarding <see cref="UpdateFrom3Async"/> and
    /// <see cref="UpdateFrom4Async"/> need: those two add a <em>column</em> that
    /// <see cref="CreateAsync"/> also produces, and so have to handle both states.
    ///
    /// Nothing is migrated into it. <c>DimensionRecordPart.HeadEmployeeId</c> is retired by
    /// ADR-0012 but its stored values are left exactly where they are, per ADR-0009: a migration
    /// never destroys data. Converting them would also be wrong on the facts — the old field is
    /// undated, so there is no term to invent for it, and inventing one would assert that every
    /// head had led their unit since the beginning of time.
    /// </remarks>
    public async Task<int> UpdateFrom5Async()
    {
        await SchemaBuilder.CreateMapIndexTableAsync<UnitHeadIndex>(table => table
            .Column<string>(nameof(UnitHeadIndex.StructureId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(UnitHeadIndex.NodeId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(UnitHeadIndex.EmployeeId), column => column.WithLength(IdentifierLength))
            .Column<DateTime>(nameof(UnitHeadIndex.EffectiveFrom))
            .Column<DateTime>(nameof(UnitHeadIndex.EffectiveToInclusive)));

        await SchemaBuilder.AlterIndexTableAsync<UnitHeadIndex>(table =>
        {
            // "Who heads this unit on this date" — what the designer's card asks of every unit in a
            // row and what prompt 5's routing asks of one. The column order is the order both
            // filter in.
            table.CreateIndex(
                $"IDX_{nameof(UnitHeadIndex)}_Node",
                nameof(UnitHeadIndex.StructureId),
                nameof(UnitHeadIndex.NodeId),
                nameof(UnitHeadIndex.EffectiveFrom),
                nameof(UnitHeadIndex.EffectiveToInclusive));

            // "What does this person head" — asked by the exit transition, which has to close an
            // employee's headships on the day they leave across every axis at once, and by the
            // employee's own profile.
            table.CreateIndex(
                $"IDX_{nameof(UnitHeadIndex)}_Employee",
                nameof(UnitHeadIndex.EmployeeId),
                nameof(UnitHeadIndex.EffectiveFrom),
                nameof(UnitHeadIndex.EffectiveToInclusive));
        });

        return 6;
    }

    /// <summary>
    /// Whether <paramref name="tableName"/> already has a column named <paramref name="columnName"/>,
    /// read from the database itself rather than assumed from what the code expects — the whole
    /// point of the check in <see cref="UpdateFrom3Async"/> is to stop trusting that.
    /// </summary>
    /// <remarks>
    /// Not <see cref="System.Data.Common.DbConnection.GetSchema(String, String[])"/>: it looks like
    /// the standard, provider-agnostic way to ask this, but verified directly against
    /// <c>Microsoft.Data.Sqlite</c> 10.0.8 — the provider this solution's tests actually run
    /// against — it does not implement the "Columns" collection at all.
    /// <c>connection.GetSchema("Columns", [])</c> throws <c>"The requested collection 'Columns' is
    /// not defined"</c>, and `connection.GetSchema()` with no arguments lists only
    /// <c>MetaDataCollections</c> and <c>ReservedWords</c> as supported. A provider-agnostic
    /// abstraction that only one of the two providers this solution pins actually implements is
    /// not provider-agnostic; this queries each dialect directly instead, which is what
    /// <c>DimensionsMigrationUpgradeTenantTests</c> proves against the real SQLite provider.
    ///
    /// SQL Server's branch is unverified against a live SQL Server — this solution's tests run
    /// against SQLite only — but <c>INFORMATION_SCHEMA.COLUMNS</c> is standard ANSI SQL, not a
    /// SQL-Server-specific guess.
    /// </remarks>
    private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
    {
        var fullTableName = SchemaBuilder.TablePrefix + tableName;
        var connection = SchemaBuilder.Connection;

        var wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = SchemaBuilder.Transaction;

            if (string.Equals(SchemaBuilder.Dialect.Name, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                // PRAGMA does not accept bound parameters; fullTableName is built from nameof(...)
                // and this module's own table prefix, never from anything a caller supplies.
                command.CommandText = $"PRAGMA table_info('{fullTableName}')";

                using var reader = await command.ExecuteReaderAsync();
                var nameOrdinal = -1;

                while (await reader.ReadAsync())
                {
                    if (nameOrdinal < 0)
                    {
                        nameOrdinal = reader.GetOrdinal("name");
                    }

                    if (string.Equals(reader.GetString(nameOrdinal), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            command.CommandText =
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column";

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = fullTableName;
            command.Parameters.Add(tableParameter);

            var columnParameter = command.CreateParameter();
            columnParameter.ParameterName = "@column";
            columnParameter.Value = columnName;
            command.Parameters.Add(columnParameter);

            var count = await command.ExecuteScalarAsync();
            return count is not null and not DBNull && Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
