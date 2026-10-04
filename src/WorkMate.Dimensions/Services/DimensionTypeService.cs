using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;
using IIdGenerator = OrchardCore.Entities.IIdGenerator;
using OrchardCore.Title.Models;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using YesSql;

namespace WorkMate.Dimensions.Services;

/// <inheritdoc />
public sealed class DimensionTypeService : IDimensionTypeService
{
    /// <summary>
    /// The part every generated dimension content type carries. Its definition is created by
    /// this module's migration; this service only attaches it.
    /// </summary>
    public const string DimensionRecordPartName = "DimensionRecordPart";

    /// <summary>Orchard's title part, attached so a generated type lists by name in the admin.</summary>
    private const string TitlePartName = "TitlePart";

    /// <summary>
    /// The Liquid pattern that drives a record's title from its English name.
    /// </summary>
    /// <remarks>
    /// Specification section 4 asks for "TitlePart bound to NameEn". Orchard 3.0.1 has no literal
    /// binding of a title to another field; what it has is a generated title with a Liquid
    /// pattern, which is the mechanism that produces the same effect. Paired with
    /// <see cref="TitlePartOptions.GeneratedDisabled"/> the editor shows the title read-only and
    /// regenerates it on every save, so the two can never disagree.
    /// </remarks>
    private const string TitlePatternForNameEn =
        "{{ ContentItem.Content." + DimensionRecordPartName + ".NameEn.Text }}";

    private readonly ISession _session;
    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly IAuditTrailManager _auditTrailManager;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IIdGenerator _idGenerator;
    private readonly IStringLocalizer S;

    /// <summary>
    /// The validator, resolved on first use. It needs to look types up to answer "does this
    /// reference exist", so the dependency genuinely runs both ways; see the same note on
    /// <see cref="StructureService"/> for why deferring beats letting either side read the
    /// other's tables.
    /// </summary>
    private readonly Lazy<IDimensionValidator> _validatorSource;

    private IDimensionValidator _validator => _validatorSource.Value;

