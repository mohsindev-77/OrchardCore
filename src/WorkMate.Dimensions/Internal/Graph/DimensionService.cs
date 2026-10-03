using Microsoft.Extensions.Localization;
using OrchardCore.ContentManagement;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using YesSql;

namespace WorkMate.Dimensions.Internal.Graph;

/// <inheritdoc />
internal sealed class DimensionService : IDimensionService
{
    private readonly ISession _session;
    private readonly IContentManager _contentManager;
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IDimensionGraphService _graph;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IStringLocalizer S;

    public DimensionService(
        ISession session,
        IContentManager contentManager,
        IDimensionTypeService dimensionTypeService,
        IDimensionGraphService graph,
        IEmployeeAssignmentService assignments,
        IDimensionAuthorisation authorisation,
        IStringLocalizer<DimensionService> stringLocalizer)
    {
        _session = session;
        _contentManager = contentManager;
        _dimensionTypeService = dimensionTypeService;
        _graph = graph;
        _assignments = assignments;
        _authorisation = authorisation;
        S = stringLocalizer;
    }

    public async Task<DimensionResult<DimensionNodeRef>> CreateAsync(
        string dimensionTypeId,
        string code,
        BilingualText name,
        EffectiveRange effectiveRange,
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

        var item = await _contentManager.NewAsync(type.ContentTypeName);

        item.Alter<DimensionRecordPart>(part =>
        {
            part.Code = code;
            part.NameEn = name.En;
            part.NameAr = name.Ar;
            part.DimensionTypeId = dimensionTypeId;
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

        return DimensionResult.Success(ToNodeRef(item.ContentItemId, code, name, dimensionTypeId, effectiveRange));
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
            cancellationToken);

    public async Task<DimensionResult<DimensionNodeRef>> RetireAsync(
        string recordId,
        DateOnly effectiveDate,
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

        if (string.Equals(sourceRecordId, targetRecordId, StringComparison.Ordinal))
        {
            return DimensionResult.Failed<MergePlan>(new DimensionError(
                DimensionRule.MergeTarget,
                sourceRecordId,
                S["A record cannot be merged into itself."]));
        }

        if (await _graph.IsUnderAsync(structureId, targetRecordId, sourceRecordId, effectiveFrom, cancellationToken))
        {
            // Folding a unit into something inside it would leave the target with no parent
            // chain once the source retires.
            return DimensionResult.Failed<MergePlan>(new DimensionError(
                DimensionRule.MergeTarget,
                targetRecordId,
                S["'{0}' is inside '{1}', so it cannot be the target of merging '{1}' into it.",
                    targetRecordId,
                    sourceRecordId]));
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

        var retired = await RetireAsync(sourceRecordId, effectiveFrom, cancellationToken);

        return retired.Succeeded
            ? DimensionResult.Success(plan)
            : DimensionResult.Failed<MergePlan>(retired.Errors);
    }

    public async Task<DimensionNodeRef?> GetAsync(
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var row = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.ContentItemId == recordId && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return await VisibleAsync(row, asAt);
    }

    public async Task<DimensionNodeRef?> GetByCodeAsync(
        string code,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var row = await _session
            .QueryIndex<DimensionRecordPartIndex>(index => index.Code == code && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return await VisibleAsync(row, asAt);
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

    private async Task<DimensionNodeRef?> VisibleAsync(DimensionRecordPartIndex? row, DateOnly? asAt)
    {
        if (row is null)
        {
            return null;
        }

        var range = new EffectiveRange(
            EffectiveDates.FromColumn(row.EffectiveFrom),
            EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive));

        var date = asAt ?? await _authorisation.TodayAsync();

        return range.Contains(date)
            ? new DimensionNodeRef(
                row.ContentItemId, row.Code, row.NameEn, row.NameAr, row.DimensionTypeId,
                range, row.IsActive, row.SortOrder)
            : null;
    }

    private static DimensionNodeRef ToNodeRef(
        string recordId,
        string code,
        BilingualText name,
        string dimensionTypeId,
        EffectiveRange range) =>
        new(recordId, code, name.En, name.Ar, dimensionTypeId, range, IsActive: true, SortOrder: 0);

    private DimensionError UnknownRecord(string recordId) => new(
        DimensionRule.UnknownReference,
        recordId,
        S["There is no dimension record with the id '{0}' in this tenant.", recordId]);
}
