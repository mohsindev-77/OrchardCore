using Microsoft.Extensions.Localization;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
using YesSql;
using YesSql.Services;

namespace WorkMate.Dimensions.Internal.Graph;

/// <inheritdoc />
/// <remarks>
/// Internal, like the tables it owns. Only the interface is public.
/// </remarks>
internal sealed class DimensionGraphService : IDimensionGraphService
{
    private readonly ISession _session;

    /// <summary>
    /// The read side of the structure aggregate. This service only ever reads a structure's
    /// levels; it never writes one, so it depends on the lookup rather than
    /// <c>IStructureService</c>. That is also what keeps it from cycling back through
    /// <c>StructureService</c>, which depends on this service to announce a level change.
    /// </summary>
    private readonly IStructureLookup _structureLookup;

    /// <summary>
    /// Read only, and only to ask whether a type nests inside itself when working out what may be
    /// added under a parent. Same reasoning as <see cref="_structureLookup"/>.
    /// </summary>
    private readonly IDimensionTypeLookup _dimensionTypeLookup;

    private readonly IDimensionAuthorisation _authorisation;
    private readonly IDimensionValidator _validator;
    private readonly IStringLocalizer S;

    public DimensionGraphService(
        ISession session,
        IStructureLookup structureLookup,
        IDimensionTypeLookup dimensionTypeLookup,
        IDimensionAuthorisation authorisation,
        IDimensionValidator validator,
        IStringLocalizer<DimensionGraphService> stringLocalizer)
    {
        _session = session;
        _structureLookup = structureLookup;
        _dimensionTypeLookup = dimensionTypeLookup;
        _authorisation = authorisation;
        _validator = validator;
        S = stringLocalizer;
    }

    // ---- resolve ----------------------------------------------------------------------