    public DimensionTypeService(
        ISession session,
        IContentDefinitionManager contentDefinitionManager,
        IAuditTrailManager auditTrailManager,
        IDimensionAuthorisation authorisation,
        Lazy<IDimensionValidator> validator,
        IIdGenerator idGenerator,
        IStringLocalizer<DimensionTypeService> stringLocalizer)
    {
        _session = session;
        _contentDefinitionManager = contentDefinitionManager;
        _auditTrailManager = auditTrailManager;
        _authorisation = authorisation;
        _validatorSource = validator;
        _idGenerator = idGenerator;
        S = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<DimensionResult<DimensionTypeDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        bool allowsSelfNesting,
        bool isSystemDefined = false,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributeSchema);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionTypes))
        {
            return DimensionResult.NotAuthorised<DimensionTypeDocument>();
        }

        // Every rule lives in IDimensionValidator so the designer, the API and every import
        // path get the same answer from the same code. Architecture section 6: the service is
        // the authority and re-checks on write.
        var errors = await _validator.ValidateDimensionTypeAsync(
            dimensionTypeId: null,
            code,
            name,
            attributeSchema,
            batch,
            cancellationToken);

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<DimensionTypeDocument>(errors);
        }

        var contentTypeName = DimensionCodes.ToContentTypeName(code);

        // Load, not Get: a type created earlier in this same scope — by a recipe importing
        // several at once, most obviously — is not yet in the cached definition.
        if (await _contentDefinitionManager.LoadTypeDefinitionAsync(contentTypeName) is not null)
        {
            return DimensionResult.Failed<DimensionTypeDocument>(new DimensionError(
                DimensionRule.CodeUniqueness,
                code,
                S["The code '{0}' would create the content type '{1}', which already exists in this tenant. Choose another code.",
                    code,
                    contentTypeName]));
        }

        var document = new DimensionTypeDocument
        {
            DimensionTypeId = _idGenerator.GenerateUniqueId(),
            Code = code,
            Name = name,
            IsSystemDefined = isSystemDefined,
            AllowsSelfNesting = allowsSelfNesting,
            AttributeSchema = [.. attributeSchema],
            ContentTypeName = contentTypeName,
        };

        await _session.SaveCheckedAsync(document, cancellationToken);

        var diff = await WriteContentDefinitionAsync(document);

        await RecordTypeChangeAsync(document, before: null);
        await RecordContentDefinitionChangeAsync(document, diff);

        return DimensionResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<DimensionResult<DimensionTypeDocument>> UpdateAsync(
        string dimensionTypeId,
        BilingualText name,
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        bool allowsSelfNesting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributeSchema);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionTypes))
        {
            return DimensionResult.NotAuthorised<DimensionTypeDocument>();
        }

        var document = await LoadAsync(dimensionTypeId, cancellationToken);

        if (document is null)
        {
            return DimensionResult.Failed<DimensionTypeDocument>(UnknownType(dimensionTypeId));
        }

        var errors = new List<DimensionError>(await _validator.ValidateDimensionTypeAsync(
            dimensionTypeId,
            document.Code,
            name,
            attributeSchema,
            batch: null,
            cancellationToken));

        // Removing a field from a live content definition destroys the data in it, and open
        // question 3 of the dimension engine architecture — how much runtime definition churn is
        // permitted, and what guard rails apply to destructive changes — has not been answered.
        // Until it is, this refuses rather than guesses. A customer who needs a field gone can
        // stop using it; a customer who needs it gone for good needs the answer to that question
        // first.
        var removed = document.AttributeSchema
            .Select(attribute => attribute.Name)
            .Except(attributeSchema.Select(attribute => attribute.Name), StringComparer.Ordinal)
            .OrderBy(attributeName => attributeName, StringComparer.Ordinal)
            .ToList();

        foreach (var attributeName in removed)
        {
            errors.Add(new DimensionError(
                DimensionRule.ImmutableOnceInUse,
                attributeName,
                S["The attribute '{0}' cannot be removed from the dimension type '{1}'. Removing a field deletes the data held in it, and that is not permitted until the guard rails for destructive definition changes are agreed.",
                    attributeName,
                    document.Code]));
        }

        // Changing an attribute's kind rewrites the field type under data already stored in it,
        // which is the same destruction by another route.
        foreach (var attribute in attributeSchema)
        {
            var existing = document.AttributeSchema
                .FirstOrDefault(candidate => string.Equals(candidate.Name, attribute.Name, StringComparison.Ordinal));

            if (existing is not null && existing.Kind != attribute.Kind)
            {
                errors.Add(new DimensionError(
                    DimensionRule.ImmutableOnceInUse,
                    attribute.Name,
                    S["The attribute '{0}' is a {1} and cannot be changed to a {2}. Add a new attribute instead.",
                        attribute.Name,
                        existing.Kind.ToString(),
                        attribute.Kind.ToString()]));
            }
        }

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<DimensionTypeDocument>(errors);
        }

        var before = DimensionTypeState.Of(document);

        document.Name = name;
        document.AllowsSelfNesting = allowsSelfNesting;
        document.AttributeSchema = [.. attributeSchema];

        await _session.SaveCheckedAsync(document, cancellationToken);

        var diff = await WriteContentDefinitionAsync(document);

        await RecordTypeChangeAsync(document, before);

        if (!diff.IsEmpty)
        {
            await RecordContentDefinitionChangeAsync(document, diff);
        }

        return DimensionResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<DimensionResult<DimensionTypeDocument>> RetireAsync(
        string dimensionTypeId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionTypes))
        {
            return DimensionResult.NotAuthorised<DimensionTypeDocument>();
        }

        var document = await LoadAsync(dimensionTypeId, cancellationToken);

        if (document is null)
        {
            return DimensionResult.Failed<DimensionTypeDocument>(UnknownType(dimensionTypeId));
        }

        // A system-defined type is named by the recipes that ship with the product. Retiring one
        // would leave those recipes referring to a type nothing will offer, and the failure would
        // surface at the next tenant's setup rather than here.
        if (document.IsSystemDefined)
        {
            return DimensionResult.Failed<DimensionTypeDocument>(new DimensionError(
                DimensionRule.ImmutableOnceInUse,
                document.Code,
                S["The dimension type '{0}' is defined by WorkMate and cannot be retired.", document.Code]));
        }

        var before = DimensionTypeState.Of(document);

        document.RetiredOn = effectiveDate;

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordTypeChangeAsync(document, before);

        return DimensionResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<DimensionTypeDocument?> GetAsync(
        string dimensionTypeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = await LoadAsync(dimensionTypeId, cancellationToken);

        return await IsVisibleAsync(document, asAt) ? document : null;
    }

    /// <inheritdoc />
    public async Task<DimensionTypeDocument?> GetByCodeAsync(
        string code,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>(index => index.Code == code)
            .FirstOrDefaultAsync(cancellationToken);

        return await IsVisibleAsync(document, asAt) ? document : null;
    }

    /// <inheritdoc />
    public async Task<DimensionTypeDocument?> GetByContentTypeAsync(
        string contentTypeName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Not dated. The handler that calls this needs to know whether a content item is a
        // dimension record at all, and a record of a retired type is still a dimension record.
        return await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>(index => index.ContentTypeName == contentTypeName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DimensionTypeDocument>> ListAsync(
        DateOnly? asAt = null,
        bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var documents = await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>()
            .OrderBy(index => index.Code)
            .ListAsync(cancellationToken);

        if (includeRetired)
        {
            return [.. documents];
        }

        var effective = asAt ?? await _authorisation.TodayAsync();

        return [.. documents.Where(document => !document.IsRetiredOn(effective))];
    }

    private async Task<bool> IsVisibleAsync(DimensionTypeDocument? document, DateOnly? asAt)
    {
        if (document is null)
        {
            return false;
        }

        var effective = asAt ?? await _authorisation.TodayAsync();

        return !document.IsRetiredOn(effective);
    }

    private async Task<DimensionTypeDocument?> LoadAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken) =>
        await _session
            .Query<DimensionTypeDocument, DimensionTypeIndex>(index => index.DimensionTypeId == dimensionTypeId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> RetiredTypeWithCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        await _session
            .QueryIndex<DimensionTypeIndex>(index => index.Code == code && index.RetiredOn != null)
            .CountAsync(cancellationToken) > 0;

    /// <summary>
    /// Creates or brings into line the content type behind <paramref name="document"/>, and
    /// returns what changed so the caller can audit it.
    /// </summary>
    /// <remarks>
    /// The fields a dimension type declares go on a part named for the content type rather than
    /// onto <c>DimensionRecordPart</c>, which is the convention Orchard's own type editor
    /// follows. It matters here: <c>DimensionRecordPart</c> is shared by every generated type, so
    /// a field added to it would appear on all of them.
    ///
    /// In Orchard Core 3.0.1 the alteration methods are extensions on
    /// <c>ContentDefinitionManagerExtensions</c>, not members of <c>IContentDefinitionManager</c>,
    /// which is the one place this differs from how specification section 4 describes it.
    ///
    /// Both snapshots are read with <c>LoadTypeDefinitionAsync</c>, not
    /// <c>GetTypeDefinitionAsync</c>. The two are not interchangeable: Get serves a cached,
    /// read-only definition that does not see a change made in the current scope, so reading the
    /// "after" state with it returns the definition as it was before — or null, on a creation.
    /// Load serves the uncached one, which is what any write path needs. The same distinction
    /// exists on site settings, where WorkMate.Platform's settings service uses
    /// <c>LoadSiteSettingsAsync</c> for exactly this reason.
    /// </remarks>
    private async Task<ContentTypeDefinitionDiff> WriteContentDefinitionAsync(DimensionTypeDocument document)
    {
        var typeName = document.ContentTypeName;
        var before = ContentTypeDefinitionSnapshot.Of(
            await _contentDefinitionManager.LoadTypeDefinitionAsync(typeName));

        await _contentDefinitionManager.AlterPartDefinitionAsync(typeName, part =>
        {
            part.WithSettings(new ContentPartSettings
            {
                Attachable = false,
                DisplayName = document.Name.En,
                Description = $"Fields declared by the dimension type '{document.Code}'.",
            });

            foreach (var attribute in document.AttributeSchema)
            {
                part.WithField(attribute.Name, field => field
                    .OfType(DimensionAttributeKinds.FieldTypeNameFor(attribute.Kind))
                    .WithSettings(new ContentPartFieldSettings
                    {
                        DisplayName = attribute.Label.En,
                    }));
            }
        });

        await _contentDefinitionManager.AlterTypeDefinitionAsync(typeName, type =>
        {
            type
                .WithDisplayName(document.Name.En)
                .WithPart(DimensionRecordPartName)
                .WithPart(TitlePartName, title => title.WithSettings(new TitlePartSettings
                {
                    // Generated and read-only: the title is the English name, and an editable
                    // title would let the two drift apart silently.
                    Options = TitlePartOptions.GeneratedDisabled,
                    Pattern = TitlePatternForNameEn,
                    RenderTitle = true,
                }))
                .WithPart(typeName)
                .WithSettings(new ContentTypeSettings
                {
                    Creatable = true,
                    Listable = true,
                    Securable = true,

                    // Not draftable, per specification section 4. A half-saved department is not
                    // a thing: a record either exists in the structure or it does not, and a
                    // draft would be invisible to the closure index while looking real in the
                    // editor.
                    Draftable = false,
                });
        });

        var after = ContentTypeDefinitionSnapshot.Of(
            await _contentDefinitionManager.LoadTypeDefinitionAsync(typeName))
            ?? throw new InvalidOperationException(
                $"The content type '{typeName}' was not readable immediately after being written.");

        return new ContentTypeDefinitionDiff(before, after);
    }

    private Task RecordTypeChangeAsync(DimensionTypeDocument document, DimensionTypeState? before) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<DimensionTypeAuditEvent>(
            DimensionAuditTrail.DimensionTypeChanged,
            DimensionAuditTrail.Category,
            document.DimensionTypeId,
            userId: null,
            userName: null,
            new DimensionTypeAuditEvent
            {
                DimensionTypeId = document.DimensionTypeId,
                Code = document.Code,
                Before = before,
                After = DimensionTypeState.Of(document),
            }));

    private Task RecordContentDefinitionChangeAsync(
        DimensionTypeDocument document,
        ContentTypeDefinitionDiff diff) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<ContentDefinitionAuditEvent>(
            DimensionAuditTrail.ContentDefinitionChanged,
            DimensionAuditTrail.Category,
            document.DimensionTypeId,
            userId: null,
            userName: null,
            new ContentDefinitionAuditEvent
            {
                DimensionTypeId = document.DimensionTypeId,
                ContentTypeName = document.ContentTypeName,
                Diff = diff,
            }));

    /// <summary>
    /// The names <c>DimensionRecordPart</c> occupies on every generated type. Kept as a list
    /// rather than read from the part's definition because this has to be checkable before the
    /// part exists, and because a test asserts the two agree.
    /// </summary>
    internal static readonly IReadOnlySet<string> StandardFieldNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Code",
            "NameEn",
            "NameAr",
            "DimensionTypeId",
            "EffectiveFrom",
            "EffectiveTo",
            "IsActive",
            "SortOrder",
            "CostCentreCode",
            "GlAccountRef",
            "HeadEmployeeId",
        };

    private DimensionError UnknownType(string dimensionTypeId) => new(
        DimensionRule.UnknownReference,
        dimensionTypeId,
        S["There is no dimension type with the id '{0}' in this tenant.", dimensionTypeId]);
}
