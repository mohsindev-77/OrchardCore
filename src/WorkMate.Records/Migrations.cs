using System.Globalization;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Data.Migration;
using OrchardCore.Flows.Models;
using OrchardCore.Title.Models;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using YesSql;
using YesSql.Sql;

namespace WorkMate.Records;

/// <summary>
/// Schema for WorkMate.Records: the employee record's fixed core, its index, and the six standard
/// sections.
/// </summary>
/// <remarks>
/// Migrations are additive and append-only. The body of <see cref="CreateAsync"/> is never edited
/// once it has shipped to carry an upgrade — a tenant that has already run it is recorded at that
/// version and will never run it again, so the edit would be invisible to every tenant that
/// upgraded before it landed. ADR-0009 records the defect that rule exists to stop recurring, and
/// <c>WorkMate.Dimensions.Migrations.UpdateFrom3Async</c> is the repair it was written from.
///
/// The index's date columns are <c>DateTime</c> rather than <c>DateOnly</c>: YesSql 5.4.7 has no
/// mapping for the latter and the schema builder throws on one. <c>EffectiveDates</c>, in
/// <c>WorkMate.Dimensions</c>, is the one place that conversion happens, and this module reuses it
/// rather than keeping a second copy with a second open-ended sentinel.
/// </remarks>
public sealed class Migrations : DataMigration
{
    /// <summary>Column length for a generated identifier, matching Orchard's own content item ids.</summary>
    private const int IdentifierLength = 26;

    /// <summary>
    /// Column length for a name. Indexed, so bounded rather than unlimited: several providers
    /// refuse to index a column wider than a page.
    /// </summary>
    private const int NameLength = 255;

    /// <summary>Column length for the stored status name. Generous for the longest of five.</summary>
    private const int StatusLength = 32;

    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly IContentManager _contentManager;

    /// <summary>
    /// For the one step that repairs data rather than schema. <see cref="UpdateFrom1Async"/> has to
    /// read and rewrite stored content items, which <c>SchemaBuilder</c> cannot do.
    /// </summary>
    private readonly ISession _session;

    public Migrations(
        IContentDefinitionManager contentDefinitionManager,
        IContentManager contentManager,
        ISession session)
    {
        _contentDefinitionManager = contentDefinitionManager;
        _contentManager = contentManager;
        _session = session;
    }

    public async Task<int> CreateAsync()
    {
        await CreateIndexAsync();
        await CreateCorePartAsync();
        await CreateSectionTypesAsync();
        await CreateEmployeeTypeAsync();

        // The standard bindings, so a brand-new tenant gets the current definition in one pass.
        // UpdateFrom4Async carries the same change to a tenant created before it; see that step for
        // why a definition change is stated in both places and a table creation is not.
        await BindStandardLookupFieldsAsync();

        return 1;
    }

