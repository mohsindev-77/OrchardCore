using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Environment.Cache;
using OrchardCore.Environment.Shell;
using OrchardCore.Flows.Models;
using OrchardCore.Title.Models;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Internal;
using WorkMate.Records.Models;
using YesSql;
using IIdGenerator = OrchardCore.Entities.IIdGenerator;

namespace WorkMate.Records.Services;

/// <inheritdoc />
/// <remarks>
/// <b>The compiler is in here rather than in a type of its own</b>, which is a deliberate
/// departure from how large this file ends up. Compiling is not a second operation on a form
/// definition: it is what publishing <em>is</em>, it has no caller but <c>PublishAsync</c>, and it
/// must never be reachable without the validation that precedes it. A separate
/// <c>IFormCompiler</c> would be a second way to change a content definition, which is exactly
/// what CLAUDE.md rule 1 and the safety rules are trying to prevent.
///
/// <b>Every content-definition read here is a <c>Load…</c>.</b> ADR-0006: this is a write path,
/// and inside the scope that altered a definition the cached read cannot see it. The failure is
/// silent — <c>NewAsync</c> returns an item with no parts welded and discards everything set on
/// it — which is why the rule is absolute on this path rather than a judgement call.
/// </remarks>
internal sealed class FormDefinitionService : IFormDefinitionService
{
    private readonly ISession _session;
    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly IEntitlementService _entitlement;
    private readonly IBilingualNamePolicy _namePolicy;
    private readonly ILookupListService _lookupLists;
    private readonly IEmployeeSectionCatalogue _sections;
    private readonly IAuditTrailManager _auditTrailManager;
    private readonly IIdGenerator _idGenerator;
    private readonly ISignal _signal;
    private readonly ShellSettings _shellSettings;

    /// <summary>Tells this scope that Employee's definition has moved under it. ADR-0006.</summary>
    private readonly ContentDefinitionChangesInThisScope _changes;

    private readonly IStringLocalizer S;

    public FormDefinitionService(
        ISession session,
        IContentDefinitionManager contentDefinitionManager,
        IRecordsAuthorisation authorisation,
        IEntitlementService entitlement,
        IBilingualNamePolicy namePolicy,
        ILookupListService lookupLists,
        IEmployeeSectionCatalogue sections,
        IAuditTrailManager auditTrailManager,
        IIdGenerator idGenerator,
        ISignal signal,
        ShellSettings shellSettings,
        ContentDefinitionChangesInThisScope changes,
        IStringLocalizer<FormDefinitionService> stringLocalizer)
    {
        _session = session;
        _contentDefinitionManager = contentDefinitionManager;
        _authorisation = authorisation;
        _entitlement = entitlement;
        _namePolicy = namePolicy;
        _lookupLists = lookupLists;
        _sections = sections;
        _auditTrailManager = auditTrailManager;
        _idGenerator = idGenerator;
        _signal = signal;
        _shellSettings = shellSettings;
        _changes = changes;
        S = stringLocalizer;
    }

