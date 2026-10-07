using System.Globalization;
using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.ContentFields.Fields;
using OrchardCore.ContentManagement;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Fields;
using YesSql;

namespace WorkMate.Dimensions.Internal.Graph;

/// <inheritdoc />
internal sealed class DimensionService : IDimensionService
{
    private readonly ISession _session;
    private readonly IContentManager _contentManager;
    private readonly IDimensionTypeService _dimensionTypeService;

    /// <summary>
    /// Needed only to ask what is still hanging off a record on the day it would retire, which is
    /// a question about every structure the record sits on rather than about one.
    /// </summary>
    private readonly IStructureService _structureService;

    private readonly IDimensionGraphService _graph;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IDimensionValidator _validator;
    private readonly IAuditTrailManager _auditTrailManager;

    /// <summary>
    /// The read side of this aggregate. <see cref="GetAsync"/> and <see cref="GetByCodeAsync"/>
    /// delegate to it, so there is one implementation of "resolve a record" rather than two that
    /// could drift from the one <see cref="IDimensionValidator"/> uses.
    /// </summary>
    private readonly IDimensionRecordLookup _recordLookup;

    private readonly IStringLocalizer S;

    public DimensionService(
        ISession session,
        IContentManager contentManager,
        IDimensionTypeService dimensionTypeService,
        IStructureService structureService,
        IDimensionGraphService graph,
        IEmployeeAssignmentService assignments,
        IDimensionAuthorisation authorisation,
        IDimensionValidator validator,
        IAuditTrailManager auditTrailManager,
        IDimensionRecordLookup recordLookup,
        IStringLocalizer<DimensionService> stringLocalizer)
    {
        _session = session;
        _contentManager = contentManager;
        _dimensionTypeService = dimensionTypeService;
        _structureService = structureService;
        _graph = graph;
        _assignments = assignments;
        _authorisation = authorisation;
        _validator = validator;
        _auditTrailManager = auditTrailManager;
        _recordLookup = recordLookup;
        S = stringLocalizer;
    }

    public async Task<DimensionResult<DimensionNodeRef>> CreateAsync(
        string dimensionTypeId,
        string code,
        BilingualText name,
        EffectiveRange effectiveRange,
        DimensionValidationBatch? batch = null,
        int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        // The validator first, because it is the authority and because it is the only thing
        // that can see duplicates within an import batch — two rows of one file sharing a code
        // are each individually fine and together are not, and neither is committed yet.
        var errors = await _validator.ValidateRecordAsync(
            recordId: null,
            dimensionTypeId,
            code,
            name,
            effectiveRange,
            batch,
            cancellationToken);

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<DimensionNodeRef>(errors);
        }

        var type = await _dimensionTypeService.GetAsync(dimensionTypeId, asAt: null, cancellationToken);

        if (type is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(new DimensionError(
                DimensionRule.UnknownReference,
                dimensionTypeId,
                S["There is no dimension type with the id '{0}' in this tenant.", dimensionTypeId]));
        }

        var item = await _contentManager.NewAsync(type.ContentTypeName);
        var order = sortOrder ?? await NextSortOrderAsync(cancellationToken);

        item.Alter<DimensionRecordPart>(part =>
        {
            part.Code = code;
            part.NameEn = name.En;
            part.NameAr = name.Ar;
            part.DimensionTypeId = dimensionTypeId;
            part.SortOrder = order;
            part.EffectiveFrom = effectiveRange.From;
            part.EffectiveTo = effectiveRange.To;
        });

        await _contentManager.UpdateAsync(item);

        // The handler's rules run here — code format, uniqueness, bilingual name, dates — so
        // every path into a record is held to the same standard, this one included.
        var validated = await _contentManager.ValidateAsync(item);

        if (!validated.Succeeded)
        {
            return DimensionResult.Failed<DimensionNodeRef>(
            [
                .. validated.Errors.Select(error => new DimensionError(
                    DimensionRule.CodeFormat,
                    code,
                    new LocalizedString(error.ErrorMessage ?? string.Empty, error.ErrorMessage ?? string.Empty))),
            ]);
        }

        await _contentManager.CreateAsync(item, VersionOptions.Published);

        // Flushed so the record's index row exists before the graph service reads it for the
        // self pair's effective range.
        await _session.FlushAsync(cancellationToken);

        await _graph.EnsureSelfPairsAsync(item.ContentItemId, dimensionTypeId, effectiveRange, cancellationToken);
        await StartNameHistoryAsync(item.ContentItemId, name, effectiveRange.From, cancellationToken);

