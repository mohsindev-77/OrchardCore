using Microsoft.Extensions.Localization;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
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

    /// <summary>
    /// How this module asks whether a head is a real employee who has not left.
    /// </summary>
    /// <remarks>
    /// A collection rather than a single service so that the dimension engine still starts when
    /// <c>WorkMate.Records</c> is disabled, and so that the absence is reportable instead of
    /// invisible. See <see cref="IEmployeeLookup"/>.
    /// </remarks>
    private readonly IEnumerable<IEmployeeLookup> _employeeLookups;

    private readonly IDimensionAuthorisation _authorisation;

    /// <summary>
    /// Whether this tenant insists on the Arabic half of a name. ADR-0003's addendum: English is
    /// required, Arabic is optional unless the tenant says otherwise.
    /// </summary>
    private readonly IBilingualNamePolicy _namePolicy;

    private readonly IStringLocalizer S;

    public DimensionValidator(
        ISession session,
        IDimensionTypeLookup typeLookup,
        IStructureLookup structureLookup,
        IDimensionRecordLookup recordLookup,
        IEnumerable<IDimensionDeletionBlockerProvider> blockerProviders,
        IEnumerable<IEmployeeLookup> employeeLookups,
        IDimensionAuthorisation authorisation,
        IBilingualNamePolicy namePolicy,
        IStringLocalizer<DimensionValidator> stringLocalizer)
    {
        _session = session;
        _typeLookup = typeLookup;
        _structureLookup = structureLookup;
        _recordLookup = recordLookup;
        _blockerProviders = blockerProviders;
        _employeeLookups = employeeLookups;
        _authorisation = authorisation;
        _namePolicy = namePolicy;
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

        errors.AddRange(await ValidateNameAsync(name, code, cancellationToken));
        errors.AddRange(await ValidateAttributeSchemaAsync(attributeSchema, cancellationToken));

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
        StructureShape shape,
        bool isPrimaryOrganisation,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(levelDimensionTypeIds);
        ArgumentNullException.ThrowIfNull(shape);

        var errors = new List<DimensionError>();

        errors.AddRange(await ValidateNameAsync(name, code, cancellationToken));

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
        errors.AddRange(ValidateShape(levelDimensionTypeIds, shape, code));

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

        // An axis with no types yet is a legitimate state, not an error. base.recipe.json gives
        // every new tenant an empty "Organisation" structure precisely so the customer names their
        // own levels rather than deleting somebody's guess at them, and the designer draws it
        // correctly: the structure card, with nothing under it and an Add unit menu that offers
        // nothing until the Structures editor has been used. The same argument as an unplaced
        // record — "not configured yet" and "wrong" are different facts.
        if (levelDimensionTypeIds.Count == 0)
        {
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

    /// <summary>
    /// The containment map has to be about this structure's own types, and it has to have a way
    /// in.
    /// </summary>
    /// <remarks>
    /// Both failures are silent rather than loud if they are not caught here. A pairing naming a
    /// type the axis does not use is simply never consulted — the picker iterates the axis's
    /// vocabulary — so the customer ticks something and nothing happens. No root types at all is
    /// worse: every record is unplaced, the chart is empty, and nothing on screen says why. ADR-0010.
    /// </remarks>
    private List<DimensionError> ValidateShape(
        IReadOnlyList<string> levelDimensionTypeIds,
        StructureShape shape,
        string subject)
    {
        if (levelDimensionTypeIds.Count == 0)
        {
            return [];
        }

        var vocabulary = levelDimensionTypeIds.ToHashSet(StringComparer.Ordinal);
        var errors = new List<DimensionError>();

        var strangers = shape.RootDimensionTypeIds
            .Concat(shape.Containment.Select(pair => pair.ParentDimensionTypeId))
            .Concat(shape.Containment.Select(pair => pair.ChildDimensionTypeId))
            .Where(id => !vocabulary.Contains(id))
            .Distinct(StringComparer.Ordinal);

        foreach (var stranger in strangers)
        {
            errors.Add(new DimensionError(
                DimensionRule.StructureLevels,
                stranger,
                S["'{0}' is named in this structure's rules but is not one of its types.", stranger]));
        }

        if (shape.RootDimensionTypeIds.Count == 0)
        {
            errors.Add(new DimensionError(
                DimensionRule.StructureLevels,
                subject,
                S["A structure needs at least one kind of unit that may sit at its top."]));
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

        errors.AddRange(await ValidateNameAsync(name, code, cancellationToken));

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

        errors.AddRange(ValidateContainment(structure, child, parent));

        // Not effective splits into two cases that look alike and are not. A parent that has not
        // started yet is usually a customer pre-building next year's structure, which is legitimate
        // and only worth a warning. A parent that has already been retired is different in kind:
        // placing a child under it from here on would leave a live unit under a closed one with no
        // end date, which is blocked rather than merely flagged.
        if (parent.EffectiveRange.To is { } parentRetiredOn && effectiveFrom > parentRetiredOn)
        {
            errors.Add(new DimensionError(
                DimensionRule.ParentRetired,
                parent.Code,
                S["'{0}' was retired on {1} and cannot take on a new placement from {2}.",
                    parent.Code,
                    parentRetiredOn,
                    effectiveFrom]));
        }
        else if (effectiveFrom < parent.EffectiveRange.From)
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
    /// Containment: whether this axis permits a child of this type under a parent of that one.
    /// </summary>
    /// <remarks>
    /// The whole of what used to be three rules of arithmetic on level ordinals — permitted level,
    /// level skipping, self-nesting — now that ADR-0010 has made the permitted pairings explicit.
    /// The decision itself is <see cref="ContainmentRules.Decide"/>, which the pickers call too;
    /// everything here is turning its answer into a sentence.
    ///
    /// The dimension type is no longer consulted. It used to hold a veto over nesting inside
    /// itself that no axis could grant past, which meant the answer to "may this sit here" lived in
    /// two places and the screen that asks it could only offer half of it. ADR-0010's addendum put
    /// the diagonal in the same map as every other pairing.
    /// </remarks>
    private IReadOnlyList<DimensionError> ValidateContainment(
        StructureDocument structure,
        DimensionNodeRef child,
        DimensionNodeRef parent)
    {
        var outcome = ContainmentRules.Decide(structure, parent.DimensionTypeId, child.DimensionTypeId);

        if (outcome == ContainmentRules.Outcome.Permitted)
        {
            return [];
        }

        // Undeclared. On a strict axis that is a refusal; on one still being shaped it is worth
        // saying and not worth refusing, which is the whole job of IsStrict and the one place it
        // is read. ParentTypeNotDeclared is advisory by its rule, so no caller has to know that.
        var rule = structure.IsStrict
            ? DimensionRule.ParentTypeNotPermitted
            : DimensionRule.ParentTypeNotDeclared;

        if (outcome == ContainmentRules.Outcome.SelfNestingNotDeclared)
        {
            return
            [
                new DimensionError(
                    rule,
                    child.Code,
                    S["'{0}' and '{1}' are the same kind of unit, and the structure '{2}' does not allow that kind inside itself.",
                        child.Code,
                        parent.Code,
                        structure.Code]),
            ];
        }

        return
        [
            new DimensionError(
                rule,
                parent.Code,
                S["The structure '{0}' does not allow a unit like '{1}' under one like '{2}'.",
                    structure.Code,
                    child.Code,
                    parent.Code]),
        ];
    }

    // ---- assignments ------------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidateAssignmentAsync(
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
                S["A placement needs at least one unit. Use the end operation to remove an employee from a structure."]));

            return errors;
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

        errors.AddRange(await ValidateAttachmentAsync(structureId, split, effectiveFrom, cancellationToken));

        return errors;
    }

    /// <summary>
    /// Where an employee may be attached on this axis: the declared rule, and the advisory one.
    /// </summary>
    /// <remarks>
    /// Both are asked of every unit in the split, not only the primary one, because a secondary
    /// placement is counted by exactly the same roll-ups the primary is — the Zenith matrix is the
    /// worked example, and its whole point is that cost rolls up by project over the secondary rows.
    ///
    /// An unknown structure is not reported here. Every caller has already resolved it, and a
    /// second "that structure does not exist" from a method about allocations would be noise on top
    /// of the error that already said so.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionError>> ValidateAttachmentAsync(
        string structureId,
        IReadOnlyList<AssignmentSplitEntry> split,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken)
    {
        var structure = await _structureLookup.GetAsync(structureId, cancellationToken);

        if (structure is null)
        {
            return [];
        }

        var errors = new List<DimensionError>();
        var on = EffectiveDates.ToColumn(effectiveFrom);

        foreach (var entry in split.DistinctBy(entry => entry.RecordId, StringComparer.Ordinal))
        {
            var record = await _recordLookup.GetAsync(entry.RecordId, cancellationToken);

            if (record is null)
            {
                errors.Add(new DimensionError(
                    DimensionRule.UnknownReference,
                    entry.RecordId,
                    S["There is no dimension record with the id '{0}' in this tenant.", entry.RecordId]));

                continue;
            }

            if (!structure.PermitsEmployeesAt(record.DimensionTypeId))
            {
                var type = await _typeLookup.GetAsync(record.DimensionTypeId, cancellationToken);

                errors.Add(new DimensionError(
                    DimensionRule.UnitDoesNotHoldEmployees,
                    record.Code,
                    S["'{0}' is a {1}, and '{2}' does not place employees on a {1}. Place them at a unit beneath it.",
                        record.NameEn,
                        type?.Name.En ?? record.DimensionTypeId,
                        structure.Name.En]));

                // No point also warning that it has children: the blocking rule has already
                // answered, and two messages about one unit is one more than anybody can act on.
                continue;
            }

            var children = await _session
                .QueryIndex<DimensionLinkIndex>(index =>
                    index.StructureId == structureId &&
                    index.ParentId == entry.RecordId &&
                    index.EffectiveFrom <= on &&
                    on <= index.EffectiveToInclusive)
                .CountAsync(cancellationToken);

            if (children > 0)
            {
                errors.Add(new DimensionError(
                    DimensionRule.EmployeeAtContainerUnit,
                    record.Code,
                    S["'{0}' has {1} unit(s) under it on {2}. Somebody placed here is counted by this unit and again by every roll-up beneath it.",
                        record.NameEn,
                        children,
                        effectiveFrom]));
            }
        }

        return errors;
    }

    // ---- head appointments ------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionError>> ValidateHeadAppointmentAsync(
        string structureId,
        string recordId,
        string employeeId,
        EffectiveRange range,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DimensionError>();

        var structure = await _structureLookup.GetAsync(structureId, cancellationToken);

        if (structure is null)
        {
            errors.Add(new DimensionError(
                DimensionRule.UnknownReference,
                structureId,
                S["There is no structure with the id '{0}' in this tenant.", structureId]));
        }

        var record = await _recordLookup.GetAsync(recordId, cancellationToken);

        if (record is null)
        {
            errors.Add(new DimensionError(
                DimensionRule.UnknownReference,
                recordId,
                S["There is no dimension record with the id '{0}' in this tenant.", recordId]));
        }

        if (range.To is not null && range.To < range.From)
        {
            errors.Add(new DimensionError(
                DimensionRule.EffectiveRangeInvalid,
                recordId,
                S["A head's term cannot end before it starts."]));
        }

        errors.AddRange(await ValidateHeadIsEligibleAsync(recordId, employeeId, range, cancellationToken));
        errors.AddRange(await ValidateNoCompetingHeadAsync(structureId, recordId, employeeId, range, cancellationToken));

        return errors;
    }

    /// <summary>
    /// That the head is somebody this tenant employs, and had not already left when their term
    /// starts.
    /// </summary>
    /// <remarks>
    /// Deliberately checks the <em>start</em> of the term rather than the whole of it. A head who
    /// leaves mid-term is not an invalid appointment, it is an appointment that ends — and
    /// <c>IEmployeeService.ExitAsync</c> closes it on the exit date rather than refusing the exit.
    /// Refusing here on the strength of a future end date would make it impossible to record a
    /// past leaver's headship at all, which is precisely the history this engine exists to keep.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionError>> ValidateHeadIsEligibleAsync(
        string recordId,
        string employeeId,
        EffectiveRange range,
        CancellationToken cancellationToken)
    {
        var lookup = _employeeLookups.FirstOrDefault();

        if (lookup is null)
        {
            return
            [
                new DimensionError(
                    DimensionRule.HeadNotEligible,
                    recordId,
                    S["A unit head cannot be appointed on this tenant: the employee record (WorkMate.Records) is not enabled."]),
            ];
        }

        var employee = await lookup.GetAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return
            [
                new DimensionError(
                    DimensionRule.HeadNotEligible,
                    employeeId,
                    S["There is no employee with the id '{0}' in this tenant.", employeeId]),
            ];
        }

        if (employee.HasLeftBy(range.From))
        {
            return
            [
                new DimensionError(
                    DimensionRule.HeadNotEligible,
                    employee.Code,
                    S["{0} left on {1} and cannot be appointed to head a unit from {2}.",
                        employee.NameEn,
                        employee.ExitedOn!.Value,
                        range.From]),
            ];
        }

        return [];
    }

    /// <summary>
    /// That nobody else's term stands in the way of the one being claimed.
    /// </summary>
    /// <remarks>
    /// <b>A handover is not a clash, and this is the distinction the rule turns on.</b> Appointing a
    /// successor from 1 April while the sitting head's term runs open-ended from 2024 overlaps on
    /// paper and is the most ordinary thing that happens to a unit: <c>SetHeadAsync</c> closes the
    /// outgoing term on 31 March, the same displacement a new placement performs on the one it
    /// replaces. A naive overlap test refuses every handover there has ever been.
    ///
    /// Two things genuinely do stand in the way, and both are refused:
    /// <list type="bullet">
    /// <item><b>A term already on record that starts on or after the new date.</b> Somebody has
    /// recorded a future appointment; writing over it would delete a decision rather than supersede
    /// one.</item>
    /// <item><b>A closed term by somebody else covering days this one claims.</b> This is backdating
    /// into a period another head is recorded as having led. The engine splits rather than overwrites
    /// when a <em>placement</em> is backdated (ADR-0005's addendum), but a head is not a placement:
    /// truncating a completed term changes who an approval from that period resolves to, which is
    /// the one thing dating the appointment exists to keep stable.</item>
    /// </list>
    ///
    /// A term the same employee already holds is never a clash. Re-appointing somebody already in
    /// post is a no-op a recipe re-run does constantly, and refusing it would make <c>unit-heads</c>
    /// fail its second application for a reason ADR-0008 promises it will not.
    ///
    /// Read from the index rather than from the document: it is the same rows either way, and it is
    /// what the write path will have flushed, so the validator and the writer cannot disagree about
    /// what is already there.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionError>> ValidateNoCompetingHeadAsync(
        string structureId,
        string recordId,
        string employeeId,
        EffectiveRange range,
        CancellationToken cancellationToken)
    {
        var from = EffectiveDates.ToColumn(range.From);
        var to = EffectiveDates.ToInclusiveEndColumn(range.To);
        var openEnded = EffectiveDates.OpenEnded;

        var overlapping = await _session
            .QueryIndex<UnitHeadIndex>(index =>
                index.StructureId == structureId &&
                index.NodeId == recordId &&
                index.EmployeeId != employeeId &&
                index.EffectiveFrom <= to &&
                from <= index.EffectiveToInclusive &&
                // Either it begins inside the claimed period — a future appointment this one would
                // delete — or it is a closed term reaching into it, which is backdating over
                // somebody else's recorded tenure. An open-ended term that began earlier is the
                // handover case and is displaced, not refused.
                (from <= index.EffectiveFrom || index.EffectiveToInclusive < openEnded))
            .ListAsync(cancellationToken);

        if (!overlapping.Any())
        {
            return [];
        }

        var record = await _recordLookup.GetAsync(recordId, cancellationToken);
        var clash = overlapping.First();
        var incumbent = await (_employeeLookups.FirstOrDefault()?.GetAsync(clash.EmployeeId, cancellationToken)
            ?? Task.FromResult<EmployeeRef?>(null));

        return
        [
            new DimensionError(
                DimensionRule.SingleHeadPerUnit,
                record?.Code ?? recordId,
                S["{0} already heads '{1}' from {2}. A unit has one head at a time; end that term first.",
                    incumbent?.NameEn ?? clash.EmployeeId,
                    record?.NameEn ?? recordId,
                    EffectiveDates.FromColumn(clash.EffectiveFrom)]),
        ];
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

    /// <summary>
    /// English is required. Arabic is required only where the tenant has said so.
    /// </summary>
    /// <remarks>
    /// ADR-0003's addendum. The two halves are reported separately because they are different
    /// problems: a missing English name is always a mistake, a missing Arabic one is a mistake
    /// only in a tenant that has chosen to insist on it, and telling a customer who has not that
    /// "a name is required in both" would be telling them something untrue of their tenant.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionError>> ValidateNameAsync(
        BilingualText name, string subject, CancellationToken cancellationToken)
    {
        var errors = new List<DimensionError>();

        if (string.IsNullOrWhiteSpace(name.En))
        {
            errors.Add(new DimensionError(
                DimensionRule.NameRequired,
                subject,
                S["A name is required in English."],
                Field: "NameEn"));
        }

        if (string.IsNullOrWhiteSpace(name.Ar) &&
            await _namePolicy.RequiresArabicAsync(cancellationToken))
        {
            errors.Add(new DimensionError(
                DimensionRule.NameRequired,
                subject,
                S["A name is required in Arabic. This tenant requires Arabic names; that can be changed in WorkMate settings."],
                Field: "NameAr"));
        }

        return errors;
    }

    private async Task<IReadOnlyList<DimensionError>> ValidateAttributeSchemaAsync(
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        CancellationToken cancellationToken)
    {
        var errors = new List<DimensionError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Asked once for the whole schema rather than per attribute: it is one tenant setting, and
        // a twelve-attribute type should not read it twelve times.
        var requiresArabic = await _namePolicy.RequiresArabicAsync(cancellationToken);

        foreach (var attribute in attributeSchema)
        {
            if (!DimensionCodes.IsValidAttributeName(attribute.Name))
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["An attribute name must start with a letter and may contain letters and digits only, up to fifty characters."]));

                continue;
            }

            if (!seen.Add(attribute.Name))
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["The attribute '{0}' is declared more than once.", attribute.Name]));
            }

            if (string.IsNullOrWhiteSpace(attribute.Label.En))
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["The attribute '{0}' needs a label in English.", attribute.Name]));
            }

            // A label is a name like any other, so it follows the same rule: optional in Arabic
            // unless the tenant has said otherwise. ADR-0003's addendum.
            if (requiresArabic && string.IsNullOrWhiteSpace(attribute.Label.Ar))
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    attribute.Name,
                    S["The attribute '{0}' needs a label in Arabic. This tenant requires Arabic names.", attribute.Name]));
            }
        }

        // The standard fields are on DimensionRecordPart. An attribute with the same name would
        // put two fields called Code on one record, and the editor would show both.
        foreach (var attribute in attributeSchema.Where(attribute =>
            DimensionTypeService.StandardFieldNames.Contains(attribute.Name)))
        {
            errors.Add(new DimensionError(
                DimensionRule.AttributeSchema,
                attribute.Name,
                S["'{0}' is a standard field that every dimension record already carries. Choose another attribute name.",
                    attribute.Name]));
        }

        return errors;
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
