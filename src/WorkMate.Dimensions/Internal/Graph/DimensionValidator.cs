using Microsoft.Extensions.Localization;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using YesSql;

namespace WorkMate.Dimensions.Internal.Graph;

/// <inheritdoc />
/// <remarks>
/// Internal because it reads the graph tables to answer placement and deletion questions, and
/// those tables are the module's own. Only the interface is public.
///
/// Depends on the three read-only lookups, never on <c>IDimensionTypeService</c>,
/// <c>IStructureService</c> or <c>IDimensionService</c>. Those write services all depend on this
/// validator; depending back on any of them would be the cycle the module README's lookup
/// pattern exists to rule out. The lookups have no dependency on the validator or on each other,
/// so nothing here can cycle.
/// </remarks>
internal sealed class DimensionValidator : IDimensionValidator
{
    private readonly ISession _session;
    private readonly IDimensionTypeLookup _typeLookup;
    private readonly IStructureLookup _structureLookup;
    private readonly IDimensionRecordLookup _recordLookup;
    private readonly IEnumerable<IDimensionDeletionBlockerProvider> _blockerProviders;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IStringLocalizer S;

    public DimensionValidator(
        ISession session,
        IDimensionTypeLookup typeLookup,
        IStructureLookup structureLookup,
        IDimensionRecordLookup recordLookup,
        IEnumerable<IDimensionDeletionBlockerProvider> blockerProviders,
        IDimensionAuthorisation authorisation,
        IStringLocalizer<DimensionValidator> stringLocalizer)
    {
        _session = session;
        _typeLookup = typeLookup;
        _structureLookup = structureLookup;
        _recordLookup = recordLookup;
        _blockerProviders = blockerProviders;
        _authorisation = authorisation;
        S = stringLocalizer;
    }

    public DimensionValidationBatch BeginBatch() => new();