        return DimensionResult.Success(ToNodeRef(item.ContentItemId, code, name, dimensionTypeId, effectiveRange, order));
    }

    /// <summary>
    /// The sort order a record gets when its caller does not state one: one past the highest in
    /// the tenant, so records sort in the order they were created.
    /// </summary>
    /// <remarks>
    /// Siblings are ordered by <c>SortOrder</c> and then by English name, so leaving every record
    /// at zero made that second clause the only one that ever applied and every tree came out
    /// alphabetical. A recipe that lists Engineering, Projects and Corporate in that order means
    /// that order — it is the order the organisation itself puts them in — and a unit added in
    /// the designer belongs after the siblings that were already there, not wherever its initial
    /// falls.
    ///
    /// Tenant-wide rather than per-parent, which costs one indexed read instead of a sibling
    /// query and gives the same answer for the case that matters: a record created later sorts
    /// after one created earlier, whichever parent each ends up under, including a record moved
    /// under a parent it was not created beneath. An operator who wants a different order sets
    /// <c>SortOrder</c> on the record, or a recipe states it, and that wins.
    ///
    /// Existing tenants are untouched: every record already written sits at zero and therefore
    /// stays in the alphabetical order it is in today until something renumbers it. That is why
    /// this is not a migration — there is no correct order to invent for records whose creation
    /// order was never recorded.
    /// </remarks>
    private async Task<int> NextSortOrderAsync(CancellationToken cancellationToken)
    {
        var highest = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.Latest)
            .OrderByDescending(index => index.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        return highest is null ? 0 : highest.SortOrder + 1;
    }

    public async Task<DimensionResult<DimensionNodeRef>> AddUnitAsync(
        string structureId,
        string? parentRecordId,
        string dimensionTypeId,
        string code,
        BilingualText name,
        DateOnly effectiveFrom,
        IReadOnlyList<DimensionAttributeValue>? attributes = null,
        int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        var type = await _dimensionTypeService.GetAsync(dimensionTypeId, asAt: null, cancellationToken);

        if (type is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(new DimensionError(
                DimensionRule.UnknownReference,
                dimensionTypeId,
                S["There is no dimension type with the id '{0}' in this tenant.", dimensionTypeId]));
        }

        // Everything is checked before anything is written. A unit that exists but never reached
        // its parent is precisely the half-finished state the unplaced panel reports, and the
        // caller of one method should not be the one responsible for avoiding it.
        var range = new EffectiveRange(effectiveFrom, null);

        var errors = new List<DimensionError>(await _validator.ValidateRecordAsync(
            recordId: null, dimensionTypeId, code, name, range, batch: null, cancellationToken));

        errors.AddRange(ValidateAttributes(type, attributes));

        if (errors.Any(error => !error.IsAdvisory))
        {
            return DimensionResult.Failed<DimensionNodeRef>(errors);
        }

        var created = await CreateAsync(dimensionTypeId, code, name, range, batch: null, sortOrder, cancellationToken);

        if (!created.Succeeded)
        {
            return created;
        }

        var recordId = created.Value!.RecordId;

        if (attributes is { Count: > 0 })
        {
            await WriteAttributesAsync(recordId, type, attributes, cancellationToken);
        }

        var placed = await _graph.PlaceAsync(structureId, recordId, parentRecordId, effectiveFrom, cancellationToken);

        if (!placed.Succeeded)
        {
            // The record was written and its placement was refused. Rather than leave a unit
            // nobody asked for, it is taken back out: the graph rows first, then the content item.
            // The validator ran over both before either was written, so arriving here means
            // something changed underneath us, and reporting the placement's own errors is more
            // use than a generic failure.
            await _graph.RemoveAsync(recordId, cancellationToken);
            await _contentManager.RemoveAsync((await _contentManager.GetAsync(recordId))!);

            return placed.IsAuthorised
                ? DimensionResult.Failed<DimensionNodeRef>(placed.Errors)
                : DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        await RecordRecordChangeAsync(new DimensionRecordAuditEvent
        {
            RecordId = recordId,
            Code = code,
            Operation = DimensionRecordOperation.Added,
            EffectiveFrom = effectiveFrom,
            StructureId = structureId,
            ParentRecordId = parentRecordId,
            After = DimensionRecordNameState.Of(name),
        });

        // Advisories from both halves ride along: a parent that is not yet effective on the day
        // the child starts is a warning, per the architecture's validation table, not a refusal.
        return DimensionResult.Success(
            (await GetAsync(recordId, effectiveFrom, cancellationToken))!,
            [.. errors.Concat(placed.Errors).Where(error => error.IsAdvisory)]);
    }

    public async Task<DimensionResult<DimensionNodeRef>> UpdateAsync(
        string recordId,
        string costCentreCode,
        string glAccountRef,
        string headEmployeeId,
        int sortOrder,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        var item = await _contentManager.GetAsync(recordId);

        if (item is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(UnknownRecord(recordId));
        }

        item.Alter<DimensionRecordPart>(part =>
        {
            part.CostCentreCode = costCentreCode;
            part.GlAccountRef = glAccountRef;
            part.HeadEmployeeId = headEmployeeId;
            part.SortOrder = sortOrder;
            part.IsActive = isActive;
        });

        await _contentManager.UpdateAsync(item);

        return DimensionResult.Success((await GetAsync(recordId, null, cancellationToken))!);
    }

    public Task<DimensionResult<DimensionNodeRef>> CorrectNameAsync(
        string recordId,
        BilingualText name,
        DateOnly withinPeriodContaining,
        CancellationToken cancellationToken = default) =>
        ChangeNameAsync(
            recordId,
            name,
            periods => DimensionNameHistory.Correct(periods, name, withinPeriodContaining),
            // A correction applies retrospectively, so the record's current name only changes
            // when the period corrected is the current one.
            updatesCurrentName: periods => periods.Count > 0 && periods[^1].Range.Contains(withinPeriodContaining),
            S["There is no name in effect on {0} to correct.", withinPeriodContaining],
            DimensionRecordOperation.NameCorrected,
            withinPeriodContaining,
            cancellationToken);

    public Task<DimensionResult<DimensionNodeRef>> RenameAsync(
        string recordId,
        BilingualText name,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        ChangeNameAsync(
            recordId,
            name,
            periods => DimensionNameHistory.Rename(periods, name, effectiveFrom, out _),
            // A substantive rename always ends up as the last period, so it is always the
            // current name once applied.
            updatesCurrentName: _ => true,
            S["The record had no name on {0}, so it cannot be renamed from then.", effectiveFrom],
            DimensionRecordOperation.Renamed,
            effectiveFrom,
            cancellationToken);

    public async Task<DimensionResult<RetirePlan>> PlanRetireAsync(
        string recordId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<RetirePlan>();
        }

        var existing = await GetAsync(recordId, null, cancellationToken);

        if (existing is null)
        {
            return DimensionResult.Failed<RetirePlan>(UnknownRecord(recordId));
        }

        return DimensionResult.Success(
            await BuildRetirePlanAsync(recordId, effectiveDate, cancellationToken));
    }

    /// <summary>
    /// The assessment, the counts and the children behind it, shared by the dry run and the write
    /// so the two can never disagree about what is at stake.
    /// </summary>
    private async Task<RetirePlan> BuildRetirePlanAsync(
        string recordId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var assessment = await _validator.AssessDeletionAsync(recordId, cancellationToken);

        // Asked as at the day the unit closes, not the day before it. The question is which units
        // are left with nowhere to sit once it has gone, and that is a question about the first
        // day it is gone: a unit opened on the same morning the parent closes would be invisible
        // to a plan that looked at yesterday, and is exactly the unit most likely to be stranded.
        //
        // Safe to ask of a record that has not retired yet, which is the only time it is asked:
        // its own range is still open here, so the closure still resolves everything under it.
        var descendants = 0;
        var employees = 0;
        var byStructure = new List<StructureChildren>();

        // Every structure, not only the one the screen is showing: retirement closes the record,
        // so it lands on all of them at once.
        foreach (var structure in await _structureService.ListAsync(cancellationToken))
        {
            var page = await _graph.GetDescendantsAsync(
                structure.StructureId, recordId, effectiveDate, skip: 0, take: 1, cancellationToken);

            descendants += page.Total;

            var counts = await _assignments.CountEmployeesAtAsync(
                structure.StructureId, [recordId], effectiveDate, cancellationToken);

            employees += counts.TryGetValue(recordId, out var count) ? count : 0;

            var directChildren = await _graph.GetChildrenAsync(
                structure.StructureId, recordId, effectiveDate, cancellationToken);

            if (directChildren.Count == 0)
            {
                continue;
            }

            var subtree = await SubtreeAsync(structure.StructureId, recordId, effectiveDate, cancellationToken);

            byStructure.Add(new StructureChildren(
                structure.StructureId,
                structure.Code,
                structure.Name.En,
                structure.Name.Ar,
                directChildren,
                subtree,
                await ValidMoveTargetsAsync(
                    structure.StructureId, recordId, directChildren, subtree, effectiveDate, cancellationToken)));
        }

        return new RetirePlan(
            recordId,
            effectiveDate,
            assessment.Outcome,
            assessment.Blockers,
            descendants,
            employees,
            byStructure);
    }

    /// <summary>Everything under a node on a date, nearest first.</summary>
    /// <remarks>
    /// Paged out in full rather than counted, because a cascade has to close every one of them and
    /// the preview has to show the whole subtree before it does. The page size is the whole point
    /// of asking in a loop: an organisation with more units under one branch than fits in a single
    /// page is exactly the one where closing it blind would do the most damage.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionNodeRef>> SubtreeAsync(
        string structureId,
        string recordId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        const int PageSize = 200;

        var all = new List<DimensionNodeRef>();

        while (true)
        {
            var page = await _graph.GetDescendantsAsync(
                structureId, recordId, asAt, all.Count, PageSize, cancellationToken);

            all.AddRange(page.Items);

            if (all.Count >= page.Total || page.Items.Count == 0)
            {
                break;
            }
        }

        return [.. all.OrderBy(node => node.Depth).ThenBy(node => node.NameEn, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Where the children could go instead: every unit on the structure whose level permits all of
    /// their types, minus the branch that is closing.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="IDimensionGraphService.GetPermittedChildTypeIdsAsync"/> — the same
    /// rule the validator enforces, read from the parent's side — so the picker cannot offer a
    /// target the write would then refuse. The service re-validates each move anyway; this is what
    /// keeps the screen from presenting choices that were never going to work.
    /// </remarks>
    private async Task<IReadOnlyList<DimensionNodeRef>> ValidMoveTargetsAsync(
        string structureId,
        string retiringRecordId,
        IReadOnlyList<DimensionNodeRef> children,
        IReadOnlyList<DimensionNodeRef> subtree,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var childTypeIds = children
            .Select(child => child.DimensionTypeId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The closing branch is not a destination: its own units are about to have no parent
        // themselves, and moving a child into its own subtree is a cycle.
        var excluded = new HashSet<string>(StringComparer.Ordinal) { retiringRecordId };
        excluded.UnionWith(subtree.Select(node => node.RecordId));

        var candidates = new List<DimensionNodeRef>();
        var roots = await _graph.GetRootsAsync(structureId, asAt, cancellationToken);

        candidates.AddRange(roots);

        foreach (var root in roots)
        {
            candidates.AddRange(await SubtreeAsync(structureId, root.RecordId, asAt, cancellationToken));
        }

        // A unit with no parent of its own is still a perfectly good parent for something else.
        candidates.AddRange(await _graph.GetUnplacedAsync(structureId, asAt, cancellationToken));

        var permitted = new List<DimensionNodeRef>();

        foreach (var candidate in candidates
            .DistinctBy(node => node.RecordId, StringComparer.Ordinal)
            .Where(node => !excluded.Contains(node.RecordId)))
        {
            var permittedTypes = await _graph.GetPermittedChildTypeIdsAsync(
                structureId, candidate.RecordId, asAt, cancellationToken);

            if (childTypeIds.All(typeId => permittedTypes.Contains(typeId, StringComparer.Ordinal)))
            {
                permitted.Add(candidate);
            }
        }

        return [.. permitted.OrderBy(node => node.NameEn, StringComparer.Ordinal)];
    }

    public async Task<DimensionResult<DimensionNodeRef>> RetireAsync(
        string recordId,
        DateOnly effectiveDate,
        IReadOnlyDictionary<string, ChildrenDisposition>? childrenDispositions = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        // Re-planned here rather than trusted from the screen: the screen is not the authority,
        // and a unit can gain a child between the preview and the confirmation.
        var plan = await BuildRetirePlanAsync(recordId, effectiveDate, cancellationToken);
        var dispositions = childrenDispositions ?? new Dictionary<string, ChildrenDisposition>(StringComparer.Ordinal);

        // Named one at a time, because "there are children somewhere" is not something a person
        // can act on — the structure they are not looking at is exactly the one they need told.
        var undecided = plan.ChildrenByStructure
            .Where(entry => !dispositions.ContainsKey(entry.StructureId))
            .ToList();

        if (undecided.Count > 0)
        {
            return DimensionResult.Failed<DimensionNodeRef>(
            [
                .. undecided.Select(entry => new DimensionError(
                    DimensionRule.ChildrenNeedDisposition,
                    entry.StructureCode,
                    S["{0} units still sit under this one on '{1}' on {2}. Say what should happen to them before retiring it.",
                        entry.DirectChildren.Count,
                        ViewModels.BilingualDisplay.Name(entry.StructureNameEn, entry.StructureNameAr),
                        ViewModels.BilingualDisplay.Date(effectiveDate)])),
            ]);
        }

        foreach (var entry in plan.ChildrenByStructure)
        {
            if (await DisposeOfChildrenAsync(
                plan, entry, dispositions[entry.StructureId], cancellationToken) is { } refusal)
            {
                return refusal;
            }
        }

        return await CloseRecordAsync(recordId, effectiveDate, cancellationToken);
    }

    /// <summary>
    /// Carries out the decision about the children, or returns the reason it cannot. Runs before
    /// the parent closes, so that a refusal leaves the organisation exactly as it was.
    /// </summary>
    private async Task<DimensionResult<DimensionNodeRef>?> DisposeOfChildrenAsync(
        RetirePlan plan,
        StructureChildren onStructure,
        ChildrenDisposition disposition,
        CancellationToken cancellationToken)
    {
        switch (disposition.Kind)
        {
            case ChildrenDispositionKind.MoveToParent:
                foreach (var child in onStructure.DirectChildren)
                {
                    // The same date the parent closes, so there is no day on which the child sits
                    // nowhere: its old placement runs to the day before and the new one starts here.
                    var moved = await _graph.MoveAsync(
                        onStructure.StructureId,
                        child.RecordId,
                        disposition.NewParentRecordId,
                        plan.EffectiveDate,
                        cancellationToken);

                    if (!moved.Succeeded)
                    {
                        return moved.IsAuthorised
                            ? DimensionResult.Failed<DimensionNodeRef>(moved.Errors)
                            : DimensionResult.NotAuthorised<DimensionNodeRef>();
                    }
                }

                return null;

            case ChildrenDispositionKind.RetireCascade:
                // Shallowest first, so each unit closes after the one above it: by the time a node
                // is reached its ancestors are already capped, and the closure it writes on the way
                // through is its final one rather than one that has to be written again.
                foreach (var descendant in onStructure.Subtree.OrderBy(node => node.Depth))
                {
                    var closed = await CloseRecordAsync(
                        descendant.RecordId, plan.EffectiveDate, cancellationToken, plan.RecordId);

                    if (!closed.Succeeded)
                    {
                        return DimensionResult.Failed<DimensionNodeRef>(closed.Errors);
                    }
                }

                return null;

            default:
                await _graph.MarkOrphanedByParentRetirementAsync(
                    onStructure.StructureId,
                    [.. onStructure.DirectChildren.Select(child => child.RecordId)],
                    plan.RecordId,
                    plan.EffectiveDate,
                    cancellationToken);

                return null;
        }
    }

    /// <summary>
    /// Closes a record with an end date, with no deletion assessment.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RetireAsync"/> because a merge retires its source as part of
    /// succeeding, and the references that would block a standalone retirement are the ones the
    /// merge has just deliberately left behind: the children and employees it reassigned still
    /// resolve through the source for prior periods, which the architecture requires. Asking the
    /// deletion assessment about that is asking the wrong question — it is there to stop a user
    /// closing a unit that something still depends on, not to stop an operation that has already
    /// dealt with everything that depended on it.
    /// </remarks>
    /// <param name="cascadedFrom">
    /// The unit whose retirement closed this one too, when it was not retired in its own right.
    /// Recorded on the audit entry so that a unit closing on a date it was never named for can be
    /// traced back to the decision that closed it.
    /// </param>
    private async Task<DimensionResult<DimensionNodeRef>> CloseRecordAsync(
        string recordId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken,
        string? cascadedFrom = null)
    {
        var item = await _contentManager.GetAsync(recordId);

        if (item is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(UnknownRecord(recordId));
        }

        // Retirement, not deletion: the record is closed with an end date, disappears from
        // pickers and continues to resolve for historical queries. Architecture section 6.
        var lastDay = effectiveDate.AddDays(-1);

        item.Alter<DimensionRecordPart>(part =>
        {
            part.EffectiveTo = lastDay;
            part.IsActive = false;
        });

        await _contentManager.UpdateAsync(item);
        await _session.FlushAsync(cancellationToken);

        var part = item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!;

        // The closure has to shrink with the record, or a retired unit keeps appearing under
        // its parent for dates after it closed.
        await _graph.EnsureSelfPairsAsync(
            recordId,
            part.DimensionTypeId,
            new EffectiveRange(part.EffectiveFrom, lastDay),
            cancellationToken);

        await RecordRecordChangeAsync(new DimensionRecordAuditEvent
        {
            RecordId = recordId,
            Code = part.Code,
            Operation = DimensionRecordOperation.Retired,
            EffectiveFrom = effectiveDate,
            CascadedFromRecordId = cascadedFrom,
            Before = new DimensionRecordNameState { NameEn = part.NameEn, NameAr = part.NameAr },
        });

        return DimensionResult.Success((await GetAsync(recordId, part.EffectiveFrom, cancellationToken))!);
    }

    public async Task<DimensionResult<DimensionNodeRef>> MoveAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        var moved = await _graph.MoveAsync(structureId, recordId, newParentId, effectiveFrom, cancellationToken);

        if (!moved.IsAuthorised)
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        if (!moved.Succeeded)
        {
            return DimensionResult.Failed<DimensionNodeRef>(moved.Errors);
        }

        return DimensionResult.Success((await GetAsync(recordId, effectiveFrom, cancellationToken))!);
    }

    public Task<DimensionResult<CancelMovePlan>> PlanCancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        CancelMoveAsync(structureId, recordId, effectiveFrom, reason: null, apply: false, cancellationToken);

    public Task<DimensionResult<CancelMovePlan>> CancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return CancelMoveAsync(structureId, recordId, effectiveFrom, reason, apply: true, cancellationToken);
    }

    /// <summary>
    /// The one implementation behind both the dry run and the cancellation, for the same reason
    /// the private merge implementation below is one method with an <paramref name="apply"/>
    /// flag rather than two that look alike: the plan is computed once, so the report a human
    /// sees cannot drift from what applying it does.
    /// </summary>
    private async Task<DimensionResult<CancelMovePlan>> CancelMoveAsync(
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        string? reason,
        bool apply,
        CancellationToken cancellationToken)
    {
        var restoration = apply
            ? await _graph.CancelMoveAsync(structureId, recordId, effectiveFrom, cancellationToken)
            : await _graph.PreviewCancelMoveAsync(structureId, recordId, effectiveFrom, cancellationToken);

        if (!restoration.IsAuthorised)
        {
            return DimensionResult.NotAuthorised<CancelMovePlan>();
        }

        if (!restoration.Succeeded)
        {
            return DimensionResult.Failed<CancelMovePlan>(restoration.Errors);
        }

        // Everyone under the node on the day the restored placement takes hold: their ancestor
        // chain for this window is what changes, whether they sit at this node or below it.
        var employees = await _assignments.GetEmployeesUnderAsync(
            structureId,
            recordId,
            effectiveFrom,
            includeDescendants: true,
            skip: 0,
            take: int.MaxValue,
            cancellationToken);

        var plan = new CancelMovePlan(
            structureId,
            recordId,
            effectiveFrom,
            restoration.Value!.CancelledParentId,
            restoration.Value.RestoredParentId,
            restoration.Value.RestoredUntil,
            [.. employees.Items.Select(row => row.EmployeeId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)]);

        if (apply)
        {
            await RecordMoveCancelledAsync(structureId, recordId, plan, reason!);
        }

        return DimensionResult.Success(plan, [.. restoration.Errors]);
    }

    /// <summary>
    /// Checks the submitted attribute values against the schema the dimension type declares:
    /// nothing it does not know about, nothing missing that it requires, and every value
    /// convertible to the kind it was declared as.
    /// </summary>
    /// <remarks>
    /// Returns errors rather than throwing, and names the attribute in each one, because these
    /// reach the same place every other violation does — inline on the form, before anything is
    /// written.
    /// </remarks>
    private List<DimensionError> ValidateAttributes(
        DimensionTypeDocument type,
        IReadOnlyList<DimensionAttributeValue>? attributes)
    {
        var errors = new List<DimensionError>();
        var submitted = attributes ?? [];

        foreach (var value in submitted)
        {
            var definition = type.AttributeSchema.FirstOrDefault(
                attribute => string.Equals(attribute.Name, value.Name, StringComparison.Ordinal));

            if (definition is null)
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    value.Name,
                    S["The dimension type '{0}' has no attribute called '{1}'.", type.Code, value.Name]));

                continue;
            }

            if (value.IsEmpty)
            {
                continue;
            }

            if (!TryConvert(definition, value, out _))
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    value.Name,
                    S["'{0}' is not a valid value for '{1}'.", value.Value ?? string.Empty, definition.Label.En]));
            }
        }

        foreach (var definition in type.AttributeSchema.Where(attribute => attribute.IsRequired))
        {
            var provided = submitted.FirstOrDefault(
                value => string.Equals(value.Name, definition.Name, StringComparison.Ordinal));

            if (provided is null || provided.IsEmpty)
            {
                errors.Add(new DimensionError(
                    DimensionRule.AttributeSchema,
                    definition.Name,
                    S["'{0}' is required.", definition.Label.En]));
            }
        }

        return errors;
    }

    /// <summary>
    /// Writes the attribute values onto the fields the dimension type declared, which live on a
    /// content part named after the generated content type — see
    /// <c>DimensionTypeService.WriteContentDefinitionAsync</c>.
    /// </summary>
    private async Task WriteAttributesAsync(
        string recordId,
        DimensionTypeDocument type,
        IReadOnlyList<DimensionAttributeValue> attributes,
        CancellationToken cancellationToken)
    {
        var item = await _contentManager.GetAsync(recordId);

        if (item is null)
        {
            return;
        }

        foreach (var value in attributes.Where(attribute => !attribute.IsEmpty))
        {
            var definition = type.AttributeSchema.FirstOrDefault(
                attribute => string.Equals(attribute.Name, value.Name, StringComparison.Ordinal));

            if (definition is null || !TryConvert(definition, value, out var converted))
            {
                continue;
            }

            // Altered on the part by name: the part is the content type's own, declared when the
            // dimension type wrote its definition, so there is no CLR type to name here.
            item.Alter<ContentPart>(type.ContentTypeName, part =>
            {
                switch (converted)
                {
                    case BilingualText bilingual:
                        part.Alter<BilingualTextField>(definition.Name, field => field.Set(bilingual));
                        break;
                    case decimal number:
                        part.Alter<NumericField>(definition.Name, field => field.Value = number);
                        break;
                    case bool boolean:
                        part.Alter<BooleanField>(definition.Name, field => field.Value = boolean);
                        break;
                    case DateTime date:
                        part.Alter<DateField>(definition.Name, field => field.Value = date);
                        break;
                    default:
                        part.Alter<TextField>(definition.Name, field => field.Text = (string)converted!);
                        break;
                }
            });
        }

        await _contentManager.UpdateAsync(item);
        await _session.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Turns the text that came off a form into the value the declared field kind holds, or says
    /// it cannot. Dates and numbers are read invariantly for the same reason the designer's "as
    /// at" is: the wire format does not change with the culture of whoever typed it.
    /// </summary>
    private static bool TryConvert(
        DimensionAttributeDefinition definition,
        DimensionAttributeValue value,
        out object? converted)
    {
        var text = value.Value?.Trim() ?? string.Empty;

        switch (definition.Kind)
        {
            case DimensionAttributeKind.BilingualText:
                converted = new BilingualText(text, value.ValueAr?.Trim() ?? string.Empty);
                return true;

            case DimensionAttributeKind.Number:
                var isNumber = decimal.TryParse(
                    text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number);
                converted = isNumber ? number : null;
                return isNumber;

            case DimensionAttributeKind.Boolean:
                // An unchecked checkbox posts nothing at all, so anything that arrives and is not
                // a recognised false is true; a blank never reaches here.
                converted = !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(text, "0", StringComparison.Ordinal);
                return true;

            case DimensionAttributeKind.Date:
                var isDate = IsoDate.TryParse(text, out var date);
                converted = isDate ? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null;
                return isDate;

            default:
                converted = text;
                return true;
        }
    }

    private Task RecordRecordChangeAsync(DimensionRecordAuditEvent payload) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<DimensionRecordAuditEvent>(
            DimensionAuditTrail.DimensionRecordChanged,
            DimensionAuditTrail.Category,
            payload.RecordId,
            userId: null,
            userName: null,
            payload));

    private Task RecordMoveCancelledAsync(
        string structureId,
        string recordId,
        CancelMovePlan plan,
        string reason) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<MoveCancelledAuditEvent>(
            DimensionAuditTrail.MoveCancelled,
            DimensionAuditTrail.Category,
            recordId,
            userId: null,
            userName: null,
            new MoveCancelledAuditEvent
            {
                StructureId = structureId,
                RecordId = recordId,
                EffectiveFrom = plan.EffectiveFrom,
                CancelledParentId = plan.CancelledParentId,
                RestoredParentId = plan.RestoredParentId,
                RestoredUntil = plan.RestoredUntil,
                Reason = reason,
            }));

    public Task<DimensionResult<MergePlan>> PlanMergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        MergeAsync(structureId, sourceRecordId, targetRecordId, effectiveFrom, apply: false, cancellationToken);

    public Task<DimensionResult<MergePlan>> MergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default) =>
        MergeAsync(structureId, sourceRecordId, targetRecordId, effectiveFrom, apply: true, cancellationToken);

    /// <summary>
    /// The one implementation behind both the dry run and the merge.
    /// </summary>
    /// <remarks>
    /// Deliberately one method with a flag rather than two that look alike. Architecture
    /// section 6 requires the dry run to report "exactly what will change", and two
    /// implementations would drift apart the first time one of them was fixed — at which point
    /// the report a human signed off would stop describing what actually happened, silently.
    /// Here the plan is computed once; <paramref name="apply"/> only decides whether the writes
    /// that follow it happen.
    /// </remarks>
    private async Task<DimensionResult<MergePlan>> MergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.MergeDimensionRecords))
        {
            return DimensionResult.NotAuthorised<MergePlan>();
        }

        var mergeErrors = await _validator.ValidateMergeAsync(
            structureId, sourceRecordId, targetRecordId, effectiveFrom, cancellationToken);

        if (mergeErrors.Count > 0)
        {
            return DimensionResult.Failed<MergePlan>(mergeErrors);
        }

        var children = await _graph.GetChildrenAsync(structureId, sourceRecordId, effectiveFrom, cancellationToken);

        var employees = await _assignments.GetEmployeesUnderAsync(
            structureId,
            sourceRecordId,
            effectiveFrom,
            includeDescendants: false,
            skip: 0,
            take: int.MaxValue,
            cancellationToken);

        var plan = new MergePlan(
            structureId,
            sourceRecordId,
            targetRecordId,
            effectiveFrom,
            [.. children.Select(child => child.RecordId).OrderBy(id => id, StringComparer.Ordinal)],
            [.. employees.Items.Select(row => row.EmployeeId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)],
            SourceRetired: true);

        if (!apply)
        {
            return DimensionResult.Success(plan);
        }

        foreach (var childId in plan.ChildrenReparented)
        {
            var moved = await _graph.MoveAsync(structureId, childId, targetRecordId, effectiveFrom, cancellationToken);

            if (!moved.Succeeded)
            {
                return DimensionResult.Failed<MergePlan>(moved.Errors);
            }
        }

        foreach (var employeeId in plan.EmployeesReassigned)
        {
            var placed = await _assignments.PlaceAsync(
                employeeId,
                structureId,
                targetRecordId,
                effectiveFrom,
                cancellationToken: cancellationToken);

            if (!placed.Succeeded)
            {
                return DimensionResult.Failed<MergePlan>(placed.Errors);
            }
        }

        // Closed, not assessed: everything that referenced the source has just been reassigned to
        // the target, and what still points at it is the prior-period history the merge is
        // required to leave resolving through it. See CloseRecordAsync.
        var retired = await CloseRecordAsync(sourceRecordId, effectiveFrom, cancellationToken);

        return retired.Succeeded
            ? DimensionResult.Success(plan)
            : DimensionResult.Failed<MergePlan>(retired.Errors);
    }

    public async Task<IReadOnlyList<DimensionAttributeValue>> GetAttributeValuesAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = await _contentManager.GetAsync(recordId);
        var part = item?.Get<DimensionRecordPart>(nameof(DimensionRecordPart));

        if (item is null || part is null)
        {
            return [];
        }

        var type = await _dimensionTypeService.GetAsync(part.DimensionTypeId, asAt: null, cancellationToken);
        var own = type is null ? null : item.Get<ContentPart>(type.ContentTypeName);

        if (type is null || own is null)
        {
            return [];
        }

        var values = new List<DimensionAttributeValue>();

        // Driven by the schema rather than by what happens to be on the item: a field left over
        // from an attribute that has since been removed from the type is not part of the record's
        // value any more, and an export that carried it would reintroduce it on import.
        foreach (var definition in type.AttributeSchema)
        {
            if (Read(own, definition) is { } value && !value.IsEmpty)
            {
                values.Add(value);
            }
        }

        return values;
    }

    public async Task<DimensionResult<DimensionNodeRef>> SetAttributeValuesAsync(
        string recordId,
        IReadOnlyList<DimensionAttributeValue> attributes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        var item = await _contentManager.GetAsync(recordId);
        var part = item?.Get<DimensionRecordPart>(nameof(DimensionRecordPart));

        if (item is null || part is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(UnknownRecord(recordId));
        }

        var type = await _dimensionTypeService.GetAsync(part.DimensionTypeId, asAt: null, cancellationToken);

        if (type is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(new DimensionError(
                DimensionRule.UnknownReference,
                part.DimensionTypeId,
                S["There is no dimension type with the id '{0}' in this tenant.", part.DimensionTypeId]));
        }

        // Validated before anything is written, and against the same rules the Add unit form uses:
        // an attribute the type does not declare, a missing required one, a value that is not of
        // its declared kind. The same errors, reaching the caller the same way.
        var errors = ValidateAttributes(type, attributes);

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<DimensionNodeRef>(errors);
        }

        await WriteAttributesAsync(recordId, type, attributes, cancellationToken);

        return DimensionResult.Success(ToNodeRef(
            recordId,
            part.Code,
            new BilingualText(part.NameEn, part.NameAr),
            part.DimensionTypeId,
            new EffectiveRange(part.EffectiveFrom, part.EffectiveTo),
            part.SortOrder));
    }

    /// <summary>
    /// Reads one attribute back off the record's own part, as the text an export or a form carries.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="TryConvert"/>, and it has to stay that way: a value written by one
    /// and read by the other has to survive the round trip unchanged, which is what makes an export
    /// faithful. Numbers and dates are written invariantly for the same reason they are parsed
    /// invariantly — the wire format does not change with the culture of whoever typed it.
    /// </remarks>
    private static DimensionAttributeValue? Read(ContentPart own, DimensionAttributeDefinition definition) =>
        definition.Kind switch
        {
            DimensionAttributeKind.BilingualText =>
                own.Get<BilingualTextField>(definition.Name) is { } bilingual
                    ? new DimensionAttributeValue(definition.Name, bilingual.En, bilingual.Ar)
                    : null,

            DimensionAttributeKind.Number =>
                own.Get<NumericField>(definition.Name)?.Value is { } number
                    ? new DimensionAttributeValue(
                        definition.Name, number.ToString(CultureInfo.InvariantCulture))
                    : null,

            DimensionAttributeKind.Boolean =>
                own.Get<BooleanField>(definition.Name)?.Value is { } boolean
                    ? new DimensionAttributeValue(definition.Name, boolean ? "true" : "false")
                    : null,

            DimensionAttributeKind.Date =>
                own.Get<DateField>(definition.Name)?.Value is { } date
                    ? new DimensionAttributeValue(
                        definition.Name, DateOnly.FromDateTime(date).ToIso())
                    : null,

            _ => own.Get<TextField>(definition.Name) is { } text
                ? new DimensionAttributeValue(definition.Name, text.Text)
                : null,
        };

    public async Task<IReadOnlyList<DimensionNodeRef>> ListByTypeAsync(
        string dimensionTypeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _authorisation.AuthoriseAsync(Permissions.ViewDimensionHistory) &&
            !await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return [];
        }

        // Latest only, and undated: one row per record whatever its effective range, which is what
        // an export needs. Filtering to today here would silently drop every retired unit, and a
        // retired unit is what every historical report resolves through.
        var rows = await _session
            .QueryIndex<DimensionRecordPartIndex>(index =>
                index.DimensionTypeId == dimensionTypeId && index.Latest)
            .ListAsync(cancellationToken);

        return
        [
            .. rows
                .Select(row => ToNodeRef(
                    row.ContentItemId,
                    row.Code,
                    new BilingualText(row.NameEn, row.NameAr),
                    row.DimensionTypeId,
                    new EffectiveRange(
                        EffectiveDates.FromColumn(row.EffectiveFrom),
                        EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive)),
                    row.SortOrder))
                .OrderBy(record => record.Code, StringComparer.Ordinal),
        ];
    }

    public async Task<IReadOnlyList<DimensionNamePeriod>> GetNameHistoryAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No document means no rename has ever happened: the record has had one name since it
        // opened, and the record itself already carries it. An export reads that as "nothing to
        // say about this record's history" rather than as a missing period.
        var document = await LoadNameHistoryAsync(recordId, cancellationToken);

        return document is null
            ? []
            : [.. document.Periods.OrderBy(period => period.Range.From)];
    }

    public async Task<DimensionNodeRef?> GetAsync(
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var node = await _recordLookup.GetAsync(recordId, cancellationToken);

        return await VisibleAsync(node, asAt);
    }

    public async Task<DimensionNodeRef?> GetByCodeAsync(
        string code,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var node = await _recordLookup.GetByCodeAsync(code, cancellationToken);

        return await VisibleAsync(node, asAt);
    }

    public async Task<BilingualText?> GetNameAsync(
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var history = await LoadNameHistoryAsync(recordId, cancellationToken);

        return history?.NameOn(date);
    }

    // ---- names ------------------------------------------------------------------------

    private async Task<DimensionResult<DimensionNodeRef>> ChangeNameAsync(
        string recordId,
        BilingualText name,
        Func<IReadOnlyList<DimensionNamePeriod>, IReadOnlyList<DimensionNamePeriod>?> change,
        Func<IReadOnlyList<DimensionNamePeriod>, bool> updatesCurrentName,
        LocalizedString failureMessage,
        DimensionRecordOperation operation,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageDimensionRecords))
        {
            return DimensionResult.NotAuthorised<DimensionNodeRef>();
        }

        var item = await _contentManager.GetAsync(recordId);

        if (item is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(UnknownRecord(recordId));
        }

        var document = await LoadNameHistoryAsync(recordId, cancellationToken)
            ?? new DimensionNameDocument
            {
                RecordId = recordId,
                Periods = DimensionNameHistory.Start(
                    new BilingualText(
                        item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!.NameEn,
                        item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!.NameAr),
                    item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!.EffectiveFrom),
            };

        // Captured before the change, because the audit entry's job is to say what it was as well
        // as what it became, and the period being altered may not be the current one.
        var nameBefore = document.NameOn(effectiveFrom)
            ?? document.CurrentName
            ?? new BilingualText(
                item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!.NameEn,
                item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!.NameAr);

        var wasCurrent = updatesCurrentName(document.Periods);
        var changed = change(document.Periods);

        if (changed is null)
        {
            return DimensionResult.Failed<DimensionNodeRef>(new DimensionError(
                DimensionRule.UnknownReference, recordId, failureMessage));
        }

        document.Periods = changed;

        await _session.SaveCheckedAsync(document, cancellationToken);

        // The part carries the current name so that the editor, the generated title and every
        // picker work without a join. It only moves when the period that changed is the one in
        // effect now.
        if (wasCurrent)
        {
            item.Alter<DimensionRecordPart>(part =>
            {
                part.NameEn = name.En;
                part.NameAr = name.Ar;
            });

            await _contentManager.UpdateAsync(item);
        }

        var recordPart = item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!;

        await RecordRecordChangeAsync(new DimensionRecordAuditEvent
        {
            RecordId = recordId,
            Code = recordPart.Code,
            Operation = operation,
            EffectiveFrom = effectiveFrom,
            Before = DimensionRecordNameState.Of(nameBefore),
            After = DimensionRecordNameState.Of(name),
        });

        return DimensionResult.Success((await GetAsync(recordId, null, cancellationToken))!);
    }

    private async Task StartNameHistoryAsync(
        string recordId,
        BilingualText name,
        DateOnly from,
        CancellationToken cancellationToken)
    {
        var document = new DimensionNameDocument
        {
            RecordId = recordId,
            Periods = DimensionNameHistory.Start(name, from),
        };

        await _session.SaveCheckedAsync(document, cancellationToken);
    }

    private async Task<DimensionNameDocument?> LoadNameHistoryAsync(
        string recordId,
        CancellationToken cancellationToken)
    {
        var documents = await _session
            .Query<DimensionNameDocument, DimensionNameIndex>(index => index.RecordId == recordId)
            .ListAsync(cancellationToken);

        return documents.FirstOrDefault(document =>
            string.Equals(document.RecordId, recordId, StringComparison.Ordinal));
    }

    // ---- helpers ----------------------------------------------------------------------

    private async Task<DimensionNodeRef?> VisibleAsync(DimensionNodeRef? node, DateOnly? asAt)
    {
        if (node is null)
        {
            return null;
        }

        var date = asAt ?? await _authorisation.TodayAsync();

        return node.EffectiveRange.Contains(date) ? node : null;
    }

    private static DimensionNodeRef ToNodeRef(
        string recordId,
        string code,
        BilingualText name,
        string dimensionTypeId,
        EffectiveRange range,
        int sortOrder) =>
        new(recordId, code, name.En, name.Ar, dimensionTypeId, range, IsActive: true, sortOrder);

    private DimensionError UnknownRecord(string recordId) => new(
        DimensionRule.UnknownReference,
        recordId,
        S["There is no dimension record with the id '{0}' in this tenant.", recordId]);
}
