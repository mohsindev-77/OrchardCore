using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Data.Migration;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
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

    public Migrations(IContentDefinitionManager contentDefinitionManager) =>
        _contentDefinitionManager = contentDefinitionManager;

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
            .Column<int>(nameof(StructureIndex.LevelCount)));

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
}