    // ---- dimension types --------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidateDimensionTypeAsync(
        string? dimensionTypeId,
        string code,
        BilingualText name,
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributeSchema);

        var errors = new List<DimensionError>();

        errors.AddRange(ValidateName(name, code));
        errors.AddRange(ValidateAttributeSchema(attributeSchema));

        // The code is immutable once a type exists, so it is only validated on creation.
        if (dimensionTypeId is null)
        {
            if (!DimensionCodes.IsValidCode(code))
            {
                errors.Add(new DimensionError(
                    DimensionRule.CodeFormat,
                    code,
                    S["A dimension type code must start with a letter and may contain letters, digits, hyphens and underscores, up to fifty characters."]));
            }
            else
            {
                var taken = batch?.TypeCodeSeen(code) == true ||
                    await _typeLookup.GetByCodeAsync(code, cancellationToken) is not null;

                if (taken)
                {
                    errors.Add(new DimensionError(
                        DimensionRule.CodeUniqueness,
                        code,
                        S["A dimension type with the code '{0}' already exists in this tenant, or did and was retired.", code]));
                }
                else
                {
                    batch?.RememberTypeCode(code);
                }
            }
        }

        return errors;
    }

    // ---- structures -------------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidateStructureAsync(
        string? structureId,
        string code,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        bool isPrimaryOrganisation,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(levelDimensionTypeIds);

        var errors = new List<DimensionError>();

        errors.AddRange(ValidateName(name, code));

        if (structureId is null)
        {
            if (!DimensionCodes.IsValidCode(code))
            {
                errors.Add(new DimensionError(
                    DimensionRule.CodeFormat,
                    code,
                    S["A structure code must start with a letter and may contain letters, digits, hyphens and underscores, up to fifty characters."]));
            }
            else
            {
                var taken = batch?.StructureCodeSeen(code) == true ||
                    await _structureLookup.GetByCodeAsync(code, cancellationToken) is not null;

                if (taken)
                {
                    errors.Add(new DimensionError(
                        DimensionRule.CodeUniqueness,
                        code,
                        S["A structure with the code '{0}' already exists in this tenant.", code]));
                }
                else
                {
                    batch?.RememberStructureCode(code);
                }
            }
        }

        errors.AddRange(await ValidateLevelsAsync(levelDimensionTypeIds, code, cancellationToken));

        if (isPrimaryOrganisation)
        {
            var existing = await _structureLookup.GetPrimaryOrganisationAsync(cancellationToken);

            if (existing is not null && !string.Equals(existing.StructureId, structureId, StringComparison.Ordinal))
            {
                errors.Add(new DimensionError(
                    DimensionRule.SinglePrimaryOrganisation,
                    code,
                    S["'{0}' is already the primary organisation structure. Clear that first, then set this one.",
                        existing.Code]));
            }
        }

        return errors;
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
            if (await TypeExistsAsync(dimensionTypeId, cancellationToken) is null)
            {
                errors.Add(new DimensionError(
                    DimensionRule.UnknownReference,
                    dimensionTypeId,
                    S["There is no dimension type with the id '{0}' in this tenant.", dimensionTypeId]));
            }
        }

        return errors;
    }

    // ---- records ----------------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidateRecordAsync(
        string? recordId,
        string dimensionTypeId,
        string code,
        BilingualText name,
        EffectiveRange effectiveRange,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        var errors = new List<DimensionError>();

        if (!DimensionCodes.IsValidCode(code))
        {
            errors.Add(new DimensionError(
                DimensionRule.CodeFormat,
                code,
                S["A code must start with a letter and may contain letters, digits, hyphens and underscores, up to fifty characters."]));
        }
        else if (await RecordCodeIsTakenAsync(code, recordId, batch, cancellationToken))
        {
            // Across every dimension type and including retired records, per architecture
            // section 6: reusing the code of a retired unit makes every historical report
            // ambiguous about which one it means.
            errors.Add(new DimensionError(
                DimensionRule.CodeUniqueness,
                code,
                S["The code '{0}' is already used by another dimension record in this tenant.", code]));
        }
        else
        {
            batch?.RememberRecordCode(code);
        }

        errors.AddRange(ValidateName(name, code));

        if (effectiveRange.From == default)
        {
            errors.Add(new DimensionError(
                DimensionRule.EffectiveDateRequired,
                code,
                S["An effective from date is required."]));
        }
        else if (effectiveRange.IsEmpty)
        {
            errors.Add(new DimensionError(
                DimensionRule.EffectiveRangeInvalid,
                code,
                S["The effective to date cannot be before the effective from date."]));
        }

        if (await TypeExistsAsync(dimensionTypeId, cancellationToken) is null)
        {
            errors.Add(new DimensionError(
                DimensionRule.UnknownReference,
                dimensionTypeId,
                S["There is no dimension type with the id '{0}' in this tenant.", dimensionTypeId]));
        }

        return errors;
    }

    /// <summary>
    /// Whether another record already holds this code, counting retired records and earlier
    /// rows of the same batch.
    /// </summary>
    /// <remarks>
    /// The batch is what makes this work for an import. Two rows of one file sharing a code are
    /// each individually fine and together are not, and nothing in the database can see the
    /// clash because neither row is committed yet.
    ///
    /// The index query excludes the record's own rows. A record has one row per content item
    /// version, so a record being edited matches itself and would otherwise be reported as a
    /// duplicate of itself.
    /// </remarks>
    private async Task<bool> RecordCodeIsTakenAsync(
        string code,
        string? recordId,
        DimensionValidationBatch? batch,
        CancellationToken cancellationToken)
    {
        if (batch?.RecordCodeSeen(code) == true)
        {
            return true;
        }

        return await _recordLookup.CodeExistsAsync(code, recordId, cancellationToken);
    }

    // ---- placement --------------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidatePlacementAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DimensionError>();

        if (string.Equals(recordId, newParentId, StringComparison.Ordinal))
        {
            errors.Add(new DimensionError(
                DimensionRule.Cycle,
                recordId,
                S["A record cannot be its own parent."]));

            return errors;
        }

        var structure = await _structureLookup.GetAsync(structureId, cancellationToken);

        if (structure is null)
        {
            errors.Add(new DimensionError(
                DimensionRule.UnknownReference,
                structureId,
                S["There is no structure with the id '{0}' in this tenant.", structureId]));

            return errors;
        }

        var child = await RecordAsync(recordId, cancellationToken);

        if (child is null)
        {
            errors.Add(new DimensionError(
                DimensionRule.UnknownReference,
                recordId,
                S["There is no dimension record with the id '{0}' in this tenant.", recordId]));

            return errors;
        }

        if (newParentId is null)
        {
            // Becoming a root breaks no placement rule: a root is what every axis starts with.
            return errors;
        }

        var parent = await RecordAsync(newParentId, cancellationToken);

        if (parent is null)
        {
            errors.Add(new DimensionError(
                DimensionRule.UnknownReference,
                newParentId,
                S["There is no dimension record with the id '{0}' in this tenant.", newParentId]));

            return errors;
        }

        // Across every date, not only today. A cycle anywhere in the history makes ancestor
        // resolution on that date non-terminating, and the date it breaks on is one nobody
        // will be looking at when they make the change.
        var wouldCycle = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId == recordId &&
                index.DescendantId == newParentId)
            .CountAsync(cancellationToken) > 0;

        if (wouldCycle)
        {
            errors.Add(new DimensionError(
                DimensionRule.Cycle,
                newParentId,
                S["'{0}' is at or below '{1}' at some point in time, so moving '{1}' under it would create a cycle.",
                    parent.Code,
                    child.Code]));

            return errors;
        }

        errors.AddRange(await ValidateLevelRulesAsync(structure, child, parent, cancellationToken));

        // Advisory, never blocking: customers legitimately pre-build next year's structure, and
        // refusing that would make the engine unusable for the one case it was designed for.
        if (!parent.EffectiveRange.Contains(effectiveFrom))
        {
            errors.Add(new DimensionError(
                DimensionRule.ParentNotEffectiveWhenChildIs,
                parent.Code,
                S["'{0}' is not effective on {1}. The placement is allowed, but check the dates.",
                    parent.Code,
                    effectiveFrom]));
        }

        return errors;
    }

    /// <summary>
    /// The three level rules from architecture section 6: permitted level, level skipping, and
    /// self-nesting.
    /// </summary>
    private async Task<IReadOnlyList<DimensionError>> ValidateLevelRulesAsync(
        StructureDocument structure,
        DimensionNodeRef child,
        DimensionNodeRef parent,
        CancellationToken cancellationToken)
    {
        var errors = new List<DimensionError>();

        var childLevel = structure.OrdinalOf(child.DimensionTypeId);
        var parentLevel = structure.OrdinalOf(parent.DimensionTypeId);

        if (childLevel is null || parentLevel is null)
        {
            // A non-strict structure tolerates a type that is not one of its levels, which is
            // what an axis still being shaped needs. A strict one does not.
            if (structure.IsStrict)
            {
                var stranger = childLevel is null ? child : parent;

                errors.Add(new DimensionError(
                    DimensionRule.ParentTypeNotPermitted,
                    stranger.Code,
                    S["'{0}' is not one of the levels of the structure '{1}', which is strict.",
                        stranger.Code,
                        structure.Code]));
            }

            return errors;
        }

        if (string.Equals(child.DimensionTypeId, parent.DimensionTypeId, StringComparison.Ordinal))
        {
            // Undated: the lookup never filters by retirement, so there is no "as of which
            // date" workaround needed here the way there was through IDimensionTypeService.
            var type = await _typeLookup.GetAsync(child.DimensionTypeId, cancellationToken);

            if (type?.AllowsSelfNesting != true)
            {
                errors.Add(new DimensionError(
                    DimensionRule.SelfNesting,
                    child.Code,
                    S["'{0}' and '{1}' are the same kind of unit, and that kind does not allow nesting inside itself.",
                        child.Code,
                        parent.Code]));
            }

            return errors;
        }

        if (parentLevel >= childLevel)
        {
            errors.Add(new DimensionError(
                DimensionRule.ParentTypeNotPermitted,
                parent.Code,
                S["'{0}' sits at or below '{1}' in the structure '{2}', so it cannot be its parent.",
                    parent.Code,
                    child.Code,
                    structure.Code]));

            return errors;
        }

        if (childLevel - parentLevel > 1 && !structure.AllowSkipLevel)
        {
            errors.Add(new DimensionError(
                DimensionRule.LevelSkipping,
                child.Code,
                S["Placing '{0}' under '{1}' skips a level, and the structure '{2}' does not allow that.",
                    child.Code,
                    parent.Code,
                    structure.Code]));
        }

        return errors;
    }

    // ---- assignments ------------------------------------------------------------------

    public Task<IReadOnlyList<DimensionError>> ValidateAssignmentAsync(
        string employeeId,
        string structureId,
        IReadOnlyList<AssignmentSplitEntry> split,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(split);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<DimensionError>();

        if (split.Count == 0)
        {
            errors.Add(new DimensionError(
                DimensionRule.AllocationTotal,
                employeeId,
                S["A placement needs at least one node. Use the end operation to remove an employee from an axis."]));

            return Task.FromResult<IReadOnlyList<DimensionError>>(errors);
        }

        var total = split.Sum(entry => entry.AllocationPercent);

        if (total != 100m)
        {
            errors.Add(new DimensionError(
                DimensionRule.AllocationTotal,
                employeeId,
                S["Allocations must total 100 percent on any one date. These total {0}.", total]));
        }

        if (split.Any(entry => entry.AllocationPercent <= 0m))
        {
            errors.Add(new DimensionError(
                DimensionRule.AllocationTotal,
                employeeId,
                S["An allocation must be greater than zero. Remove the placement instead."]));
        }

        var primaries = split.Count(entry => entry.IsPrimary);

        if (primaries != 1)
        {
            errors.Add(new DimensionError(
                DimensionRule.SinglePrimaryAssignment,
                employeeId,
                S["Exactly one placement must be primary, so that matrix cases stay unambiguous. {0} are marked primary.",
                    primaries]));
        }

        foreach (var recordId in split
            .GroupBy(entry => entry.RecordId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            errors.Add(new DimensionError(
                DimensionRule.OverlappingAssignment,
                recordId,
                S["An employee cannot be placed at the same node twice on the same date."]));
        }

        return Task.FromResult<IReadOnlyList<DimensionError>>(errors);
    }

    // ---- merge ------------------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidateMergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DimensionError>();

        if (string.Equals(sourceRecordId, targetRecordId, StringComparison.Ordinal))
        {
            errors.Add(new DimensionError(
                DimensionRule.MergeTarget,
                sourceRecordId,
                S["A record cannot be merged into itself."]));

            return errors;
        }

        var date = EffectiveDates.ToColumn(effectiveFrom);

        var targetIsInsideSource = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId == sourceRecordId &&
                index.DescendantId == targetRecordId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .CountAsync(cancellationToken) > 0;

        if (targetIsInsideSource)
        {
            // Folding a unit into something inside it would leave the target with no parent
            // chain the moment the source retires.
            errors.Add(new DimensionError(
                DimensionRule.MergeTarget,
                targetRecordId,
                S["'{0}' is inside '{1}', so it cannot be the target of merging '{1}' into it.",
                    targetRecordId,
                    sourceRecordId]));
        }

        return errors;
    }

    // ---- deletion ---------------------------------------------------------------------

    public async Task<DeletionAssessment> AssessDeletionAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        var blockers = new List<DeletionBlocker>();

        var today = EffectiveDates.ToColumn(await _authorisation.TodayAsync());

        var liveAssignments = await _session
            .QueryIndex<EmployeeAssignmentIndex>(index =>
                index.NodeId == recordId &&
                index.EffectiveFrom <= today &&
                today <= index.EffectiveToInclusive)
            .CountAsync(cancellationToken);

        if (liveAssignments > 0)
        {
            blockers.Add(new DeletionBlocker(
                new DimensionError(
                    DimensionRule.DeletionBlocked,
                    recordId,
                    S["{0} employee placement(s) are live at this record today.", liveAssignments]),
                IsHistoricalOnly: false));
        }

        var anyAssignments = await _session
            .QueryIndex<EmployeeAssignmentIndex>(index => index.NodeId == recordId)
            .CountAsync(cancellationToken);

        if (anyAssignments > liveAssignments)
        {
            blockers.Add(new DeletionBlocker(
                new DimensionError(
                    DimensionRule.DeletionBlocked,
                    recordId,
                    S["{0} past employee placement(s) refer to this record.", anyAssignments - liveAssignments]),
                IsHistoricalOnly: true));
        }

        // Children are a live reference: deleting a node that still has something under it
        // would orphan the subtree.
        var children = await _session
            .QueryIndex<DimensionLinkIndex>(index => index.ParentId == recordId)
            .CountAsync(cancellationToken);

        if (children > 0)
        {
            blockers.Add(new DeletionBlocker(
                new DimensionError(
                    DimensionRule.DeletionBlocked,
                    recordId,
                    S["{0} record(s) sit under this one. Move or merge them first.", children]),
                IsHistoricalOnly: false));
        }

        // Payroll, workflows and approval scopes are modules that do not exist yet. Each will
        // register a provider rather than this list quietly pretending to be complete.
        foreach (var provider in _blockerProviders)
        {
            blockers.AddRange(await provider.GetBlockersAsync(recordId, cancellationToken));
        }

        if (blockers.Count == 0)
        {
            return new DeletionAssessment(DeletionOutcome.Clean, []);
        }

        var outcome = blockers.All(blocker => blocker.IsHistoricalOnly)
            ? DeletionOutcome.RetireOnly
            : DeletionOutcome.Blocked;

        return new DeletionAssessment(outcome, [.. blockers.Select(blocker => blocker.Error)]);
    }

    // ---- shared -----------------------------------------------------------------------

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

    private IEnumerable<DimensionError> ValidateAttributeSchema(
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in attributeSchema)
        {
            if (!DimensionCodes.IsValidAttributeName(attribute.Name))
            {
                yield return new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["An attribute name must start with a letter and may contain letters and digits only, up to fifty characters."]);

                continue;
            }

            if (!seen.Add(attribute.Name))
            {
                yield return new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["The attribute '{0}' is declared more than once.", attribute.Name]);
            }

            if (string.IsNullOrWhiteSpace(attribute.Label.En) || string.IsNullOrWhiteSpace(attribute.Label.Ar))
            {
                yield return new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["The attribute '{0}' needs a label in both English and Arabic.", attribute.Name]);
            }
        }

        // The standard fields are on DimensionRecordPart. An attribute with the same name would
        // put two fields called Code on one record, and the editor would show both.
        foreach (var attribute in attributeSchema.Where(attribute =>
            DimensionTypeService.StandardFieldNames.Contains(attribute.Name)))
        {
            yield return new DimensionError(
                DimensionRule.AttributeSchema,
                attribute.Name,
                S["'{0}' is a standard field that every dimension record already carries. Choose another attribute name.",
                    attribute.Name]);
        }
    }

    private async Task<DimensionTypeDocument?> TypeExistsAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken) =>
        // Undated on purpose: a retired type still exists, and a rule about whether something
        // was ever defined must not depend on today's date. The lookup is undated by design, so
        // there is no "as of which date" workaround to write here.
        await _typeLookup.GetAsync(dimensionTypeId, cancellationToken);

    private async Task<DimensionNodeRef?> RecordAsync(string recordId, CancellationToken cancellationToken) =>
        await _recordLookup.GetAsync(recordId, cancellationToken);
}