    // ---- reads ---------------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<FormDefinitionDocument?> GetAsync(
        string formDefinitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(formDefinitionId))
        {
            return null;
        }

        return await _session
            .Query<FormDefinitionDocument, FormDefinitionIndex>(
                index => index.FormDefinitionId == formDefinitionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FormDefinitionDocument?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var folded = FormCodes.Normalise(code).ToUpperInvariant();

        if (folded.Length == 0)
        {
            return null;
        }

        return await _session
            .Query<FormDefinitionDocument, FormDefinitionIndex>(index => index.CodeUpper == folded)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FormDefinitionDocument>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return
        [
            .. (await _session
                    .Query<FormDefinitionDocument, FormDefinitionIndex>()
                    .OrderBy(index => index.NameEn)
                    .ListAsync(cancellationToken)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FormVersionDocument>> GetVersionsAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var folded = FormCodes.Normalise(code).ToUpperInvariant();

        if (folded.Length == 0)
        {
            return [];
        }

        return
        [
            .. (await _session
                    .Query<FormVersionDocument, FormVersionIndex>(index => index.FormCodeUpper == folded)
                    .OrderByDescending(index => index.VersionNumber)
                    .ListAsync(cancellationToken)),
        ];
    }

    // ---- the draft -----------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<RecordResult<FormDefinitionDocument>> CreateAsync(
        string code,
        BilingualText name,
        FormTarget target,
        string? sectionItemType = null,
        string? sectionName = null,
        BilingualText? sectionLabel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!await MayDesignAsync())
        {
            return RecordResult.NotAuthorised<FormDefinitionDocument>();
        }

        var normalised = FormCodes.Normalise(code);
        var errors = new List<RecordError>();

        if (!FormCodes.IsValidCode(normalised))
        {
            errors.Add(new RecordError(
                RecordRule.CodeFormat,
                normalised,
                S["'{0}' is not a valid form code. Use a letter followed by letters, digits, hyphens or underscores, up to {1} characters.",
                    normalised,
                    FormCodes.MaxCodeLength],
                Field: "Code"));
        }
        else if (await GetByCodeAsync(normalised, cancellationToken) is not null)
        {
            errors.Add(new RecordError(
                RecordRule.CodeUniqueness,
                normalised,
                S["There is already a form definition with the code '{0}' in this tenant.", normalised],
                Field: "Code"));
        }

        errors.AddRange(await ValidateNameAsync(name, normalised, cancellationToken));
        errors.AddRange(await ValidateTargetAsync(target, sectionItemType, sectionName, cancellationToken));

        if (errors.Count > 0)
        {
            return RecordResult.Failed<FormDefinitionDocument>(errors);
        }

        var document = new FormDefinitionDocument
        {
            FormDefinitionId = _idGenerator.GenerateUniqueId(),
            Code = normalised,
            Name = Trimmed(name),
            Target = target,
            SectionItemType = (sectionItemType ?? string.Empty).Trim(),
            SectionName = (sectionName ?? string.Empty).Trim(),
            SectionLabel = Trimmed(sectionLabel ?? name),
            Fields = [],

            // A definition with no fields published is still a change worth publishing — a new
            // section with no fields yet is a real intermediate state — so this starts true.
            HasUnpublishedChanges = true,
        };

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordAsync(document, FormsAuditTrail.FormDefinitionChanged, "Created");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<RecordResult<FormDefinitionDocument>> SetFieldsAsync(
        string formDefinitionId,
        IReadOnlyList<FormFieldDefinition> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (!await MayDesignAsync())
        {
            return RecordResult.NotAuthorised<FormDefinitionDocument>();
        }

        var document = await GetAsync(formDefinitionId, cancellationToken);

        if (document is null)
        {
            return RecordResult.Failed<FormDefinitionDocument>(Unknown(formDefinitionId));
        }

        var errors = await ValidateFieldsAsync(document, fields, cancellationToken);

        if (errors.Count > 0)
        {
            return RecordResult.Failed<FormDefinitionDocument>(errors);
        }

        document.Fields = [.. fields];
        document.HasUnpublishedChanges = true;

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordAsync(document, FormsAuditTrail.FormDefinitionChanged, "FieldsChanged");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<RecordResult<FormDefinitionDocument>> RenameAsync(
        string formDefinitionId,
        BilingualText name,
        BilingualText? sectionLabel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!await MayDesignAsync())
        {
            return RecordResult.NotAuthorised<FormDefinitionDocument>();
        }

        var document = await GetAsync(formDefinitionId, cancellationToken);

        if (document is null)
        {
            return RecordResult.Failed<FormDefinitionDocument>(Unknown(formDefinitionId));
        }

        var errors = await ValidateNameAsync(name, document.Code, cancellationToken);

        if (errors.Count > 0)
        {
            return RecordResult.Failed<FormDefinitionDocument>(errors);
        }

        document.Name = Trimmed(name);

        if (sectionLabel is not null)
        {
            document.SectionLabel = Trimmed(sectionLabel);
        }

        // A label is presentation and changing one is always allowed (design note §2.3) — but it
        // IS a change the compiled definition carries, because Orchard's generic screens read the
        // English half out of DisplayName. So it counts as unpublished.
        document.HasUnpublishedChanges = true;

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordAsync(document, FormsAuditTrail.FormDefinitionChanged, "Renamed");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    // ---- validation ----------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordError>> ValidateAsync(
        string formDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var document = await GetAsync(formDefinitionId, cancellationToken);

        return document is null
            ? [Unknown(formDefinitionId)]
            : await ValidateForPublishAsync(document, cancellationToken);
    }

    /// <summary>
    /// Every rule a publish applies, against the draft as it stands.
    /// </summary>
    /// <remarks>
    /// Re-run inside <see cref="PublishAsync"/> rather than trusted from the draft edit, because a
    /// draft saved last week may have become invalid since: a lookup list can be retired, and a
    /// section this definition targets can be gone. A publish that trusted the edit would compile
    /// a definition the service would refuse to accept today.
    /// </remarks>
    private async Task<IReadOnlyList<RecordError>> ValidateForPublishAsync(
        FormDefinitionDocument document,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>(await ValidateTargetAsync(
            document.Target, document.SectionItemType, document.SectionName, cancellationToken));

        errors.AddRange(await ValidateFieldsAsync(document, document.Fields, cancellationToken));

        return errors;
    }

    /// <summary>
    /// The target: a part this module may write to, and a section that is this definition's to
    /// create or to extend.
    /// </summary>
    private async Task<IReadOnlyList<RecordError>> ValidateTargetAsync(
        FormTarget target,
        string? sectionItemType,
        string? sectionName,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();
        var itemType = (sectionItemType ?? string.Empty).Trim();

        if (target == FormTarget.Record)
        {
            // Always EmployeeProfilePart, and nothing to check: the part is ours, the guard knows
            // it, and the fields themselves are checked by name below.
            return errors;
        }

        // THE TARGET GUARD, asked before anything else about the section. A definition naming
        // EmployeePart has to be refused as "the core is closed" rather than as "there is no
        // section called that" — the second is true and tells the reader to go and create one,
        // which is the one thing specification section 5 forbids. Design note §5.3 rule 5.
        var known = (await _sections.ListAsync(cancellationToken))
            .Select(section => section.ItemTypeName)
            .ToHashSet(StringComparer.Ordinal);

        if (target == FormTarget.Section &&
            EmployeeContentDefinitionGuard.CheckTarget(itemType, known) is { } refusal &&
            refusal.Kind == EmployeeDefinitionViolationKind.CoreIsClosed)
        {
            errors.Add(new RecordError(
                RecordRule.ReservedField,
                itemType,
                MessageFor(refusal),
                Field: nameof(FormDefinitionDocument.SectionItemType)));

            return errors;
        }

        if (!FormCodes.IsValidFieldName(itemType))
        {
            errors.Add(new RecordError(
                RecordRule.CodeFormat,
                itemType,
                S["'{0}' is not a valid section type name. Use a letter followed by letters and digits.", itemType],
                Field: nameof(FormDefinitionDocument.SectionItemType)));

            return errors;
        }

        if (target == FormTarget.Section)
        {
            // Extending a section means extending one that exists. A definition naming a section
            // the tenant does not have would compile into a part welded to nothing, which looks
            // exactly like a working publish until somebody opens an employee.
            if (!known.Contains(itemType))
            {
                errors.Add(new RecordError(
                    RecordRule.UnknownFormDefinition,
                    itemType,
                    S["This tenant has no section called '{0}'. Create it as a new section instead of adding fields to it.", itemType],
                    Field: nameof(FormDefinitionDocument.SectionItemType)));
            }

            return errors;
        }

        // NewSection: the opposite test. Creating one that already exists would silently take
        // ownership of somebody else's section and rewrite its label on every publish.
        if (known.Contains(itemType))
        {
            errors.Add(new RecordError(
                RecordRule.CodeUniqueness,
                itemType,
                S["This tenant already has a section called '{0}'. Add fields to it instead of creating it again.", itemType],
                Field: nameof(FormDefinitionDocument.SectionItemType)));
        }

        if (!FormCodes.IsValidFieldName((sectionName ?? string.Empty).Trim()))
        {
            errors.Add(new RecordError(
                RecordRule.CodeFormat,
                sectionName ?? string.Empty,
                S["'{0}' is not a valid section name. Use a letter followed by letters and digits.", sectionName ?? string.Empty],
                Field: nameof(FormDefinitionDocument.SectionName)));
        }

        return errors;
    }

    /// <summary>
    /// The field rules: the guard, the format, duplicates, the list behind a choice, and the two
    /// safety rules that compare against what has already been published.
    /// </summary>
    private async Task<List<RecordError>> ValidateFieldsAsync(
        FormDefinitionDocument document,
        IReadOnlyList<FormFieldDefinition> fields,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();
        var partName = PartNameFor(document);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What has already been published, which is what safety rules 2 and 3 compare against.
        // The draft is not the authority here: a field added and renamed before any publish is
        // nobody's data, and refusing that would make the designer unusable.
        var published = (await LastPublishedAsync(document, cancellationToken))
            ?.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal)
            ?? [];

        foreach (var field in fields)
        {
            var name = (field.Name ?? string.Empty).Trim();

            if (!FormCodes.IsValidFieldName(name))
            {
                errors.Add(new RecordError(
                    RecordRule.CodeFormat,
                    name,
                    S["'{0}' is not a valid field name. Use a letter followed by letters and digits — a field name is a property in Liquid, in GraphQL and in every report.", name],
                    Field: name));

                continue;
            }

            if (!seen.Add(name))
            {
                errors.Add(new RecordError(
                    RecordRule.CodeUniqueness,
                    name,
                    S["'{0}' is named twice on this form. Two fields of one name are one field.", name],
                    Field: name));

                continue;
            }

            // THE GUARD, for every field, which is design note §5.3 rule 5 and the row the Records
            // README has carried as "Not yet" since prompt 4.
            if (EmployeeContentDefinitionGuard.Check(partName, name) is { } violation)
            {
                errors.Add(new RecordError(
                    RecordRule.ReservedField,
                    name,
                    MessageFor(violation),
                    Field: name));

                continue;
            }

            errors.AddRange(await ValidateChoiceAsync(field, name, cancellationToken));
            errors.AddRange(ValidateAgainstPublished(field, name, published));
        }

        // SAFETY RULE 1, as the compiler keeps it rather than as a refusal: a field that has been
        // published and is no longer in the draft is HIDDEN, not removed. Nothing is reported and
        // nothing is deleted — PublishAsync never takes a field off a content definition. See the
        // D33 note on why this reads differently from the design note's wording.
        return errors;
    }

    /// <summary>A choice field names a list that exists and has not been superseded.</summary>
    /// <remarks>
    /// D22's addendum: a retired list is not offered for a NEW binding. The question it answered
    /// is answered by a dimension now, and a designer pointing a fresh field at <c>grade</c> would
    /// be rebuilding by hand the thing F6c retired.
    /// </remarks>
    private async Task<IReadOnlyList<RecordError>> ValidateChoiceAsync(
        FormFieldDefinition field,
        string name,
        CancellationToken cancellationToken)
    {
        if (!FormFieldKinds.NeedsAList(field.Type))
        {
            return [];
        }

        var listCode = LookupCodes.Normalise(field.ListCode);

        if (listCode.Length == 0)
        {
            return
            [
                new RecordError(
                    RecordRule.UnknownLookupList,
                    name,
                    S["'{0}' is a choice field and names no list. A choice offers the entries of a managed list.", name],
                    Field: name),
            ];
        }

        var list = await _lookupLists.GetByCodeAsync(listCode, cancellationToken);

        if (list is null)
        {
            return
            [
                new RecordError(
                    RecordRule.UnknownLookupList,
                    name,
                    S["There is no lookup list with the code '{0}' in this tenant.", listCode],
                    Field: name),
            ];
        }

        if (list.IsRetired)
        {
            return
            [
                new RecordError(
                    RecordRule.UnknownLookupList,
                    name,
                    S["The list '{0}' has been superseded and is not offered for new fields. A dimension answers that question now.", listCode],
                    Field: name),
            ];
        }

        return [];
    }

    /// <summary>
    /// Safety rules 2 and 3: a published field's type and name never change.
    /// </summary>
    /// <remarks>
    /// Both destroy data and neither warns about it. A type change leaves every stored value
    /// shaped for the old type — a date in a number, a code in a boolean — and a rename orphans
    /// every one of them under a name nothing reads. Labels are what change freely; that is what a
    /// label is for.
    /// </remarks>
    private IReadOnlyList<RecordError> ValidateAgainstPublished(
        FormFieldDefinition field,
        string name,
        Dictionary<string, FormFieldDefinition> published)
    {
        if (!published.TryGetValue(name, out var before))
        {
            return [];
        }

        if (before.Type != field.Type)
        {
            return
            [
                new RecordError(
                    RecordRule.ImmutableOnceInUse,
                    name,
                    S["The field '{0}' cannot change type once it has been published. Every value already stored in it is shaped for the type it has. Retire it and add a new field instead.", name],
                    Field: name),
            ];
        }

        if (FormFieldKinds.NeedsAList(field.Type) &&
            !LookupCodes.Same(before.ListCode, field.ListCode))
        {
            return
            [
                new RecordError(
                    RecordRule.ImmutableOnceInUse,
                    name,
                    S["The field '{0}' cannot be pointed at a different list once it has been published. Every code already stored in it belongs to the list it had.", name],
                    Field: name),
            ];
        }

        return [];
    }

    // ---- publishing ----------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<RecordResult<FormPublishResult>> PublishAsync(
        string formDefinitionId,
        CancellationToken cancellationToken = default)
    {
        if (!await MayDesignAsync())
        {
            return RecordResult.NotAuthorised<FormPublishResult>();
        }

        var document = await GetAsync(formDefinitionId, cancellationToken);

        if (document is null)
        {
            return RecordResult.Failed<FormPublishResult>(Unknown(formDefinitionId));
        }

        var errors = await ValidateForPublishAsync(document, cancellationToken);

        if (errors.Count > 0)
        {
            return RecordResult.Failed<FormPublishResult>([.. errors]);
        }

        var last = await LastPublishedAsync(document, cancellationToken);

        if (last is not null && MatchesPublished(document, last))
        {
            // NOTHING TO SAY. No version, no audit entry, no version number allocated: a version
            // is a record of a change, and a history full of identical entries is a history
            // nobody reads. ADR-0008's re-run rule depends on this being a success rather than a
            // refusal — a recipe applied twice must not fail on the second pass.
            if (document.HasUnpublishedChanges)
            {
                document.HasUnpublishedChanges = false;

                await _session.SaveCheckedAsync(document, cancellationToken);
            }

            return RecordResult.Success(new FormPublishResult(document, last, WasAlreadyPublished: true));
        }

        await CompileAsync(document, cancellationToken);

        var version = new FormVersionDocument
        {
            FormCode = document.Code,
            VersionNumber = document.PublishedVersion + 1,
            Name = document.Name,
            Target = document.Target,
            SectionItemType = document.SectionItemType,
            SectionName = document.SectionName,
            SectionLabel = document.SectionLabel,
            Fields = [.. document.Fields],
            PublishedOnUtc = DateTime.UtcNow,
            // Left to the audit entry beside it, which resolves the acting user from the request.
            // This column is the version list's own, and a name it had to invent would be worse
            // than an empty one — see FormVersionDocument.PublishedBy.
            PublishedBy = string.Empty,
        };

        await _session.SaveCheckedAsync(version, cancellationToken);

        document.PublishedVersion = version.VersionNumber;
        document.HasUnpublishedChanges = false;

        await _session.SaveCheckedAsync(document, cancellationToken);

        await RecordAsync(
            document,
            FormsAuditTrail.FormDefinitionPublished,
            $"Published v{version.VersionNumber}");

        await InvalidateCacheAsync();

        return RecordResult.Success(new FormPublishResult(document, version, WasAlreadyPublished: false));
    }

    /// <summary>
    /// Writes the definition onto the tenant's content definitions. Additive, always.
    /// </summary>
    /// <remarks>
    /// <b>There is no code here that removes anything</b>, and that is the enforcement of the
    /// brief's rule rather than a comment about it: no field is taken off a part, no part is taken
    /// off <c>Employee</c>, no value is touched. A field the definition no longer lists keeps its
    /// definition and its data and simply stops being rendered, because B4's editor renders from
    /// the definition.
    ///
    /// <b>Every read is a <c>Load…</c></b> — ADR-0006 — because this method alters definitions and
    /// anything downstream in this scope would otherwise read a cache that cannot see the change.
    /// </remarks>
    private async Task CompileAsync(FormDefinitionDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var partName = PartNameFor(document);

        await _contentDefinitionManager.AlterPartDefinitionAsync(partName, part =>
        {
            // WithSettings MERGES on a part builder, so a shipped section's own display name and
            // description survive a definition that only adds fields to it.
            part.WithSettings(new ContentPartSettings
            {
                // Like EmployeePart and like every shipped section: Orchard's own type editor does
                // not offer it in "Add parts", so an administrator cannot weld a designer section
                // onto another content type. Design note §5.3 rule 6.
                Attachable = false,
                DisplayName = document.SectionLabel.En.Length > 0
                    ? document.SectionLabel.En
                    : document.Name.En,
            });

            var position = 0;

            foreach (var field in document.Fields)
            {
                var editor = FormFieldKinds.EditorNameFor(field.Type);
                var order = position++;

                part.WithField(field.Name, builder =>
                {
                    builder
                        .OfType(FormFieldKinds.FieldTypeNameFor(field.Type))
                        .WithSettings(new ContentPartFieldSettings
                        {
                            // The ENGLISH half only, and only as a fallback for Orchard's own
                            // generic screens — design note §2.3. ContentPartFieldSettings carries
                            // single strings, so a bilingual product cannot put its label there;
                            // the definition is the source of truth and our editors render from
                            // it, which is also why a culture switch needs no republish.
                            DisplayName = field.Label.En,
                            Description = field.Help.En,

                            // So Orchard's own editors honour the designer's order too.
                            Position = order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        });

                    if (editor.Length > 0)
                    {
                        builder.WithEditor(editor);
                    }

                    if (FormFieldKinds.NeedsAList(field.Type))
                    {
                        builder.WithSettings(new LookupFieldSettings
                        {
                            ListCode = LookupCodes.Normalise(field.ListCode),
                        });
                    }
                });
            }
        });

        if (document.Target == FormTarget.Record)
        {
            // The part is welded onto Employee on the first record-level publish and left alone
            // afterwards. AlterTypeDefinitionAsync is idempotent, so saying it every time is
            // cheaper than asking whether it has been said.
            await _contentDefinitionManager.AlterTypeDefinitionAsync(
                EmployeeFieldNames.ContentType,
                type => type.WithPart(nameof(EmployeeProfilePart)));

            _changes.MarkAltered(EmployeeFieldNames.ContentType);

            return;
        }

        if (document.Target == FormTarget.Section)
        {
            // Nothing else to do: the type and the bag already exist, which is what
            // ValidateTargetAsync established.
            return;
        }

        await CompileNewSectionAsync(document);
    }

    /// <summary>
    /// A section of the tenant's own: an item type shaped exactly like the six the product ships,
    /// and a bag for it on <c>Employee</c>.
    /// </summary>
    /// <remarks>
    /// <b>The same shape as <c>Migrations.SectionTypeAsync</c></b>, deliberately: the stereotype,
    /// not creatable, not listable, not draftable, a generated read-only title. A designer section
    /// that differed from a shipped one in any of those would behave differently on Orchard's own
    /// content screens for no reason a customer could discover.
    ///
    /// <b>The title pattern names the first field</b>, so a bag row has something in its collapsed
    /// header. A section with no fields yet gets no pattern rather than a broken one — and
    /// <c>EmployeeHandler</c> is the authority for an employee's own title for the reason
    /// <c>DimensionRecordHandler.SetDisplayText</c> records: Liquid patterns do not render outside
    /// an HTTP request, so a pattern is a fallback and never the mechanism.
    /// </remarks>
    private async Task CompileNewSectionAsync(FormDefinitionDocument document)
    {
        var itemType = document.SectionItemType;

        var titleField = document.Fields.FirstOrDefault(field => !field.IsRetired)?.Name;

        await _contentDefinitionManager.AlterTypeDefinitionAsync(itemType, type =>
        {
            type
                .WithDisplayName(document.SectionLabel.En.Length > 0
                    ? document.SectionLabel.En
                    : document.Name.En)
                .WithPart(itemType)
                .WithPart(nameof(TitlePart), title => title.WithSettings(new TitlePartSettings
                {
                    Options = TitlePartOptions.GeneratedDisabled,
                    Pattern = titleField is null
                        ? string.Empty
                        : $"{{{{ ContentItem.Content.{itemType}.{titleField} }}}}",
                    RenderTitle = false,
                }))
                .WithSettings(new ContentTypeSettings
                {
                    // Never on its own, exactly as a shipped section row is never on its own: a
                    // listable one would turn up in the content picker of every form in the tenant.
                    Creatable = false,
                    Listable = false,
                    Securable = false,
                    Draftable = false,
                    Stereotype = EmployeeSections.Stereotype,
                });
        });

        await _contentDefinitionManager.AlterTypeDefinitionAsync(
            EmployeeFieldNames.ContentType,
            type => type.WithPart(document.SectionName, nameof(BagPart), bag => bag
                .WithSettings(new ContentTypePartSettings { DisplayName = document.SectionName })
                .WithSettings(new BagPartSettings
                {
                    // One kind of row per section. Without this the bag offers every creatable
                    // type in the tenant, so a designer section would invite somebody to add a
                    // blog post to an employee.
                    ContainedContentTypes = [itemType],
                    CollapseContainedItems = true,
                })));

        // ADR-0006: this scope has just moved Employee under its own cached read, and anything
        // creating an employee after this line would otherwise get the OLD parts and silently
        // drop everything written into the section just created.
        _changes.MarkAltered(EmployeeFieldNames.ContentType);
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>The part a definition writes its fields onto.</summary>
    private static string PartNameFor(FormDefinitionDocument document) =>
        document.Target == FormTarget.Record
            ? nameof(EmployeeProfilePart)

            // Orchard's own convention, which the six shipped sections follow: the fields of a
            // section live on a part named for its item type, so a shared part cannot put every
            // section's fields on every section.
            : document.SectionItemType;

    /// <summary>The most recent published version, or null while this has never been published.</summary>
    private async Task<FormVersionDocument?> LastPublishedAsync(
        FormDefinitionDocument document,
        CancellationToken cancellationToken)
    {
        if (document.PublishedVersion == 0)
        {
            return null;
        }

        var folded = FormCodes.Normalise(document.Code).ToUpperInvariant();

        return await _session
            .Query<FormVersionDocument, FormVersionIndex>(index =>
                index.FormCodeUpper == folded && index.VersionNumber == document.PublishedVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the draft says exactly what the last published version says.
    /// </summary>
    /// <remarks>
    /// Compared field by field rather than by a hash, because the answer decides whether a version
    /// is written and a hash that collided would silently lose one. The comparison covers
    /// everything a version carries, which is everything the compiler reads.
    /// </remarks>
    private static bool MatchesPublished(FormDefinitionDocument document, FormVersionDocument published) =>
        document.Name == published.Name &&
        document.Target == published.Target &&
        string.Equals(document.SectionItemType, published.SectionItemType, StringComparison.Ordinal) &&
        string.Equals(document.SectionName, published.SectionName, StringComparison.Ordinal) &&
        document.SectionLabel == published.SectionLabel &&
        document.Fields.Count == published.Fields.Count &&
        document.Fields.Zip(published.Fields).All(pair => pair.First == pair.Second);

    private async Task<bool> MayDesignAsync() =>
        await _authorisation.AuthoriseAsync(Permissions.ManageFormDefinitions) &&
        await _entitlement.IsAllowedAsync(Capabilities.FormDesigner);

    /// <summary>
    /// English required, Arabic optional unless the tenant says otherwise — ADR-0003's addendum.
    /// </summary>
    /// <remarks>
    /// Through <see cref="IBilingualNamePolicy"/>, the one-question seam every other write path on
    /// this platform uses, so the form designer and the organisation designer cannot disagree
    /// about whether this tenant insists on Arabic.
    /// </remarks>
    private async Task<IReadOnlyList<RecordError>> ValidateNameAsync(
        BilingualText name,
        string code,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();

        if (string.IsNullOrWhiteSpace(name.En))
        {
            errors.Add(new RecordError(
                RecordRule.NameRequired,
                code,
                S["A form definition needs an English name."],
                Field: "NameEn"));
        }

        if (await _namePolicy.RequiresArabicAsync(cancellationToken) &&
            string.IsNullOrWhiteSpace(name.Ar))
        {
            errors.Add(new RecordError(
                RecordRule.NameRequired,
                code,
                S["This tenant requires an Arabic name."],
                Field: "NameAr"));
        }

        return errors;
    }

    private static BilingualText Trimmed(BilingualText name) =>
        new((name.En ?? string.Empty).Trim(), (name.Ar ?? string.Empty).Trim());

    private RecordError Unknown(string formDefinitionId) => new(
        RecordRule.UnknownFormDefinition,
        formDefinitionId,
        S["There is no form definition with the id '{0}' in this tenant.", formDefinitionId]);

    /// <summary>
    /// The guard's refusal, localised. The guard's own <c>Describe</c> is for a developer.
    /// </summary>
    /// <remarks>
    /// <c>EmployeeDefinitionViolation.Describe</c> is deliberately not localised: its readers are
    /// a failing migration and a test. A designer's refusal is read by an administrator in their
    /// own language, on the field they typed, so the sentence is composed here where the localiser
    /// is — CLAUDE.md rule 7.
    /// </remarks>
    private LocalizedString MessageFor(EmployeeDefinitionViolation violation) => violation.Kind switch
    {
        EmployeeDefinitionViolationKind.CoreIsClosed =>
            S["The employee record's fixed core takes no added fields. Payroll, leave and attendance depend on its fields by name. Add them to a section instead."],

        EmployeeDefinitionViolationKind.ReservedName =>
            S["A field cannot be called '{0}': the name belongs to the employee record's fixed core and would read as that field everywhere.", violation.FieldName],

        EmployeeDefinitionViolationKind.PlacementBelongsInTheDimensionEngine =>
            S["A field cannot be called '{0}': where somebody sits, what post they hold and who they report to are dated rows in the organisation engine, not fields on the record.", violation.FieldName],

        EmployeeDefinitionViolationKind.RetiredByTheDimension =>
            S["A field cannot be called '{0}': that name was retired, and the Position and Grade dimensions answer for it now.", violation.FieldName],

        _ =>
            S["A form definition cannot add fields to '{0}'.", violation.PartName],
    };

    /// <summary>
    /// Invalidates the cached reads after every write, after the commit. ADR-0019.
    /// </summary>
    /// <remarks>
    /// Through <c>CacheSignals.SignalNowAndAfterCommitAsync</c> and never
    /// <c>ISignal.SignalTokenAsync</c> alone, for the reason <c>LookupListService</c> records at
    /// length: a write is staged on the session and committed when the scope is disposed, so an
    /// immediate signal lands before the commit and a read arriving in between caches the state
    /// the write is about to replace, under a token nothing will signal again.
    /// <c>CacheInvalidationSignalTests</c> fails the build if anything here calls the bare signal.
    /// </remarks>
    private Task InvalidateCacheAsync() =>
        _signal.SignalNowAndAfterCommitAsync(RecordsCacheKeys.FormDefinitionsSignal(_shellSettings.Name));

    /// <summary>
    /// Files the audit entry. Specification section 9 names a content-definition change as a
    /// WorkMate audit event, and publishing is the event that produces one.
    /// </summary>
    private Task RecordAsync(FormDefinitionDocument document, string name, string operation) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<AuditTrailFormDefinitionEvent>(
            name,
            FormsAuditTrail.Category,
            document.FormDefinitionId,

            // Null, like every other audit entry in this module: the acting user is resolved by
            // Orchard's own audit trail from the request, and a service that passed its own
            // answer would be a second source for the one fact an auditor must be able to trust.
            userId: null,
            userName: null,
            new AuditTrailFormDefinitionEvent
            {
                Code = document.Code,
                NameEn = document.Name.En,
                Target = document.Target.ToString(),
                SectionItemType = document.SectionItemType,
                PublishedVersion = document.PublishedVersion,
                FieldCount = document.Fields.Count,
                Operation = operation,
            }));
}
