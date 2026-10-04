using Microsoft.Extensions.Localization;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
using YesSql;
using YesSql.Services;

namespace WorkMate.Dimensions.Internal.Graph;

/// <inheritdoc />
internal sealed class EmployeeAssignmentService : IEmployeeAssignmentService
{
    private readonly ISession _session;
    private readonly IDimensionAuthorisation _authorisation;
    private readonly IDimensionValidator _validator;
    private readonly IStringLocalizer S;

    public EmployeeAssignmentService(
        ISession session,
        IDimensionAuthorisation authorisation,
        IDimensionValidator validator,
        IStringLocalizer<EmployeeAssignmentService> stringLocalizer)
    {
        _session = session;
        _authorisation = authorisation;
        _validator = validator;
        S = stringLocalizer;
    }

    public async Task<DimensionResult<EmployeeAssignment>> PlaceAsync(
        string employeeId,
        string structureId,
        string recordId,
        DateOnly effectiveFrom,
        decimal allocationPercent = 100m,
        bool isPrimary = true,
        CancellationToken cancellationToken = default)
    {
        var result = await ReallocateAsync(
            employeeId,
            structureId,
            [new AssignmentSplitEntry(recordId, allocationPercent, isPrimary)],
            effectiveFrom,
            cancellationToken);

        return result.Succeeded
            ? DimensionResult.Success(result.Value![0])
            : result.IsAuthorised
                ? DimensionResult.Failed<EmployeeAssignment>(result.Errors)
                : DimensionResult.NotAuthorised<EmployeeAssignment>();
    }

    public async Task<DimensionResult<int>> EndAsync(
        string employeeId,
        string structureId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return DimensionResult.NotAuthorised<int>();
        }

        var document = await LoadAsync(employeeId, structureId, cancellationToken);

        if (document is null)
        {
            return DimensionResult.Success(0);
        }

        var ended = 0;

        document.Rows =
        [
            .. document.Rows
                .Where(row => row.Range.From <= lastDay)
                .Select(row =>
                {
                    if (row.Range.To is not null && row.Range.To <= lastDay)
                    {
                        return row;
                    }

                    ended++;

                    return row with { Range = row.Range.EndingOn(lastDay) };
                }),
        ];

        await _session.SaveCheckedAsync(document, cancellationToken);