    private async Task CreateIndexAsync()
    {
        await SchemaBuilder.CreateMapIndexTableAsync<EmployeeIndex>(table => table
            .Column<string>(nameof(EmployeeIndex.ContentItemId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(EmployeeIndex.Code), column => column.WithLength(EmployeeCodes.MaxLength))
            .Column<string>(nameof(EmployeeIndex.CodeUpper), column => column.WithLength(EmployeeCodes.MaxLength))
            .Column<string>(nameof(EmployeeIndex.NameEn), column => column.WithLength(NameLength))
            .Column<string>(nameof(EmployeeIndex.NameAr), column => column.WithLength(NameLength))
            .Column<string>(nameof(EmployeeIndex.Status), column => column.WithLength(StatusLength))
            .Column<DateTime>(nameof(EmployeeIndex.StatusEffectiveFrom))
            .Column<DateTime>(nameof(EmployeeIndex.JoinDate))
            .Column<DateTime>(nameof(EmployeeIndex.ExitedFrom), column => column.Nullable())
            .Column<string>(nameof(EmployeeIndex.LineManagerEmployeeId), column => column.WithLength(IdentifierLength))
            .Column<bool>(nameof(EmployeeIndex.Latest))
            .Column<bool>(nameof(EmployeeIndex.Published)));

        await SchemaBuilder.AlterIndexTableAsync<EmployeeIndex>(table =>
        {
            // The uniqueness check runs on every employee write and on every row of an import, so
            // it is the hottest lookup on this table. On the folded column, because that is what
            // the comparison actually uses — see EmployeeIndex.CodeUpper for why the database's own
            // collation is not trusted with it.
            table.CreateIndex($"IDX_{nameof(EmployeeIndex)}_CodeUpper", nameof(EmployeeIndex.CodeUpper));

            table.CreateIndex($"IDX_{nameof(EmployeeIndex)}_ContentItemId", nameof(EmployeeIndex.ContentItemId));

            // "Everyone employed on this date" — what every headcount, payroll run and picker asks.
            // The column order is the order the query filters in.
            table.CreateIndex(
                $"IDX_{nameof(EmployeeIndex)}_Employment",
                nameof(EmployeeIndex.Status),
                nameof(EmployeeIndex.JoinDate),
                nameof(EmployeeIndex.ExitedFrom));

            // "Who reports to this person", which an approval chain and an org view both walk.
            table.CreateIndex(
                $"IDX_{nameof(EmployeeIndex)}_LineManager",
                nameof(EmployeeIndex.LineManagerEmployeeId));
        });
    }

    /// <summary>
    /// The table behind <see cref="LookupListIndex"/>: the managed lists a choice field offers.
    /// </summary>
    /// <remarks>
    /// ADR-0014 decided these are documents of ours rather than Orchard taxonomies, so this module
    /// owns their schema. One row per list; the entries live in the document, because a list is
    /// read whole — a reader is offered all of it or none of it — and a second table would buy a
    /// join on every render for a set that is tens of rows.
    ///
    /// <b>Called only from <see cref="UpdateFrom3Async"/>, never from <see cref="CreateAsync"/>.</b>
    /// A brand-new tenant runs <see cref="CreateAsync"/> and then every <c>UpdateFromNAsync</c> in
    /// turn, so a table named in both would be created twice and the second attempt fails the whole
    /// migration — which fails tenant setup, with an error about a table rather than about the step
    /// that added it. A wholly new table therefore goes in the new step alone, exactly as
    /// <c>WorkMate.Dimensions.Migrations.UpdateFrom5Async</c> does for <c>UnitHeadIndex</c>.
    ///
    /// That is not in tension with ADR-0009. The rule there is that a <em>shipped</em> step is never
    /// edited, and that <see cref="CreateAsync"/> is kept current for things an update step cannot
    /// express on its own — a content type setting, say, which <see cref="UpdateFrom2Async"/> had to
    /// state in both places because altering a definition is idempotent and creating a table is not.
    /// </remarks>
    private async Task CreateLookupListIndexAsync()
    {
        await SchemaBuilder.CreateMapIndexTableAsync<LookupListIndex>(table => table
            .Column<string>(nameof(LookupListIndex.LookupListId), column => column.WithLength(IdentifierLength))
            .Column<string>(nameof(LookupListIndex.Code), column => column.WithLength(LookupCodes.MaxLength))
            .Column<string>(nameof(LookupListIndex.CodeUpper), column => column.WithLength(LookupCodes.MaxLength))
            .Column<string>(nameof(LookupListIndex.NameEn), column => column.WithLength(NameLength))
            .Column<string>(nameof(LookupListIndex.NameAr), column => column.WithLength(NameLength))
            .Column<bool>(nameof(LookupListIndex.IsSystemDefined))
            .Column<int>(nameof(LookupListIndex.ItemCount))
            .Column<int>(nameof(LookupListIndex.ActiveItemCount)));

        await SchemaBuilder.AlterIndexTableAsync<LookupListIndex>(table =>
        {
            // The uniqueness check on create and the lookup a recipe step, an export and a bound
            // field's editor all make. On the folded column, because that is what the comparison
            // uses — see LookupListIndex.CodeUpper.
            table.CreateIndex($"IDX_{nameof(LookupListIndex)}_CodeUpper", nameof(LookupListIndex.CodeUpper));

            table.CreateIndex($"IDX_{nameof(LookupListIndex)}_LookupListId", nameof(LookupListIndex.LookupListId));
        });
    }

    /// <summary>
    /// The fixed core, declared here rather than by a service.
    /// </summary>
    /// <remarks>
    /// A part definition is schema: the same on every tenant, and present whether or not an employee
    /// has been created. The same reasoning as <c>DimensionRecordPart</c>'s.
    ///
    /// <b><c>Attachable = false</c> is load-bearing, not tidiness.</b> It keeps <c>EmployeePart</c>
    /// out of Orchard's "Add parts" list, so an administrator cannot weld it onto another content
    /// type or take it off <c>Employee</c>. Specification section 5 requires the core to be beyond
    /// their reach and the brief asks for that to be impossible rather than discouraged; this is the
    /// half of it Orchard itself enforces, and <c>EmployeeContentDefinitionGuard</c> says which
    /// halves are not closed yet and where they will be.
    /// </remarks>
    private Task CreateCorePartAsync() =>
        _contentDefinitionManager.AlterPartDefinitionAsync(
            EmployeeFieldNames.PartName,
            part => part.WithSettings(new ContentPartSettings
            {
                Attachable = false,
                DisplayName = "Employee",
                Description =
                    "The fixed core of the employee record: code, bilingual name, date of birth, "
                    + "nationality, gender, join date, employment status, line manager and photo. "
                    + "Not extensible: payroll, leave and attendance depend on these fields by name.",
            }));

    /// <summary>
    /// The six standard sections' item types.
    /// </summary>
    /// <remarks>
    /// Every field name goes through <see cref="EmployeeContentDefinitionGuard.EnsureMayDefine"/> as
    /// it is declared, so a section field that collided with a core name — or that reintroduced the
    /// department field architecture section 2 exists to rule out — fails at tenant setup rather
    /// than shipping. A check on this module's own source, which is why it throws rather than
    /// returning a message.
    /// </remarks>
    private async Task CreateSectionTypesAsync()
    {
        // Job details. Dated, because a grade, a job title and a contract all change, and "what
        // were they on last March" is a question payroll asks constantly. The dates are on the row
        // rather than on a version of the record, so a correction and a promotion look different.
        await SectionTypeAsync(
            EmployeeSections.JobDetailType,
            "Job details",
            "JobTitle.En",
            [
                new SectionField("JobTitle", "BilingualTextField", "Job title"),
                new SectionField("GradeCode", "TextField", "Grade"),
                new SectionField("EmploymentTypeCode", "TextField", "Employment type"),
                new SectionField("WorkLocation", "TextField", "Work location"),
                new SectionField("EffectiveFrom", "DateField", "Effective from"),
                new SectionField("EffectiveTo", "DateField", "Effective to"),
                new SectionField("ProbationEndsOn", "DateField", "Probation ends"),
            ]);

        // Documents. ExpiresOn is why this is a list and not three fields: the document expiry
        // alerts the product promises are a query over these rows, and a flattened section could
        // hold one passport and no visa.
        await SectionTypeAsync(
            EmployeeSections.DocumentType,
            "Document",
            "DocumentTypeCode.Text",
            [
                new SectionField("DocumentTypeCode", "TextField", "Document type"),
                new SectionField("DocumentNumber", "TextField", "Number"),
                new SectionField("IssuingAuthority", "TextField", "Issuing authority"),
                new SectionField("IssuedOn", "DateField", "Issued on"),
                new SectionField("ExpiresOn", "DateField", "Expires on"),
                new SectionField("Attachment", "MediaField", "Attachment"),
            ]);

        // Bank details. SharePercent and IsPrimaryAccount are the split-salary case, which is
        // ordinary in the Gulf: part paid locally, part remitted home.
        await SectionTypeAsync(
            EmployeeSections.BankAccountType,
            "Bank account",
            "BankName.En",
            [
                new SectionField("BankName", "BilingualTextField", "Bank"),
                new SectionField("AccountNumber", "TextField", "Account number"),
                new SectionField("Iban", "TextField", "IBAN"),
                new SectionField("SwiftCode", "TextField", "SWIFT/BIC"),
                new SectionField("CurrencyCode", "TextField", "Currency"),
                new SectionField("SharePercent", "NumericField", "Share of salary (%)"),
                new SectionField("IsPrimaryAccount", "BooleanField", "Primary account"),
            ]);

        await SectionTypeAsync(
            EmployeeSections.QualificationType,
            "Qualification",
            "Qualification.En",
            [
                new SectionField("Qualification", "BilingualTextField", "Qualification"),
                new SectionField("Institution", "BilingualTextField", "Institution"),
                new SectionField("AwardedOn", "DateField", "Awarded on"),
                new SectionField("CountryCode", "TextField", "Country"),
                new SectionField("IsVerified", "BooleanField", "Verified"),
            ]);

        // Dependants. IsSponsored drives visa and air-ticket entitlement, which is why it is a
        // field rather than something inferred from the relationship.
        await SectionTypeAsync(
            EmployeeSections.DependantType,
            "Dependant",
            "FullName.En",
            [
                new SectionField("FullName", "BilingualTextField", "Name"),
                new SectionField("RelationshipCode", "TextField", "Relationship"),
                new SectionField("DependantDateOfBirth", "DateField", "Date of birth"),
                new SectionField("DependantNationalityCode", "TextField", "Nationality"),
                new SectionField("IsSponsored", "BooleanField", "Sponsored"),
            ]);

        await SectionTypeAsync(
            EmployeeSections.ContactType,
            "Contact",
            "FullName.En",
            [
                new SectionField("FullName", "BilingualTextField", "Name"),
                new SectionField("ContactTypeCode", "TextField", "Contact type"),
                new SectionField("Telephone", "TextField", "Telephone"),
                new SectionField("EmailAddress", "TextField", "Email"),
                new SectionField("AddressLine", "TextField", "Address"),
                new SectionField("City", "TextField", "City"),
                new SectionField("CountryCode", "TextField", "Country"),
                new SectionField("IsEmergencyContact", "BooleanField", "Emergency contact"),
            ]);
    }

    /// <summary>
    /// Gives every employee who already exists the display text they should always have had.
    /// </summary>
    /// <remarks>
    /// The repair half of the fix on <see cref="Handlers.EmployeeHandler"/>: the <c>TitlePart</c>
    /// pattern produces nothing for a record created outside an HTTP request, because
    /// <c>TitlePartHandler</c> renders it through Liquid and that throws there. The handler fixes it
    /// from here on; this fixes what is already stored.
    ///
    /// A separate step rather than an edit to <see cref="CreateAsync"/>, per ADR-0009, even though
    /// the window in which an affected employee could have been created is one commit wide: the
    /// rule is that a shipped step is never edited, and a rule with an exception for "it is probably
    /// fine" is not a rule. The cost of being wrong about that is a tenant whose employees are
    /// permanently blank in every picker.
    ///
    /// Only fills what is absent, so it never overwrites a title somebody has, and is therefore
    /// idempotent — a retried half-finished run does not undo the half that worked.
    /// </remarks>
    public async Task<int> UpdateFrom1Async()
    {
        // The ids from the index, then each item through IContentManager, which is the way a
        // content item is written everywhere else in this product. See
        // WorkMate.Dimensions.Migrations.UpdateFrom6Async for why an ISession.SaveAsync would be
        // the wrong tool for a content item even where no guard forbids it.
        var ids = (await _session
                .QueryIndex<EmployeeIndex>(index => index.Latest)
                .ListAsync())
            .Select(row => row.ContentItemId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var id in ids)
        {
            var employee = await _contentManager.GetAsync(id);

            if (employee is null ||
                !string.IsNullOrWhiteSpace(employee.DisplayText) ||
                !employee.TryGet<EmployeePart>(out var part) ||
                string.IsNullOrWhiteSpace(part.NameEn))
            {
                continue;
            }

            employee.DisplayText = part.NameEn;

            await _contentManager.UpdateAsync(employee);
        }

        return 2;
    }

    /// <summary>
    /// Takes <c>Employee</c> off Orchard's generic "new content item" list, so there is one way to
    /// create an employee rather than two.
    /// </summary>
    /// <remarks>
    /// <b>The defect this closes.</b> <c>IEmployeeService.CreateAsync</c> raises
    /// <c>EmployeeCreated</c>, which specification section 5 requires leave, attendance and payroll
    /// to subscribe to. Orchard's generic content editor does not call it: an employee created that
    /// way is validated by <see cref="Handlers.EmployeeHandler"/> and is correct in every visible
    /// respect, and the three modules that needed telling are never told. The failure is permanent
    /// and silent — there is no later moment at which anybody notices the event did not fire.
    ///
    /// Listable and securable stay on. The type still appears in Orchard's content list and still
    /// obeys its per-type permissions; it simply has no "New" button, which is what makes this
    /// module's own Add-employee screen the only way in. Editing is unaffected, which is what the
    /// standard sections are edited through.
    ///
    /// <b>A new step, and <see cref="CreateAsync"/> is amended to match.</b> The append-only rule:
    /// <see cref="CreateAsync"/> is kept producing the current schema for a brand-new tenant and
    /// never carries an upgrade, so it now says <c>Creatable = false</c> and this step applies the
    /// same change to a tenant that already ran it. The same shape as
    /// <c>WorkMate.Dimensions.Migrations.UpdateFrom4Async</c> and
    /// <c>StructureIndex.ContainmentRuleCount</c>.
    /// </remarks>
    public async Task<int> UpdateFrom2Async()
    {
        await _contentDefinitionManager.AlterTypeDefinitionAsync(
            EmployeeFieldNames.ContentType,
            type => type.WithSettings(new ContentTypeSettings
            {
                Creatable = false,
                Listable = true,
                Securable = true,
                Draftable = false,
            }));

        return 3;
    }

    /// <summary>
    /// Gives a tenant created before slice B1 the table the managed lookup lists live in.
    /// </summary>
    /// <remarks>
    /// ADR-0009: a shipped step is never edited, so the table arrives in a step of its own. It is
    /// the only place the table is created — see <see cref="CreateLookupListIndexAsync"/> for why
    /// naming it in <see cref="CreateAsync"/> as well would fail every fresh tenant's setup.
    ///
    /// <b>It is safe to re-enter.</b> No earlier step creates this table, so the guard is not
    /// about tolerating two shipped versions the way
    /// <c>WorkMate.Dimensions.Migrations.UpdateFrom3Async</c>'s is. It is about a recorded version
    /// going backwards, which happens — see <see cref="TableExistsAsync"/> for the three ways and
    /// for what the failure looks like when it does, which is a migration aborting somewhere else
    /// entirely.
    ///
    /// <c>RecordsMigrationUpgradeTenantTests</c> is the upgrade test the rule requires: it rolls a
    /// tenant back to version 3, drops the table as a tenant of that vintage would have had it,
    /// runs the migration forward, and then creates and reads a list through
    /// <see cref="Services.ILookupListService"/> — the real operation, not a check that a column
    /// exists.
    /// </remarks>
    public async Task<int> UpdateFrom3Async()
    {
        if (!await TableExistsAsync(nameof(LookupListIndex)))
        {
            await CreateLookupListIndexAsync();
        }

        return 4;
    }

    /// <summary>
    /// Binds the standard section fields to the lookup lists WorkMate ships.
    /// </summary>
    /// <remarks>
    /// <b>A migration rather than a recipe step, because a binding is schema.</b> It is part of the
    /// content definition, it is the same on every tenant, and it must reach tenants that already
    /// exist — which a recipe cannot do, because a recipe is applied once at setup and a later edit
    /// to it never arrives anywhere. The same reasoning the module manifest's dependency list
    /// carries for <c>OrchardCore.Flows</c>, and the reason that one was a defect when it was only
    /// in the base recipe.
    ///
    /// <b>Stated here and in <see cref="CreateSectionTypesAsync"/> both</b>, which is ADR-0009's
    /// shape for a definition change rather than duplication: altering a definition is idempotent,
    /// so a brand-new tenant can get the current shape in one pass from <see cref="CreateAsync"/>
    /// and this step can carry the same change to a tenant that ran it before the binding existed.
    /// <see cref="UpdateFrom2Async"/> did exactly this for the <c>Creatable</c> setting; creating a
    /// table is the case where that does not work, which is why
    /// <see cref="CreateLookupListIndexAsync"/> is in one place and this is in two.
    ///
    /// <b>Binding changes no stored data and takes nothing away.</b> Each field keeps its type and
    /// its stored string; what changes is the editor that renders it. On a tenant that has not
    /// applied the standard lists there is nothing in the list behind the binding, and
    /// <c>ILookupChoiceProvider</c> falls back to the free-text box the screen has always shown —
    /// so this step is invisible until the lists arrive.
    /// </remarks>
    public async Task<int> UpdateFrom4Async()
    {
        await BindStandardLookupFieldsAsync();

        return 5;
    }

    /// <summary>
    /// Sets the lookup editor and the list code on every field in
    /// <see cref="StandardLookupLists.Bindings"/>.
    /// </summary>
    /// <remarks>
    /// Grouped by part, because <c>AlterPartDefinitionAsync</c> rewrites the part's definition and
    /// two calls for two fields on one part would make the second read whatever the first left.
    ///
    /// <c>WithSettings</c> on a field builder merges into the field's settings rather than
    /// replacing them, so the display name and description
    /// <see cref="CreateSectionTypesAsync"/> set survive. <c>RenderedLabelTenantTests</c> is the
    /// existing guard that a section field still renders its label.
    /// </remarks>
    private async Task BindStandardLookupFieldsAsync()
    {
        foreach (var part in StandardLookupLists.Bindings.GroupBy(binding => binding.PartName, StringComparer.Ordinal))
        {
            await _contentDefinitionManager.AlterPartDefinitionAsync(part.Key, builder =>
            {
                foreach (var binding in part)
                {
                    builder.WithField(binding.FieldName, field => field
                        .WithEditor(LookupFieldEditor.Name)
                        .WithSettings(new LookupFieldSettings { ListCode = binding.ListCode }));
                }
            });
        }
    }

    /// <summary>
    /// Whether <paramref name="tableName"/> already exists, read from the database itself rather
    /// than inferred from the recorded migration version.
    /// </summary>
    /// <remarks>
    /// <b>Why a migration step that creates a table has to ask.</b> Orchard's runner records a
    /// version per tenant and will not normally re-run a step the tenant is past — but "normally"
    /// is doing real work in that sentence. A recorded version can go backwards: an operator
    /// repairing a half-migrated tenant, a restore that brings back an older migration document
    /// beside a newer schema, or — the case that found this — another part of the suite rolling
    /// this module back to exercise an earlier step. The runner then re-enters this step against a
    /// database that already has the table, <c>CreateMapIndexTableAsync</c> throws, and the whole
    /// feature's migration aborts: every later step stops running and the failure surfaces
    /// somewhere else entirely.
    ///
    /// A step that is safe to re-enter costs one query and removes that whole class of problem.
    /// <c>WorkMate.Dimensions.Migrations.UpdateFrom3Async</c> reached the same conclusion for a
    /// column, by a different route, and carries the detail on why
    /// <c>DbConnection.GetSchema("Columns", …)</c> is not the provider-agnostic answer it looks
    /// like — <c>Microsoft.Data.Sqlite</c> 10.0.8, the provider these tests run against, does not
    /// implement the collection at all. So each dialect is queried directly.
    ///
    /// SQL Server's branch is unverified against a live SQL Server — this solution's tests run on
    /// SQLite only — but <c>INFORMATION_SCHEMA.TABLES</c> is standard ANSI SQL rather than a
    /// SQL-Server-specific guess.
    /// </remarks>
    private async Task<bool> TableExistsAsync(string tableName)
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

            command.CommandText =
                string.Equals(SchemaBuilder.Dialect.Name, "Sqlite", StringComparison.OrdinalIgnoreCase)
                    ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table"
                    : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @table";

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "@table";
            tableParameter.Value = fullTableName;
            command.Parameters.Add(tableParameter);

            var count = await command.ExecuteScalarAsync();

            return count is not null and not DBNull &&
                Convert.ToInt64(count, CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>One field on one section item type.</summary>
    private sealed record SectionField(string Name, string FieldType, string Label);

    /// <summary>
    /// Builds one section item type: a part named for the type carrying its fields, a generated
    /// title, and nothing else.
    /// </summary>
    /// <param name="titleField">
    /// The field path, relative to the type's own part, that a bag row shows in its collapsed
    /// header — so a list of dependants reads as names rather than as six identical blank rows.
    /// </param>
    /// <remarks>
    /// The fields go on a part named for the content type rather than onto a shared part: the
    /// convention Orchard's own type editor follows, and necessary here, because a shared part would
    /// put every section's fields on every section.
    /// </remarks>
    private async Task SectionTypeAsync(
        string typeName,
        string displayName,
        string titleField,
        IReadOnlyList<SectionField> fields)
    {
        foreach (var field in fields)
        {
            EmployeeContentDefinitionGuard.EnsureMayDefine(typeName, field.Name);
        }

        await _contentDefinitionManager.AlterPartDefinitionAsync(typeName, part =>
        {
            part.WithSettings(new ContentPartSettings
            {
                Attachable = false,
                DisplayName = displayName,
                Description = string.Format(
                    CultureInfo.InvariantCulture,
                    "The fields of one {0} row on an employee record.",
                    displayName.ToLowerInvariant()),
            });

            foreach (var field in fields)
            {
                part.WithField(field.Name, builder => builder
                    .OfType(field.FieldType)
                    .WithSettings(new ContentPartFieldSettings { DisplayName = field.Label }));
            }
        });

        await _contentDefinitionManager.AlterTypeDefinitionAsync(typeName, type => type
            .WithDisplayName(displayName)
            .WithPart(typeName)
            .WithPart(nameof(TitlePart), title => title.WithSettings(new TitlePartSettings
            {
                // Generated and read-only. A bag row needs something in its collapsed header, and
                // an editable title on a row of a list is one more box nobody fills in and every
                // list then shows blank.
                Options = TitlePartOptions.GeneratedDisabled,
                Pattern = $"{{{{ ContentItem.Content.{typeName}.{titleField} }}}}",
                RenderTitle = false,
            }))
            .WithSettings(new ContentTypeSettings
            {
                // Never on its own. A bank account with no employee is not a thing, and a listable
                // one would turn up in the content picker of every form in the tenant.
                Creatable = false,
                Listable = false,
                Securable = false,
                Draftable = false,
                Stereotype = EmployeeSections.Stereotype,
            }));
    }

    /// <summary>
    /// The <c>Employee</c> content type: the fixed core, a generated title, and the six sections as
    /// bags.
    /// </summary>
    /// <remarks>
    /// <b>Not draftable</b>, like a dimension record and for the same reason: a half-saved employee
    /// is not a thing. A draft would be invisible to the employee index — and so to the uniqueness
    /// check, to every headcount and to the picker — while looking real in the editor.
    ///
    /// <b>Securable</b>, so Orchard's own per-type content permissions apply on top of this module's
    /// four. Creatable and listable so the standard content screens work; this module still ships
    /// its own list in A2, because an employee list wants a status filter and a bilingual search
    /// that the generic one has no way to offer.
    /// </remarks>
    private Task CreateEmployeeTypeAsync() =>
        _contentDefinitionManager.AlterTypeDefinitionAsync(EmployeeFieldNames.ContentType, type =>
        {
            type
                .WithDisplayName("Employee")
                .WithPart(EmployeeFieldNames.PartName)
                .WithPart(nameof(TitlePart), title => title.WithSettings(new TitlePartSettings
                {
                    // The title is the English name, generated and read-only, so the two cannot
                    // drift apart silently. Exactly what every generated dimension type does.
                    Options = TitlePartOptions.GeneratedDisabled,
                    Pattern = $"{{{{ ContentItem.Content.{EmployeeFieldNames.PartName}.NameEn }}}}",
                    RenderTitle = true,
                }))
                .WithSettings(new ContentTypeSettings
                {
                    // Not creatable through Orchard's generic "new content item" screen. That path
                    // does not call IEmployeeService.CreateAsync, so it would never raise
                    // EmployeeCreated — and leave, attendance and payroll would simply never be
                    // told about the person. This module's Add-employee screen is the one way in.
                    // UpdateFrom2Async applies the same change to a tenant created before it.
                    Creatable = false,
                    Listable = true,
                    Securable = true,
                    Draftable = false,
                });

            foreach (var (section, itemType) in EmployeeSections.All)
            {
                type.WithPart(section, nameof(BagPart), bag => bag
                    .WithSettings(new ContentTypePartSettings { DisplayName = section })
                    .WithSettings(new BagPartSettings
                    {
                        // One kind of item per section. Without this a bag offers every creatable
                        // type in the tenant, so a "Bank details" section would invite somebody to
                        // add a blog post to it.
                        ContainedContentTypes = [itemType],
                        CollapseContainedItems = true,
                    }));
            }
        });
}
