using System.Globalization;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Data.Migration;
using OrchardCore.Flows.Models;
using OrchardCore.Title.Models;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
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

    public Migrations(IContentDefinitionManager contentDefinitionManager) =>
        _contentDefinitionManager = contentDefinitionManager;

    public async Task<int> CreateAsync()
    {
        await CreateIndexAsync();
        await CreateCorePartAsync();
        await CreateSectionTypesAsync();
        await CreateEmployeeTypeAsync();

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
                    Creatable = true,
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