        return DimensionResult.Success(ended);
    }

    public async Task<DimensionResult<IReadOnlyList<EmployeeAssignment>>> ReallocateAsync(
        string employeeId,
        string structureId,
        IReadOnlyList<AssignmentSplitEntry> split,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(split);

        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return DimensionResult.NotAuthorised<IReadOnlyList<EmployeeAssignment>>();
        }

        var errors = await _validator.ValidateAssignmentAsync(
            employeeId, structureId, split, effectiveFrom, cancellationToken);

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<IReadOnlyList<EmployeeAssignment>>(errors);
        }

        var document = await LoadAsync(employeeId, structureId, cancellationToken)
            ?? new EmployeeAssignmentDocument { EmployeeId = employeeId, StructureId = structureId };

        // Everything that was running when the new split starts is closed the day before. This
        // is the mid-month transfer from architecture section 5: the old row runs to the 15th
        // and the new one from the 16th, with no overlap and no gap.
        var lastDayOfTheOld = effectiveFrom.AddDays(-1);

        var kept = document.Rows
            .Where(row => row.Range.From < effectiveFrom)
            .Select(row => row.Range.To is null || row.Range.To > lastDayOfTheOld
                ? row with { Range = row.Range.EndingOn(lastDayOfTheOld) }
                : row)
            .ToList();

        // Rows that started on or after the effective date are replaced outright: a reallocation
        // states the whole picture from that date onwards.
        var added = split
            .Select(entry => new AssignmentRow(
                entry.RecordId,
                new EffectiveRange(effectiveFrom, null),
                entry.AllocationPercent,
                entry.IsPrimary))
            .ToList();

        document.Rows = [.. kept, .. added];

        await _session.SaveCheckedAsync(document, cancellationToken);

        return DimensionResult.Success<IReadOnlyList<EmployeeAssignment>>(
            [.. added.Select(row => ToAssignment(employeeId, structureId, row))]);
    }

    public async Task<EmployeeAssignment?> GetEffectiveAsync(
        string employeeId,
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var document = await LoadAsync(employeeId, structureId, cancellationToken);

        var row = document?.PrimaryOn(date);

        return row is null ? null : ToAssignment(employeeId, structureId, row);
    }

    public async Task<IReadOnlyList<EmployeeAssignment>> GetAllEffectiveAsync(
        string employeeId,
        string structureId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var document = await LoadAsync(employeeId, structureId, cancellationToken);

        return document is null
            ? []
            : [.. document.RowsOn(date).Select(row => ToAssignment(employeeId, structureId, row))];
    }

    public async Task<Page<EmployeeAssignment>> GetEmployeesUnderAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        bool includeDescendants = true,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(asAt ?? await _authorisation.TodayAsync());

        // The sub-select is the whole point.
        //
        // The obvious implementation resolves the descendants first and passes their ids as an
        // IN list. At the 5,000-record scale the acceptance criterion names, that list exceeds
        // SQL Server's limit of 2,100 parameters per command, and the query starts throwing on
        // a customer's database while still passing on the SQLite the tests run against. A
        // correlated sub-select against the closure index has no parameter cost at all, and it
        // is one statement rather than two round trips.
        //
        // YesSql.Services.DefaultQueryExtensionsIndex.IsIn is what makes it expressible without
        // raw SQL, which rule 2 of the specification forbids outside a migration.
        var query = includeDescendants
            ? _session.QueryIndex<EmployeeAssignmentIndex>(index =>
                index.StructureId == structureId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive &&
                index.NodeId.IsIn<DimensionClosureIndex>(
                    closure => closure.DescendantId,
                    closure =>
                        closure.StructureId == structureId &&
                        closure.AncestorId == recordId &&
                        closure.EffectiveFrom <= date &&
                        date <= closure.EffectiveToInclusive))
            : _session.QueryIndex<EmployeeAssignmentIndex>(index =>
                index.StructureId == structureId &&
                index.NodeId == recordId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(index => index.EmployeeId)
            .Skip(skip)
            .Take(take)
            .ListAsync(cancellationToken);

        return new Page<EmployeeAssignment>([.. rows.Select(ToAssignment)], total, skip, take);
    }

    public async Task<IReadOnlyList<EmployeeAssignment>> GetAllAxesAsync(
        string employeeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(asAt ?? await _authorisation.TodayAsync());

        var rows = await _session
            .QueryIndex<EmployeeAssignmentIndex>(index =>
                index.EmployeeId == employeeId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        return [.. rows.Select(ToAssignment)];
    }

    private async Task<EmployeeAssignmentDocument?> LoadAsync(
        string employeeId,
        string structureId,
        CancellationToken cancellationToken)
    {
        var documents = await _session
            .Query<EmployeeAssignmentDocument, EmployeeAssignmentIndex>(index =>
                index.EmployeeId == employeeId && index.StructureId == structureId)
            .ListAsync(cancellationToken);

        return documents.FirstOrDefault(document =>
            string.Equals(document.EmployeeId, employeeId, StringComparison.Ordinal) &&
            string.Equals(document.StructureId, structureId, StringComparison.Ordinal));
    }

    private static EmployeeAssignment ToAssignment(string employeeId, string structureId, AssignmentRow row) =>
        new(employeeId, structureId, row.RecordId, row.Range, row.AllocationPercent, row.IsPrimary);

    private static EmployeeAssignment ToAssignment(EmployeeAssignmentIndex row) => new(
        row.EmployeeId,
        row.StructureId,
        row.NodeId,
        new EffectiveRange(
            EffectiveDates.FromColumn(row.EffectiveFrom),
            EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive)),
        row.AllocationPercent,
        row.IsPrimary);
}