    public async Task<IReadOnlyList<DimensionNodeRef>> GetAncestorsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));

        var rows = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.DescendantId == recordId &&
                index.Depth > 0 &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .OrderBy(index => index.Depth)
            .ListAsync(cancellationToken);

        return await HydrateAsync(rows.Select(row => (row.AncestorId, row.Depth)), cancellationToken);
    }

    public async Task<Page<DimensionNodeRef>> GetDescendantsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));

        // Depth > 0 excludes the self pair: "descendants" never includes the node itself, and a
        // caller that wants it says so by adding it.
        var total = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId == recordId &&
                index.Depth > 0 &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .CountAsync(cancellationToken);

        var rows = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId == recordId &&
                index.Depth > 0 &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .OrderBy(index => index.Depth)
            .ThenBy(index => index.DescendantId)
            .Skip(skip)
            .Take(take)
            .ListAsync(cancellationToken);

        var items = await HydrateAsync(rows.Select(row => (row.DescendantId, row.Depth)), cancellationToken);

        return new Page<DimensionNodeRef>(items, total, skip, take);
    }

    public async Task<IReadOnlyList<DimensionNodeRef>> GetChildrenAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));

        var rows = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId == recordId &&
                index.Depth == 1 &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        var children = await HydrateAsync(rows.Select(row => (row.DescendantId, row.Depth)), cancellationToken);

        return [.. children.OrderBy(child => child.SortOrder).ThenBy(child => child.NameEn, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyDictionary<string, int>> CountChildrenAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordIds);

        if (recordIds.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));
        var ids = recordIds.Distinct(StringComparer.Ordinal).ToArray();

        var rows = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId.IsIn(ids) &&
                index.Depth == 1 &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.AncestorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.DescendantId).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<DimensionNodeRef>> GetRootsAsync(
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var structure = await _structureLookup.GetAsync(structureId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no structure '{structureId}' in this tenant.");

        var rootTypeId = structure.DimensionTypeIdAt(0);

        if (rootTypeId is null)
        {
            return [];
        }

        var date = await ResolveDateAsync(asAt);

        var roots = (await RecordsOfTypeAsync(rootTypeId, cancellationToken))
            .Where(record => record.EffectiveRange.Contains(date));

        return [.. roots.OrderBy(root => root.SortOrder).ThenBy(root => root.NameEn, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<DimensionNodeRef>> GetUnplacedAsync(
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var structure = await _structureLookup.GetAsync(structureId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no structure '{structureId}' in this tenant.");

        var date = await ResolveDateAsync(asAt);
        var column = EffectiveDates.ToColumn(date);

        var candidates = new List<DimensionNodeRef>();

        foreach (var level in structure.Levels.Where(level => level.Ordinal > 0))
        {
            candidates.AddRange((await RecordsOfTypeAsync(level.DimensionTypeId, cancellationToken))
                .Where(record => record.EffectiveRange.Contains(date)));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var candidateIds = candidates.Select(candidate => candidate.RecordId).ToArray();

        // A record with a parent as of this date is excluded; everything left has no row here at
        // all, which is exactly the designer's definition of unplaced.
        var parented = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.DescendantId.IsIn(candidateIds) &&
                index.Depth > 0 &&
                index.EffectiveFrom <= column &&
                column <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        var parentedIds = new HashSet<string>(parented.Select(row => row.DescendantId), StringComparer.Ordinal);

        return
        [
            .. candidates
                .Where(record => !parentedIds.Contains(record.RecordId))
                .OrderBy(record => record.NameEn, StringComparer.Ordinal),
        ];
    }

    public async Task<IReadOnlyList<DimensionNodeRef>> SearchAsync(
        string structureId,
        string searchText,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchText);

        var trimmed = searchText.Trim();

        if (trimmed.Length == 0)
        {
            return [];
        }

        var structure = await _structureLookup.GetAsync(structureId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no structure '{structureId}' in this tenant.");

        var date = await ResolveDateAsync(asAt);
        var matches = new List<DimensionNodeRef>();

        foreach (var level in structure.Levels)
        {
            foreach (var record in await RecordsOfTypeAsync(level.DimensionTypeId, cancellationToken))
            {
                if (!record.EffectiveRange.Contains(date))
                {
                    continue;
                }

                if (record.Code.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    record.NameEn.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    record.NameAr.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(record);
                }
            }
        }

        return [.. matches.OrderBy(record => record.NameEn, StringComparer.Ordinal).Take(50)];
    }

    public async Task<bool> IsUnderAsync(
        string structureId,
        string recordId,
        string ancestorId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));

        // One row lookup, as architecture section 4 promises.
        return await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.AncestorId == ancestorId &&
                index.DescendantId == recordId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .CountAsync(cancellationToken) > 0;
    }

    public async Task<int?> GetDepthAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));

        var deepest = await _session
            .QueryIndex<DimensionClosureIndex>(index =>
                index.StructureId == structureId &&
                index.DescendantId == recordId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .OrderByDescending(index => index.Depth)
            .FirstOrDefaultAsync(cancellationToken);

        return deepest?.Depth;
    }

    // ---- maintenance ------------------------------------------------------------------

    public async Task EnsureSelfPairsAsync(
        string recordId,
        string dimensionTypeId,
        EffectiveRange effectiveRange,
        CancellationToken cancellationToken = default)
    {
        foreach (var structure in await StructuresWithLevelAsync(dimensionTypeId, cancellationToken))
        {
            await RecomputeSubtreeAsync(structure.StructureId, recordId, effectiveRange, cancellationToken);
        }
    }

    public async Task OnStructureLevelsChangedAsync(
        string structureId,
        IReadOnlyList<string> addedDimensionTypeIds,
        IReadOnlyList<string> removedDimensionTypeIds,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addedDimensionTypeIds);
        ArgumentNullException.ThrowIfNull(removedDimensionTypeIds);

        // A level gained is retrospective: the records of that type already exist and become
        // part of this axis the moment the level is declared. Without self pairs they would be
        // invisible on an axis they belong to, with nothing to say why.
        foreach (var dimensionTypeId in addedDimensionTypeIds)
        {
            foreach (var record in await RecordsOfTypeAsync(dimensionTypeId, cancellationToken))
            {
                await RecomputeSubtreeAsync(structureId, record.RecordId, record.EffectiveRange, cancellationToken);
            }
        }

        // A level lost is not a deletion. The records stop being on this axis from the effective
        // date, and everything before it keeps resolving.
        var lastDayOnAxis = effectiveDate.AddDays(-1);

        foreach (var dimensionTypeId in removedDimensionTypeIds)
        {
            foreach (var record in await RecordsOfTypeAsync(dimensionTypeId, cancellationToken))
            {
                // A link document is created when there is none, rather than skipped. A root of
                // the departing type has never been moved and so has no links at all, but it is
                // still on the axis and its self pair still has to be capped — otherwise the
                // one kind of record that leaves without trace is the one at the top.
                var link = await LoadLinkAsync(structureId, record.RecordId, cancellationToken)
                    ?? new DimensionLinkDocument { StructureId = structureId, RecordId = record.RecordId };

                link.OnAxisUntil = lastDayOnAxis;
                link.Parents = [.. CloseAt(link.Parents, lastDayOnAxis)];

                await _session.SaveCheckedAsync(link, cancellationToken);

                // The node's own rows and everything hanging off it: a child of a departing node
                // loses an ancestor on that date too.
                await RecomputeSubtreeAsync(structureId, record.RecordId, record.EffectiveRange, cancellationToken);
            }
        }
    }

    public async Task<StructureLevelChangePlan> PlanLevelChangeAsync(
        string structureId,
        IReadOnlyList<string> newLevelDimensionTypeIds,
        bool newAllowSkipLevel,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newLevelDimensionTypeIds);

        var structure = await _structureLookup.GetAsync(structureId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no structure '{structureId}' in this tenant.");

        var currentLevelIds = structure.Levels
            .OrderBy(level => level.Ordinal)
            .Select(level => level.DimensionTypeId)
            .ToList();

        var added = newLevelDimensionTypeIds.Except(currentLevelIds, StringComparer.Ordinal).ToList();
        var removed = currentLevelIds.Except(newLevelDimensionTypeIds, StringComparer.Ordinal).ToList();

        var removalImpacts = new List<LevelRemovalImpact>();

        foreach (var dimensionTypeId in removed)
        {
            removalImpacts.Add(await RemovalImpactAsync(structureId, dimensionTypeId, asAt, cancellationToken));
        }

        var violations = await ViolationsFromReorderingAsync(
            structure, newLevelDimensionTypeIds, newAllowSkipLevel, asAt, cancellationToken);

        return new StructureLevelChangePlan(structureId, added, removed, removalImpacts, violations);
    }

    private async Task<LevelRemovalImpact> RemovalImpactAsync(
        string structureId,
        string dimensionTypeId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var onAxisRecordIds = new List<string>();

        foreach (var record in await RecordsOfTypeAsync(dimensionTypeId, cancellationToken))
        {
            if (!record.EffectiveRange.Contains(asAt))
            {
                continue;
            }

            var link = await LoadLinkAsync(structureId, record.RecordId, cancellationToken);

            if (link?.OnAxisUntil is { } until && asAt > until)
            {
                continue;
            }

            onAxisRecordIds.Add(record.RecordId);
        }

        var employeesAffected = 0;

        if (onAxisRecordIds.Count > 0)
        {
            var date = EffectiveDates.ToColumn(asAt);

            var assignmentRows = await _session
                .QueryIndex<EmployeeAssignmentIndex>(index =>
                    index.StructureId == structureId &&
                    index.NodeId.IsIn(onAxisRecordIds) &&
                    index.EffectiveFrom <= date &&
                    date <= index.EffectiveToInclusive)
                .ListAsync(cancellationToken);

            employeesAffected = assignmentRows.Select(row => row.EmployeeId).Distinct(StringComparer.Ordinal).Count();
        }

        return new LevelRemovalImpact(dimensionTypeId, onAxisRecordIds.Count, employeesAffected);
    }

    /// <summary>
    /// Every existing, today-effective placement that the proposed level order would make
    /// invalid. Adding or removing a level never lands here — only reordering can, since a record
    /// whose own type or whose parent's type is leaving the axis is already covered, safely, by
    /// the self-pair capping <see cref="OnStructureLevelsChangedAsync"/> does.
    /// </summary>
    private async Task<IReadOnlyList<DimensionError>> ViolationsFromReorderingAsync(
        Models.StructureDocument structure,
        IReadOnlyList<string> newLevelDimensionTypeIds,
        bool newAllowSkipLevel,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var links = await AllLinksAsync(structure.StructureId, cancellationToken);
        var recordIds = links
            .SelectMany(link => new[] { link.RecordId }.Concat(link.Parents.Select(parent => parent.ParentRecordId)))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (recordIds.Length == 0)
        {
            return [];
        }

        var recordsById = (await _session
                .QueryIndex<DimensionRecordPartIndex>(index => index.ContentItemId.IsIn(recordIds) && index.Latest)
                .ListAsync(cancellationToken))
            .ToDictionary(row => row.ContentItemId, row => (row.Code, row.DimensionTypeId), StringComparer.Ordinal);

        var newOrdinalByType = newLevelDimensionTypeIds
            .Select((dimensionTypeId, ordinal) => (dimensionTypeId, ordinal))
            .ToDictionary(x => x.dimensionTypeId, x => x.ordinal, StringComparer.Ordinal);

        var violations = new List<DimensionError>();

        foreach (var link in links)
        {
            var parentId = link.ParentOn(asAt);

            if (parentId is null ||
                !recordsById.TryGetValue(link.RecordId, out var child) ||
                !recordsById.TryGetValue(parentId, out var parent))
            {
                continue;
            }

            // The child's or the parent's own type is leaving the axis: a removal impact,
            // already reported above, and safe. Reordering is not the question for this pair.
            if (!newOrdinalByType.TryGetValue(child.DimensionTypeId, out var childOrdinal) ||
                !newOrdinalByType.TryGetValue(parent.DimensionTypeId, out var parentOrdinal))
            {
                continue;
            }

            if (childOrdinal <= parentOrdinal)
            {
                violations.Add(new DimensionError(
                    DimensionRule.ParentTypeNotPermitted,
                    child.Code,
                    S["'{0}' is currently placed under '{1}'. The new level order for '{2}' would put '{1}' at or below '{0}', which is not permitted.",
                        child.Code,
                        parent.Code,
                        structure.Code]));

                continue;
            }

            if (childOrdinal - parentOrdinal > 1 && !newAllowSkipLevel)
            {
                violations.Add(new DimensionError(
                    DimensionRule.LevelSkipping,
                    child.Code,
                    S["'{0}' is currently placed under '{1}'. The new level order for '{2}' would skip a level between them, which this structure would not allow.",
                        child.Code,
                        parent.Code,
                        structure.Code]));
            }
        }

        return violations;
    }

    public async Task<IReadOnlyList<string>> GetPermittedChildTypeIdsAsync(
        string structureId,
        string? parentRecordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var structure = await _structureLookup.GetAsync(structureId, cancellationToken);

        if (structure is null || structure.Levels.Count == 0)
        {
            return [];
        }

        var levels = structure.Levels.OrderBy(level => level.Ordinal).ToList();

        if (parentRecordId is null)
        {
            // A root sits at the first level. Skipping does not apply upwards: there is nothing
            // above the first level to skip past.
            return [levels[0].DimensionTypeId];
        }

        var date = EffectiveDates.ToColumn(await ResolveDateAsync(asAt));

        var parent = await _session
            .QueryIndex<DimensionRecordPartIndex>(index =>
                index.ContentItemId == parentRecordId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .FirstOrDefaultAsync(cancellationToken);

        if (parent is null)
        {
            return [];
        }

        var parentOrdinal = levels.FindIndex(level => level.DimensionTypeId == parent.DimensionTypeId);

        if (parentOrdinal < 0)
        {
            // The parent's type is not a level of this structure at all, which the validator
            // would refuse; offering anything here would only produce a form that cannot save.
            return [];
        }

        var permitted = new List<string>();

        var parentType = await _dimensionTypeLookup.GetAsync(parent.DimensionTypeId, cancellationToken);

        if (parentType?.AllowsSelfNesting == true)
        {
            permitted.Add(parent.DimensionTypeId);
        }

        var deepest = structure.AllowSkipLevel ? levels.Count - 1 : Math.Min(parentOrdinal + 1, levels.Count - 1);

        for (var ordinal = parentOrdinal + 1; ordinal <= deepest; ordinal++)
        {
            permitted.Add(levels[ordinal].DimensionTypeId);
        }

        return permitted;
    }

    public async Task MarkOrphanedByParentRetirementAsync(
        string structureId,
        IReadOnlyList<string> childRecordIds,
        string formerParentId,
        DateOnly retiredOn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(childRecordIds);

        foreach (var childRecordId in childRecordIds.Distinct(StringComparer.Ordinal))
        {
            var link = await LoadLinkAsync(structureId, childRecordId, cancellationToken)
                ?? new DimensionLinkDocument { StructureId = structureId, RecordId = childRecordId };

            link.OrphanedByParentRetirementOn = retiredOn;
            link.OrphanedFromParentId = formerParentId;

            await _session.SaveCheckedAsync(link, cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, OrphanedByParentRetirement>> GetOrphanedByParentRetirementAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordIds);

        var result = new Dictionary<string, OrphanedByParentRetirement>(StringComparer.Ordinal);

        if (recordIds.Count == 0)
        {
            return result;
        }

        var ids = recordIds.Distinct(StringComparer.Ordinal).ToArray();

        // Through the index to the documents themselves: the two fields are not indexed, because
        // nothing filters on them — this reads them off the handful of records the unplaced panel
        // is showing.
        var documents = await _session
            .Query<DimensionLinkDocument, DimensionLinkIndex>(index =>
                index.StructureId == structureId && index.ChildId.IsIn(ids))
            .ListAsync(cancellationToken);

        var marked = documents
            .Where(document => document.StructureId == structureId
                && document.OrphanedByParentRetirementOn is not null
                && document.OrphanedFromParentId is { Length: > 0 })
            .DistinctBy(document => document.RecordId, StringComparer.Ordinal)
            .ToList();

        if (marked.Count == 0)
        {
            return result;
        }

        // Undated, because every one of these parents is retired and some of them closed on the
        // day they opened. Asking for them as at any particular date is how the badge ends up
        // naming an id.
        var parentIds = marked
            .Select(document => document.OrphanedFromParentId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var parents = (await _session
                .QueryIndex<DimensionRecordPartIndex>(index =>
                    index.ContentItemId.IsIn(parentIds) && index.Latest)
                .ListAsync(cancellationToken))
            .ToDictionary(row => row.ContentItemId, row => (row.NameEn, row.NameAr), StringComparer.Ordinal);

        foreach (var document in marked)
        {
            var formerParentId = document.OrphanedFromParentId!;

            parents.TryGetValue(formerParentId, out var name);

            result[document.RecordId] = new OrphanedByParentRetirement(
                formerParentId,
                name.NameEn ?? formerParentId,
                name.NameAr ?? string.Empty,
                document.OrphanedByParentRetirementOn!.Value);
        }

        return result;
    }

    public async Task<DimensionResult<int>> PlaceAsync(
        string structureId,
        string recordId,
        string? parentRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<int>();
        }

        var existing = await LoadLinkAsync(structureId, recordId, cancellationToken);

        // The guard that keeps MoveDimensionRecords meaningful. Without it this would be a
        // reparenting under the weaker permission, and the split specification section 4 draws
        // between creating a record and reorganising the company would be decorative.
        if (existing is not null && existing.Parents.Count > 0)
        {
            return DimensionResult.Failed<int>(new DimensionError(
                DimensionRule.ImmutableOnceInUse,
                recordId,
                S["This unit is already placed on this structure. Moving it is a move, not a placement."]));
        }

        var errors = await _validator.ValidatePlacementAsync(
            structureId, recordId, parentRecordId, effectiveFrom, cancellationToken);

        if (errors.Any(error => !error.IsAdvisory))
        {
            return DimensionResult.Failed<int>(errors);
        }

        var range = await SelfRangeAsync(recordId, cancellationToken);
        var link = existing ?? new DimensionLinkDocument { StructureId = structureId, RecordId = recordId };

        link.Parents = [.. InsertLink(link.Parents, parentRecordId, effectiveFrom)];
        ClearOrphanMark(link);

        await _session.SaveCheckedAsync(link, cancellationToken);

        return DimensionResult.Success(
            await RecomputeSubtreeAsync(structureId, recordId, range, cancellationToken),
            [.. errors]);
    }

    public async Task<DimensionResult<int>> MoveAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.MoveDimensionRecords))
        {
            return DimensionResult.NotAuthorised<int>();
        }

        // Cycles across every date, permitted level, level skipping and self-nesting all live
        // in the validator now, so a move made through the designer, the API or an import is
        // held to the same rules by the same code.
        var errors = await _validator.ValidatePlacementAsync(
            structureId, recordId, newParentId, effectiveFrom, cancellationToken);

        if (errors.Any(error => !error.IsAdvisory))
        {
            return DimensionResult.Failed<int>(errors);
        }

        var selfRange = await SelfRangeAsync(recordId, cancellationToken);
        var link = await LoadLinkAsync(structureId, recordId, cancellationToken)
            ?? new DimensionLinkDocument { StructureId = structureId, RecordId = recordId };

        link.Parents = [.. InsertLink(link.Parents, newParentId, effectiveFrom)];
        ClearOrphanMark(link);

        await _session.SaveCheckedAsync(link, cancellationToken);

        var touched = await RecomputeSubtreeAsync(structureId, recordId, selfRange, cancellationToken);

        // Advisories ride along with a successful result: a parent that is not yet effective
        // is a warning the caller should surface, not a reason to refuse.
        return DimensionResult.Success(touched, [.. errors]);
    }

    public Task<DimensionResult<CancelledMoveRestoration>> PreviewCancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        CancelMoveAsync(structureId, recordId, effectiveFrom, apply: false, cancellationToken);

    public Task<DimensionResult<CancelledMoveRestoration>> CancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        CancelMoveAsync(structureId, recordId, effectiveFrom, apply: true, cancellationToken);

    /// <summary>
    /// The one implementation behind both the preview and the cancellation, for the same reason
    /// <see cref="DimensionService"/>'s merge has one implementation behind its dry run and its
    /// apply: the two must never be able to drift apart.
    /// </summary>
    private async Task<DimensionResult<CancelledMoveRestoration>> CancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.MoveDimensionRecords))
        {
            return DimensionResult.NotAuthorised<CancelledMoveRestoration>();
        }

        var link = await LoadLinkAsync(structureId, recordId, cancellationToken);
        var cancelled = link?.Parents.FirstOrDefault(parent => parent.Range.From == effectiveFrom);

        if (link is null || cancelled is null)
        {
            return DimensionResult.Failed<CancelledMoveRestoration>(new DimensionError(
                DimensionRule.MoveNotFound,
                recordId,
                S["There is no move recorded for '{0}' effective on {1}.", recordId, effectiveFrom]));
        }

        // Whatever the move displaced: the link that ran up to the day before it, if any. Its
        // range ending there — rather than wherever it used to run to — is exactly what the move
        // being cancelled recorded when it was made, per InsertLink.
        var previous = link.Parents.FirstOrDefault(parent =>
            parent.Range.To is { } to && to == effectiveFrom.AddDays(-1));

        var restoration = new CancelledMoveRestoration(
            cancelled.ParentRecordId, previous?.ParentRecordId, effectiveFrom, cancelled.Range.To);

        // Restoring a historical fact is still, today, a placement — and dimension types and
        // structures carry no history of their own past configuration, so the only honest check
        // is against the rules as they stand now.
        var today = await _authorisation.TodayAsync();

        var errors = await _validator.ValidatePlacementAsync(
            structureId, recordId, restoration.RestoredParentId, today, cancellationToken);

        if (errors.Any(error => !error.IsAdvisory))
        {
            return DimensionResult.Failed<CancelledMoveRestoration>(errors);
        }

        if (!apply)
        {
            return DimensionResult.Success(restoration, [.. errors]);
        }

        var remaining = link.Parents.Where(parent => parent.Range.From != effectiveFrom).ToList();

        if (previous is not null)
        {
            var index = remaining.IndexOf(previous);
            remaining[index] = previous with { Range = previous.Range with { To = cancelled.Range.To } };
        }

        link.Parents = remaining;

        await _session.SaveCheckedAsync(link, cancellationToken);

        var selfRange = await SelfRangeAsync(recordId, cancellationToken);
        await RecomputeSubtreeAsync(structureId, recordId, selfRange, cancellationToken);

        return DimensionResult.Success(restoration, [.. errors]);
    }

    public async Task RemoveAsync(string recordId, CancellationToken cancellationToken = default)
    {
        foreach (var structure in await _structureLookup.ListAsync(cancellationToken))
        {
            var link = await LoadLinkAsync(structure.StructureId, recordId, cancellationToken);
            var closure = await LoadClosureAsync(structure.StructureId, recordId, cancellationToken);

            if (link is null && closure is null)
            {
                continue;
            }

            // The children first, while the node's own rows still describe where they were.
            var children = await ChildIdsAsync(structure.StructureId, recordId, cancellationToken);

            if (link is not null)
            {
                _session.Delete(link);
            }

            if (closure is not null)
            {
                _session.Delete(closure);
            }

            foreach (var child in children)
            {
                var childLink = await LoadLinkAsync(structure.StructureId, child, cancellationToken);

                if (childLink is not null)
                {
                    childLink.Parents =
                    [
                        .. childLink.Parents.Where(parent =>
                            !string.Equals(parent.ParentRecordId, recordId, StringComparison.Ordinal)),
                    ];

                    await _session.SaveCheckedAsync(childLink, cancellationToken);
                }

                await RecomputeSubtreeAsync(
                    structure.StructureId,
                    child,
                    await SelfRangeAsync(child, cancellationToken),
                    cancellationToken);
            }
        }
    }

    public async Task<int> RebuildAsync(string structureId, CancellationToken cancellationToken = default)
    {
        var nodes = await NodesOnAsync(structureId, cancellationToken);

        var cache = new Dictionary<string, IReadOnlyList<ClosureAncestor>>(StringComparer.Ordinal);
        var written = 0;

        foreach (var node in nodes)
        {
            var ancestors = await ComputeAncestorsAsync(
                structureId,
                node.RecordId,
                cache,
                new HashSet<string>(StringComparer.Ordinal),
                cancellationToken);

            await WriteClosureAsync(structureId, node.RecordId, ancestors, cancellationToken);
            written++;
        }

        return written;
    }

    public async Task<ClosureVerificationReport> VerifyAsync(
        string structureId,
        IReadOnlyList<DateOnly>? asAtDates = null,
        CancellationToken cancellationToken = default)
    {
        var dates = asAtDates is { Count: > 0 }
            ? [.. asAtDates.Distinct().OrderBy(date => date)]
            : await InterestingDatesAsync(structureId, cancellationToken);

        var links = await AllLinksAsync(structureId, cancellationToken);
        var closures = await AllClosuresAsync(structureId, cancellationToken);
        var nodes = await NodesOnAsync(structureId, cancellationToken);
        var divergences = new List<ClosureDivergence>();

        foreach (var date in dates)
        {
            // What the links say, walked from scratch. This is the slow, obviously-correct
            // computation that the index exists to avoid — which is exactly why it is the right
            // thing to check the index against.
            var expected = ExpectedPairsOn(nodes, links, date);

            var actual = closures
                .SelectMany(closure => closure.Ancestors
                    .Where(ancestor => ancestor.Range.Contains(date))
                    .Select(ancestor => (Pair: (ancestor.AncestorId, closure.DescendantId), ancestor.Depth)))
                .ToDictionary(entry => entry.Pair, entry => entry.Depth);

            foreach (var (pair, depth) in actual)
            {
                if (!expected.TryGetValue(pair, out var expectedDepth))
                {
                    divergences.Add(new ClosureDivergence(
                        date, pair.Item1, pair.Item2, ClosureDivergenceKind.InClosureButNotInLinks, depth));
                }
                else if (expectedDepth != depth)
                {
                    divergences.Add(new ClosureDivergence(
                        date, pair.Item1, pair.Item2, ClosureDivergenceKind.DepthDiffers, depth, expectedDepth));
                }
            }

            foreach (var (pair, depth) in expected)
            {
                if (!actual.ContainsKey(pair))
                {
                    divergences.Add(new ClosureDivergence(
                        date, pair.Item1, pair.Item2, ClosureDivergenceKind.InLinksButNotInClosure, null, depth));
                }
            }
        }

        return new ClosureVerificationReport(structureId, dates, divergences);
    }

    // ---- closure computation ----------------------------------------------------------

    /// <summary>
    /// Recomputes the closure for a node and everything below it, and nothing else.
    /// </summary>
    /// <remarks>
    /// Architecture section 4: a move touches "only the subtree, not the whole structure". The
    /// walk is down child links from the moved node, which reaches exactly the nodes whose
    /// ancestor chain can have changed — a node outside the subtree has the same parents it had
    /// and the same ancestors above them.
    /// </remarks>
    /// <returns>How many closure documents were written.</returns>
    private async Task<int> RecomputeSubtreeAsync(
        string structureId,
        string rootId,
        EffectiveRange rootSelfRange,
        CancellationToken cancellationToken)
    {
        var cache = new Dictionary<string, IReadOnlyList<ClosureAncestor>>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string RecordId, EffectiveRange SelfRange)>();

        queue.Enqueue((rootId, rootSelfRange));

        var written = 0;

        while (queue.Count > 0)
        {
            var (recordId, selfRange) = queue.Dequeue();

            if (!done.Add(recordId))
            {
                continue;
            }

            cache.Remove(recordId);

            var ancestors = await ComputeAncestorsAsync(
                structureId,
                recordId,
                cache,
                new HashSet<string>(StringComparer.Ordinal),
                cancellationToken,
                selfRange);

            await WriteClosureAsync(structureId, recordId, ancestors, cancellationToken);
            written++;

            foreach (var child in await ChildIdsAsync(structureId, recordId, cancellationToken))
            {
                if (!done.Contains(child))
                {
                    queue.Enqueue((child, await SelfRangeAsync(child, cancellationToken)));
                }
            }
        }

        return written;
    }

    /// <summary>
    /// A node's ancestors, as the intersection of the link ranges along each path.
    /// </summary>
    /// <param name="visiting">
    /// Guards against a cycle in the stored links. Moves are refused if they would create one,
    /// so reaching this means the data is already corrupt — but a rebuild that never terminates
    /// is a worse way to find out than a rebuild that stops.
    /// </param>
    private async Task<IReadOnlyList<ClosureAncestor>> ComputeAncestorsAsync(
        string structureId,
        string recordId,
        Dictionary<string, IReadOnlyList<ClosureAncestor>> cache,
        HashSet<string> visiting,
        CancellationToken cancellationToken,
        EffectiveRange? knownSelfRange = null)
    {
        if (cache.TryGetValue(recordId, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(recordId))
        {
            return [];
        }

        var selfRange = knownSelfRange ?? await SelfRangeAsync(recordId, cancellationToken);
        var link = await LoadLinkAsync(structureId, recordId, cancellationToken);

        // A node whose type has been dropped from this axis is only on it up to that day.
        if (link?.OnAxisUntil is { } until)
        {
            var capped = selfRange.Intersect(new EffectiveRange(DateOnly.MinValue, until));

            if (capped is null)
            {
                visiting.Remove(recordId);
                cache[recordId] = [];

                return [];
            }

            selfRange = capped.Value;
        }

        var ancestors = new List<ClosureAncestor> { new(recordId, 0, selfRange) };

        foreach (var parent in link?.Parents ?? [])
        {
            // The edge only exists while both the node and the link do.
            var edge = selfRange.Intersect(parent.Range);

            if (edge is null)
            {
                continue;
            }

            var above = await ComputeAncestorsAsync(
                structureId, parent.ParentRecordId, cache, visiting, cancellationToken);

            foreach (var ancestor in above)
            {
                // ... and the ancestor is only an ancestor while its own chain held.
                var range = edge.Value.Intersect(ancestor.Range);

                if (range is not null)
                {
                    ancestors.Add(new ClosureAncestor(ancestor.AncestorId, ancestor.Depth + 1, range.Value));
                }
            }
        }

        visiting.Remove(recordId);

        var normalised = Normalise(ancestors);
        cache[recordId] = normalised;

        return normalised;
    }

    /// <summary>
    /// Merges rows for the same ancestor at the same depth whose ranges touch or overlap, so
    /// that two consecutive links to the same parent produce one row rather than two abutting
    /// ones.
    /// </summary>
    /// <remarks>
    /// Not cosmetic. Verification compares the index against a walk of the links, and a walk
    /// produces one pair per date; leaving the index fragmented would make the two disagree in
    /// shape while agreeing in meaning, and every such difference is noise a real divergence
    /// could hide in.
    /// </remarks>
    private static IReadOnlyList<ClosureAncestor> Normalise(IEnumerable<ClosureAncestor> ancestors)
    {
        var merged = new List<ClosureAncestor>();

        foreach (var group in ancestors
            .Where(ancestor => !ancestor.Range.IsEmpty)
            .GroupBy(ancestor => (ancestor.AncestorId, ancestor.Depth)))
        {
            ClosureAncestor? open = null;

            foreach (var ancestor in group.OrderBy(entry => entry.Range.From))
            {
                if (open is null)
                {
                    open = ancestor;
                    continue;
                }

                var touches = open.Range.To is null ||
                    ancestor.Range.From <= open.Range.To.Value.AddDays(1);

                if (!touches)
                {
                    merged.Add(open);
                    open = ancestor;

                    continue;
                }

                var to = open.Range.To is null || ancestor.Range.To is null
                    ? (DateOnly?)null
                    : (ancestor.Range.To > open.Range.To ? ancestor.Range.To : open.Range.To);

                open = open with { Range = new EffectiveRange(open.Range.From, to) };
            }

            if (open is not null)
            {
                merged.Add(open);
            }
        }

        return [.. merged.OrderBy(ancestor => ancestor.Depth).ThenBy(ancestor => ancestor.Range.From)];
    }

    /// <summary>
    /// Places a new parent link from <paramref name="effectiveFrom"/>, closing whatever it
    /// displaces and leaving later links alone.
    /// </summary>
    /// <remarks>
    /// The same shape as a substantive rename, and for the same reason: a backdated move
    /// corrects one period of the history, it does not erase the periods after it. A move to
    /// 1 March on a node that was already moved on 1 June leaves the June move standing and
    /// occupies only March to May.
    /// </remarks>
    private static IEnumerable<ParentLink> InsertLink(
        IReadOnlyList<ParentLink> existing,
        string? newParentId,
        DateOnly effectiveFrom)
    {
        var covering = existing.FirstOrDefault(link => link.Range.Contains(effectiveFrom));
        var result = new List<ParentLink>();

        foreach (var link in existing.OrderBy(link => link.Range.From))
        {
            if (link == covering)
            {
                // Keep the part of it that is before the move. Nothing is left of it when the
                // move starts on the day the link did.
                if (link.Range.From < effectiveFrom)
                {
                    result.Add(link with { Range = link.Range.EndingOn(effectiveFrom.AddDays(-1)) });
                }

                continue;
            }

            // Links wholly after the move are untouched; links wholly before it likewise.
            result.Add(link);
        }

        if (newParentId is not null)
        {
            // The new link runs to wherever the one it displaced ran to, so it cannot swallow a
            // later move. With nothing displaced, it runs until the next link starts.
            var to = covering?.Range.To
                ?? existing
                    .Where(link => link.Range.From > effectiveFrom)
                    .OrderBy(link => link.Range.From)
                    .Select(link => (DateOnly?)link.Range.From.AddDays(-1))
                    .FirstOrDefault();

            result.Add(new ParentLink(newParentId, new EffectiveRange(effectiveFrom, to)));
        }

        return result.OrderBy(link => link.Range.From);
    }

    /// <summary>
    /// Forgets that a record was ever orphaned by a retirement. Called whenever it gains a parent
    /// again, because from that moment the badge would be describing something that is no longer
    /// true — and a record moved to the top deliberately is unplaced on purpose, not stranded.
    /// </summary>
    private static void ClearOrphanMark(DimensionLinkDocument link)
    {
        link.OrphanedByParentRetirementOn = null;
        link.OrphanedFromParentId = null;
    }

    private static IEnumerable<ParentLink> CloseAt(IReadOnlyList<ParentLink> links, DateOnly lastDay) =>
        links
            .Where(link => link.Range.From <= lastDay)
            .Select(link => link.Range.To is null || link.Range.To > lastDay
                ? link with { Range = link.Range.EndingOn(lastDay) }
                : link);

    // ---- verification helpers ---------------------------------------------------------

    /// <summary>
    /// Every ancestor-descendant pair the links imply on one date, computed by walking up from
    /// each node.
    /// </summary>
    /// <param name="nodes">
    /// Every record whose dimension type is a level of this structure, with its effective
    /// range. The node set comes from the records rather than from the link documents, because
    /// a root that has never been moved has no link document at all and would otherwise be
    /// reported as a divergence on every run — the index would be right and the check wrong.
    /// </param>
    private static Dictionary<(string, string), int> ExpectedPairsOn(
        IReadOnlyList<DimensionNodeRef> nodes,
        IReadOnlyList<DimensionLinkDocument> links,
        DateOnly date)
    {
        var linkOf = links.ToDictionary(link => link.RecordId, link => link, StringComparer.Ordinal);

        // A node is on the axis on this date if its record is effective then and its type has
        // not been dropped from the structure.
        var onAxis = nodes
            .Where(node =>
                node.EffectiveRange.Contains(date) &&
                !(linkOf.TryGetValue(node.RecordId, out var link) &&
                    link.OnAxisUntil is { } until &&
                    date > until))
            .Select(node => node.RecordId)
            .ToHashSet(StringComparer.Ordinal);

        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in onAxis)
        {
            if (linkOf.TryGetValue(node, out var link) && link.ParentOn(date) is { } parent && onAxis.Contains(parent))
            {
                parentOf[node] = parent;
            }
        }

        var pairs = new Dictionary<(string, string), int>();

        foreach (var node in onAxis)
        {
            pairs[(node, node)] = 0;

            var current = node;
            var depth = 0;
            var guard = new HashSet<string>(StringComparer.Ordinal) { node };

            while (parentOf.TryGetValue(current, out var parent) && guard.Add(parent))
            {
                depth++;
                pairs[(parent, node)] = depth;
                current = parent;
            }
        }

        return pairs;
    }

    /// <summary>
    /// The dates worth comparing on: every day a link starts or ends, the day either side of
    /// each, and today.
    /// </summary>
    /// <remarks>
    /// Boundaries are where dated logic goes wrong, and an index is correct today far more
    /// often than it is correct for last March. Checking only today would miss exactly the
    /// drift that matters.
    /// </remarks>
    private async Task<IReadOnlyList<DateOnly>> InterestingDatesAsync(
        string structureId,
        CancellationToken cancellationToken)
    {
        var dates = new SortedSet<DateOnly> { await _authorisation.TodayAsync() };

        foreach (var link in await AllLinksAsync(structureId, cancellationToken))
        {
            foreach (var parent in link.Parents)
            {
                Add(parent.Range.From);

                if (parent.Range.To is { } to)
                {
                    Add(to);
                }
            }

            if (link.OnAxisUntil is { } until)
            {
                Add(until);
            }
        }

        return [.. dates];

        void Add(DateOnly date)
        {
            dates.Add(date);

            if (date > DateOnly.MinValue)
            {
                dates.Add(date.AddDays(-1));
            }

            if (date < EffectiveDates.OpenEndedDate)
            {
                dates.Add(date.AddDays(1));
            }
        }
    }

    // ---- storage ----------------------------------------------------------------------

    private Task<DimensionLinkDocument?> LoadLinkAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken) =>
        LoadOneAsync<DimensionLinkDocument, DimensionLinkIndex>(
            index => index.StructureId == structureId && index.ChildId == recordId,
            document => document.StructureId == structureId && document.RecordId == recordId,
            cancellationToken);

    private Task<DimensionClosureDocument?> LoadClosureAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken) =>
        LoadOneAsync<DimensionClosureDocument, DimensionClosureIndex>(
            index => index.StructureId == structureId && index.DescendantId == recordId,
            document => document.StructureId == structureId && document.DescendantId == recordId,
            cancellationToken);

    /// <summary>
    /// Loads the one document behind a set of index rows.
    /// </summary>
    /// <remarks>
    /// A document maps to many index rows, so a query through the index returns the document
    /// once per matching row. The in-memory predicate is belt and braces for that: it also
    /// catches a document whose index rows are stale, which is the failure this whole layer
    /// exists to make visible rather than silent.
    /// </remarks>
    private async Task<TDocument?> LoadOneAsync<TDocument, TIndex>(
        System.Linq.Expressions.Expression<Func<TIndex, bool>> indexPredicate,
        Func<TDocument, bool> documentPredicate,
        CancellationToken cancellationToken)
        where TDocument : class
        where TIndex : class, YesSql.Indexes.IIndex
    {
        var documents = await _session
            .Query<TDocument, TIndex>(indexPredicate)
            .ListAsync(cancellationToken);

        return documents.FirstOrDefault(documentPredicate);
    }

    private async Task WriteClosureAsync(
        string structureId,
        string recordId,
        IReadOnlyList<ClosureAncestor> ancestors,
        CancellationToken cancellationToken)
    {
        var document = await LoadClosureAsync(structureId, recordId, cancellationToken)
            ?? new DimensionClosureDocument { StructureId = structureId, DescendantId = recordId };

        document.Ancestors = ancestors;

        await _session.SaveCheckedAsync(document, cancellationToken);
    }

    private async Task<IReadOnlyList<DimensionLinkDocument>> AllLinksAsync(
        string structureId,
        CancellationToken cancellationToken)
    {
        var documents = await _session
            .Query<DimensionLinkDocument, DimensionLinkIndex>(index => index.StructureId == structureId)
            .ListAsync(cancellationToken);

        return [.. documents.Where(document => document.StructureId == structureId).DistinctBy(document => document.Id)];
    }

    private async Task<IReadOnlyList<DimensionClosureDocument>> AllClosuresAsync(
        string structureId,
        CancellationToken cancellationToken)
    {
        var documents = await _session
            .Query<DimensionClosureDocument, DimensionClosureIndex>(index => index.StructureId == structureId)
            .ListAsync(cancellationToken);

        return [.. documents.Where(document => document.StructureId == structureId).DistinctBy(document => document.Id)];
    }

    private async Task<IReadOnlyList<string>> ChildIdsAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken)
    {
        // Every child the node has ever had on this axis, not only today's: a move changes the
        // ancestor chain of every one of them, on the dates they were there.
        var rows = await _session
            .QueryIndex<DimensionLinkIndex>(index =>
                index.StructureId == structureId && index.ParentId == recordId)
            .ListAsync(cancellationToken);

        return [.. rows.Select(row => row.ChildId).Distinct(StringComparer.Ordinal)];
    }

    private async Task<EffectiveRange> SelfRangeAsync(string recordId, CancellationToken cancellationToken)
    {
        var row = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.ContentItemId == recordId && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            // Defensive: a node with no record row should not exist. Treating it as always
            // effective keeps a rebuild from silently dropping it, so verification reports the
            // problem rather than the index quietly agreeing with its own gap.
            ? new EffectiveRange(DateOnly.MinValue, null)
            : new EffectiveRange(
                EffectiveDates.FromColumn(row.EffectiveFrom),
                EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive));
    }

    /// <summary>
    /// Every record that belongs on this axis, now or in the past.
    /// </summary>
    /// <remarks>
    /// Both the rebuild and the verification need the same set, and they must agree on it. A
    /// rebuild that worked from one definition of "on this axis" and a verification that worked
    /// from another would report divergences that are only disagreements between the two
    /// commands.
    ///
    /// The set is the records of the current levels <em>plus</em> any record with a link
    /// document on this structure. The second half is what covers a record whose dimension type
    /// has since been dropped as a level: it is no longer on the axis today, but it was, and
    /// its capped rows are correct history rather than drift.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionNodeRef>> NodesOnAsync(
        string structureId,
        CancellationToken cancellationToken)
    {
        var structure = await _structureLookup.GetAsync(structureId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no structure '{structureId}' in this tenant.");

        var nodes = new Dictionary<string, DimensionNodeRef>(StringComparer.Ordinal);

        foreach (var level in structure.Levels)
        {
            foreach (var node in await RecordsOfTypeAsync(level.DimensionTypeId, cancellationToken))
            {
                nodes[node.RecordId] = node;
            }
        }

        var departed = (await AllLinksAsync(structureId, cancellationToken))
            .Select(link => link.RecordId)
            .Where(recordId => !nodes.ContainsKey(recordId))
            .ToArray();

        if (departed.Length > 0)
        {
            var rows = await _session
                .QueryIndex<DimensionRecordPartIndex>(index =>
                    index.ContentItemId.IsIn(departed) && index.Latest)
                .ListAsync(cancellationToken);

            foreach (var row in rows)
            {
                nodes[row.ContentItemId] = ToNodeRef(row);
            }
        }

        return [.. nodes.Values];
    }

    private async Task<IReadOnlyList<DimensionNodeRef>> RecordsOfTypeAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken)
    {
        var rows = await _session
            .QueryIndex<DimensionRecordPartIndex>(index =>
                index.DimensionTypeId == dimensionTypeId && index.Latest)
            .ListAsync(cancellationToken);

        return [.. rows.Select(ToNodeRef)];
    }

    private async Task<IReadOnlyList<Models.StructureDocument>> StructuresWithLevelAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken)
    {
        var structures = await _structureLookup.ListAsync(cancellationToken);

        return [.. structures.Where(structure => structure.OrdinalOf(dimensionTypeId) is not null)];
    }

    private async Task<IReadOnlyList<DimensionNodeRef>> HydrateAsync(
        IEnumerable<(string RecordId, int Depth)> pairs,
        CancellationToken cancellationToken)
    {
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (recordId, depth) in pairs)
        {
            // A node can appear at two depths on one date only if the data is inconsistent;
            // showing the nearest is the least misleading answer.
            if (!depths.TryGetValue(recordId, out var existing) || depth < existing)
            {
                depths[recordId] = depth;
            }
        }

        if (depths.Count == 0)
        {
            return [];
        }

        var ids = depths.Keys.ToArray();

        var rows = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.ContentItemId.IsIn(ids) && index.Latest)
            .ListAsync(cancellationToken);

        return
        [
            .. rows
                .Select(row => ToNodeRef(row) with { Depth = depths[row.ContentItemId] })
                .OrderBy(node => node.Depth)
                .ThenBy(node => node.SortOrder)
                .ThenBy(node => node.NameEn, StringComparer.Ordinal),
        ];
    }

    private static DimensionNodeRef ToNodeRef(DimensionRecordPartIndex row) => DimensionNodeRef.FromIndex(row);

    private async Task<DateOnly> ResolveDateAsync(DateOnly? asAt) =>
        asAt ?? await _authorisation.TodayAsync();
}
