using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using IIdGenerator = OrchardCore.Entities.IIdGenerator;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using YesSql;

namespace WorkMate.Dimensions.Services;

/// <inheritdoc />
public sealed class StructureService : IStructureService
{
    private readonly ISession _session;
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IAuditTrailManager _auditTrailManager;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IIdGenerator _idGenerator;
    private readonly IStringLocalizer S;

    public StructureService(
        ISession session,
        IDimensionTypeService dimensionTypeService,
        IAuditTrailManager auditTrailManager,
        IDimensionAuthorisation authorisation,
        IIdGenerator idGenerator,
        IStringLocalizer<StructureService> stringLocalizer)
    {
        _session = session;
        _dimensionTypeService = dimensionTypeService;
        _auditTrailManager = auditTrailManager;
        _authorisation = authorisation;
        _idGenerator = idGenerator;
        S = stringLocalizer;
    }

    /// <inheritdoc />
    public async Task<DimensionResult<StructureDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        bool isPrimaryOrganisation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(levelDimensionTypeIds);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageStructures))
        {
            return DimensionResult.NotAuthorised<StructureDocument>();
        }

        var errors = new List<DimensionError>();

        if (!DimensionCodes.IsValidCode(code))
        {
            errors.Add(new DimensionError(
                DimensionRule.CodeFormat,
                code ?? string.Empty,
                S["A structure code must start with a letter and may contain letters, digits, hyphens and underscores, up to fifty characters."]));
        }
        else if (await GetByCodeAsync(code, cancellationToken) is not null)
        {
            errors.Add(new DimensionError(
                DimensionRule.CodeUniqueness,
                code,
                S["A structure with the code '{0}' already exists in this tenant.", code]));
        }

        errors.AddRange(ValidateName(name, code ?? string.Empty));
        errors.AddRange(await ValidateLevelsAsync(levelDimensionTypeIds, code ?? string.Empty, cancellationToken));
        errors.AddRange(await ValidatePrimaryOrganisationAsync(isPrimaryOrganisation, structureIdBeingWritten: null, code ?? string.Empty));

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<StructureDocument>(errors);
        }

        var document = new StructureDocument
        {
            StructureId = _idGenerator.GenerateUniqueId(),
            Code = code!,
            Name = name,
            Levels = ToLevels(levelDimensionTypeIds),
            AllowSkipLevel = allowSkipLevel,
            IsStrict = isStrict,
            IsPrimaryOrganisation = isPrimaryOrganisation,
        };

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordChangeAsync(document, before: null, cancellationToken);

        return DimensionResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<DimensionResult<StructureDocument>> UpdateAsync(
        string structureId,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool allowSkipLevel,
        bool isStrict,
        bool isPrimaryOrganisation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(levelDimensionTypeIds);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageStructures))
        {
            return DimensionResult.NotAuthorised<StructureDocument>();
        }

        var document = await GetAsync(structureId, cancellationToken);

        if (document is null)
        {
            return DimensionResult.Failed<StructureDocument>(new DimensionError(
                DimensionRule.UnknownReference,
                structureId,
                S["There is no structure with the id '{0}' in this tenant.", structureId]));
        }

        var errors = new List<DimensionError>();

        errors.AddRange(ValidateName(name, document.Code));
        errors.AddRange(await ValidateLevelsAsync(levelDimensionTypeIds, document.Code, cancellationToken));
        errors.AddRange(await ValidatePrimaryOrganisationAsync(isPrimaryOrganisation, structureId, document.Code));

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<StructureDocument>(errors);
        }

        var typeCodes = await TypeCodesByIdAsync(cancellationToken);
        var before = StructureState.Of(document, typeCodes);

        document.Name = name;
        document.Levels = ToLevels(levelDimensionTypeIds);
        document.AllowSkipLevel = allowSkipLevel;
        document.IsStrict = isStrict;
        document.IsPrimaryOrganisation = isPrimaryOrganisation;

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordChangeAsync(document, before, cancellationToken);

        return DimensionResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<StructureDocument?> GetAsync(
        string structureId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _session
            .Query<StructureDocument, StructureIndex>(index => index.StructureId == structureId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StructureDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _session
            .Query<StructureDocument, StructureIndex>(index => index.Code == code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StructureDocument?> GetPrimaryOrganisationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _session
            .Query<StructureDocument, StructureIndex>(index => index.IsPrimaryOrganisation)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StructureDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return
        [
            .. await _session
                .Query<StructureDocument, StructureIndex>()
                .OrderBy(index => index.Code)
                .ListAsync(cancellationToken),
        ];
    }

    /// <summary>
    /// Turns the caller's root-first list into levels with contiguous ordinals.
    /// </summary>
    /// <remarks>
    /// Ordinals are assigned here rather than accepted from the caller because the level rules in
    /// architecture section 6 are arithmetic on them — a child is one level below its parent
    /// unless the structure allows skipping — and arithmetic on ordinals a caller supplied with a
    /// gap in them gives the wrong answer silently.
    /// </remarks>
    private static IReadOnlyList<StructureLevel> ToLevels(IReadOnlyList<string> dimensionTypeIds) =>
        [.. dimensionTypeIds.Select((dimensionTypeId, ordinal) => new StructureLevel(ordinal, dimensionTypeId))];

    private IEnumerable<DimensionError> ValidateName(BilingualText name, string subject)
    {
        if (string.IsNullOrWhiteSpace(name.En) || string.IsNullOrWhiteSpace(name.Ar))
        {
            yield return new DimensionError(
                DimensionRule.NameRequired,
                subject,
                S["A name is required in both English and Arabic."]);
        }
    }

    private async Task<IReadOnlyList<DimensionError>> ValidateLevelsAsync(
        IReadOnlyList<string> levelDimensionTypeIds,
        string subject,
        CancellationToken cancellationToken)
    {
        var errors = new List<DimensionError>();

        if (levelDimensionTypeIds.Count == 0)
        {
            errors.Add(new DimensionError(
                DimensionRule.StructureLevels,
                subject,
                S["A structure needs at least one level."]));

            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dimensionTypeId in levelDimensionTypeIds)
        {
            if (!seen.Add(dimensionTypeId))
            {
                errors.Add(new DimensionError(
                    DimensionRule.StructureLevels,
                    dimensionTypeId,
                    S["A dimension type may appear only once in a structure's levels."]));

                continue;
            }

            // Retired types are allowed as levels of a structure that still has to resolve
            // historical placements under them; what is not allowed is a level naming a type
            // that never existed.
            var type = await _dimensionTypeService.GetAsync(dimensionTypeId, asAt: null, cancellationToken)
                ?? await _dimensionTypeService.GetAsync(dimensionTypeId, asAt: DateOnly.MinValue, cancellationToken);

            if (type is null)
            {
                errors.Add(new DimensionError(
                    DimensionRule.UnknownReference,
                    dimensionTypeId,
                    S["There is no dimension type with the id '{0}' in this tenant.", dimensionTypeId]));
            }
        }

        return errors;
    }

    /// <summary>
    /// At most one structure in a tenant is the primary organisation axis, because approvals and
    /// data visibility default to "the" primary one and a tenant with two has no default at all.
    /// </summary>
    private async Task<IReadOnlyList<DimensionError>> ValidatePrimaryOrganisationAsync(
        bool isPrimaryOrganisation,
        string? structureIdBeingWritten,
        string subject)
    {
        if (!isPrimaryOrganisation)
        {
            return [];
        }

        var existing = await GetPrimaryOrganisationAsync();

        if (existing is null ||
            string.Equals(existing.StructureId, structureIdBeingWritten, StringComparison.Ordinal))
        {
            return [];
        }

        return
        [
            new DimensionError(
                DimensionRule.SinglePrimaryOrganisation,
                subject,
                S["'{0}' is already the primary organisation structure. Clear that first, then set this one.",
                    existing.Code]),
        ];
    }

    private async Task<IReadOnlyDictionary<string, string>> TypeCodesByIdAsync(CancellationToken cancellationToken)
    {
        var types = await _dimensionTypeService.ListAsync(
            asAt: null,
            includeRetired: true,
            cancellationToken);

        return types.ToDictionary(type => type.DimensionTypeId, type => type.Code, StringComparer.Ordinal);
    }

    private async Task RecordChangeAsync(
        StructureDocument document,
        StructureState? before,
        CancellationToken cancellationToken)
    {
        var typeCodes = await TypeCodesByIdAsync(cancellationToken);

        await _auditTrailManager.RecordEventAsync(new AuditTrailContext<StructureAuditEvent>(
            DimensionAuditTrail.StructureChanged,
            DimensionAuditTrail.Category,
            document.StructureId,
            userId: null,
            userName: null,
            new StructureAuditEvent
            {
                StructureId = document.StructureId,
                Code = document.Code,
                Before = before,
                After = StructureState.Of(document, typeCodes),
            }));
    }
}
