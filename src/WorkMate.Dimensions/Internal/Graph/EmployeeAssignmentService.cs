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
            // The advisories are carried through, not swallowed: a single placement at a unit with
            // children warns for exactly the same reason a split one does.
            ? DimensionResult.Success(result.Value![0], [.. result.Errors])
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

        // Blocking and advisory are separated here, not counted together. Placing somebody at a
        // unit that has units under it is advisory — a departmental secretary is an ordinary shape —
        // so a count of all errors would refuse it, and would do so in the way that is hardest to
        // see: DimensionResult.Succeeded ignores advisories, so a failed result carrying only one
        // reads as a success with no value, and the caller dereferences null.
        var blocking = errors.Where(error => !error.IsAdvisory).ToList();

        if (blocking.Count > 0)
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

        // The advisories travel with the success, rather than being dropped. A caller that does not
        // surface them has made a choice; one that never received them could not.
        return DimensionResult.Success<IReadOnlyList<EmployeeAssignment>>(
            [.. added.Select(row => ToAssignment(employeeId, structureId, row))],
            [.. errors]);
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

    public async Task<IReadOnlyDictionary<string, int>> CountEmployeesAtAsync(
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

        var date = EffectiveDates.ToColumn(asAt ?? await _authorisation.TodayAsync());
        var ids = recordIds.Distinct(StringComparer.Ordinal).ToArray();

        var rows = await _session
            .QueryIndex<EmployeeAssignmentIndex>(index =>
                index.StructureId == structureId &&
                index.NodeId.IsIn(ids) &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        // Counted by distinct employee, not by row: a split allocation is several rows for one
        // person at one node, and a card saying "3 employees" for one person in three slices
        // would be wrong in the way nobody checks.
        return rows
            .GroupBy(row => row.NodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.EmployeeId).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal);
    }

    // ---- unit heads -------------------------------------------------------------------

    public async Task<DimensionResult<HeadAppointment>> SetHeadAsync(
        string structureId,
        string recordId,
        string employeeId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return DimensionResult.NotAuthorised<HeadAppointment>();
        }

        var document = await LoadHeadsAsync(structureId, recordId, cancellationToken);
        var standing = document?.TermOn(effectiveFrom);

        // Already in post from a date no later than this one: nothing to do, and saying so rather
        // than writing an identical term is what makes a recipe re-run a no-op instead of a second
        // term abutting the first.
        if (standing is not null &&
            string.Equals(standing.EmployeeId, employeeId, StringComparison.Ordinal) &&
            standing.Range.From <= effectiveFrom)
        {
            return DimensionResult.Success(ToAppointment(structureId, recordId, standing));
        }

        var errors = await _validator.ValidateHeadAppointmentAsync(
            structureId, recordId, employeeId, new EffectiveRange(effectiveFrom, null), cancellationToken);

        if (errors.Count > 0)
        {
            return DimensionResult.Failed<HeadAppointment>(errors);
        }

        document ??= new HeadAppointmentDocument { StructureId = structureId, RecordId = recordId };

        var lastDayOfTheOld = effectiveFrom.AddDays(-1);

        // The same shape as a placement displacing a placement: everything running when the new
        // term starts is closed the day before, and anything that started on or after the new date
        // is replaced outright, because an appointment states the position from that date onwards.
        var kept = document.Terms
            .Where(term => term.Range.From < effectiveFrom)
            .Select(term => term.Range.To is null || term.Range.To > lastDayOfTheOld
                ? term with { Range = term.Range.EndingOn(lastDayOfTheOld) }
                : term)
            .ToList();

        var appointed = new HeadTerm(employeeId, new EffectiveRange(effectiveFrom, null));

        document.Terms = [.. kept, appointed];

        await _session.SaveCheckedAsync(document, cancellationToken);

        return DimensionResult.Success(ToAppointment(structureId, recordId, appointed));
    }

    public async Task<DimensionResult<int>> ClearHeadAsync(
        string structureId,
        string recordId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return DimensionResult.NotAuthorised<int>();
        }

        var document = await LoadHeadsAsync(structureId, recordId, cancellationToken);

        if (document is null)
        {
            return DimensionResult.Success(0);
        }

        var closed = CloseTerms(document, lastDay);

        if (closed > 0)
        {
            await _session.SaveCheckedAsync(document, cancellationToken);
        }

        return DimensionResult.Success(closed);
    }

    public async Task<HeadAppointment?> GetHeadAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = asAt ?? await _authorisation.TodayAsync();
        var document = await LoadHeadsAsync(structureId, recordId, cancellationToken);
        var term = document?.TermOn(date);

        return term is null ? null : ToAppointment(structureId, recordId, term);
    }

    public async Task<IReadOnlyDictionary<string, HeadAppointment>> GetHeadsAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordIds);

        if (recordIds.Count == 0)
        {
            return new Dictionary<string, HeadAppointment>(StringComparer.Ordinal);
        }

        var date = EffectiveDates.ToColumn(asAt ?? await _authorisation.TodayAsync());
        var ids = recordIds.Distinct(StringComparer.Ordinal).ToArray();

        var rows = await _session
            .QueryIndex<UnitHeadIndex>(index =>
                index.StructureId == structureId &&
                index.NodeId.IsIn(ids) &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        // At most one row per node is the rule the validator enforces, so a second one is a defect
        // rather than a case to merge. ToDictionary would throw on it, which is the right failure:
        // a card silently showing one of two heads is how a unit comes to have two.
        return rows.ToDictionary(row => row.NodeId, ToAppointment, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<HeadAppointment>> GetHeadshipsOfAsync(
        string employeeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default)
    {
        var date = EffectiveDates.ToColumn(asAt ?? await _authorisation.TodayAsync());

        var rows = await _session
            .QueryIndex<UnitHeadIndex>(index =>
                index.EmployeeId == employeeId &&
                index.EffectiveFrom <= date &&
                date <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        return [.. rows.Select(ToAppointment)];
    }

    public async Task<DimensionResult<int>> EndHeadshipsOfAsync(
        string employeeId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.AssignEmployees))
        {
            return DimensionResult.NotAuthorised<int>();
        }

        // Every term that is still running on the last day, across every axis — not only the ones
        // effective today. A leaver whose last day is in the past still has to stop heading things
        // from that day, and one whose appointment starts after it never began.
        var lastDayColumn = EffectiveDates.ToInclusiveEndColumn(lastDay);

        var rows = await _session
            .QueryIndex<UnitHeadIndex>(index =>
                index.EmployeeId == employeeId &&
                lastDayColumn <= index.EffectiveToInclusive)
            .ListAsync(cancellationToken);

        var closed = 0;

        foreach (var (structureId, recordId) in rows
            .Select(row => (row.StructureId, row.NodeId))
            .Distinct())
        {
            var document = await LoadHeadsAsync(structureId, recordId, cancellationToken);

            if (document is null)
            {
                continue;
            }

            var closedHere = CloseTerms(document, lastDay, employeeId);

            if (closedHere > 0)
            {
                closed += closedHere;
                await _session.SaveCheckedAsync(document, cancellationToken);
            }
        }

        return DimensionResult.Success(closed);
    }

    public async Task<IReadOnlyList<HeadAppointment>> GetHeadHistoryAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadHeadsAsync(structureId, recordId, cancellationToken);

        return document is null
            ? []
            : [.. document.Terms
                .OrderBy(term => term.Range.From)
                .Select(term => ToAppointment(structureId, recordId, term))];
    }

    /// <summary>
    /// Closes every term still running after <paramref name="lastDay"/>, optionally only one
    /// employee's, and returns how many were closed.
    /// </summary>
    /// <remarks>
    /// Terms that start after the last day are dropped rather than closed: a term beginning after
    /// the head stopped being eligible never ran at all, and keeping it as an empty range would
    /// leave a row that resolves to nothing and reads like a bug on every history screen.
    /// </remarks>
    private static int CloseTerms(HeadAppointmentDocument document, DateOnly lastDay, string? employeeId = null)
    {
        var closed = 0;
        var terms = new List<HeadTerm>();

        foreach (var term in document.Terms)
        {
            var mine = employeeId is null ||
                string.Equals(term.EmployeeId, employeeId, StringComparison.Ordinal);

            if (!mine || (term.Range.To is not null && term.Range.To <= lastDay))
            {
                terms.Add(term);
                continue;
            }

            if (term.Range.From > lastDay)
            {
                closed++;
                continue;
            }

            terms.Add(term with { Range = term.Range.EndingOn(lastDay) });
            closed++;
        }

        document.Terms = terms;

        return closed;
    }

    private async Task<HeadAppointmentDocument?> LoadHeadsAsync(
        string structureId,
        string recordId,
        CancellationToken cancellationToken)
    {
        var documents = await _session
            .Query<HeadAppointmentDocument, UnitHeadIndex>(index =>
                index.StructureId == structureId && index.NodeId == recordId)
            .ListAsync(cancellationToken);

        return documents.FirstOrDefault(document =>
            string.Equals(document.StructureId, structureId, StringComparison.Ordinal) &&
            string.Equals(document.RecordId, recordId, StringComparison.Ordinal));
    }

    private static HeadAppointment ToAppointment(string structureId, string recordId, HeadTerm term) =>
        new(structureId, recordId, term.EmployeeId, term.Range);

    private static HeadAppointment ToAppointment(UnitHeadIndex row) => new(
        row.StructureId,
        row.NodeId,
        row.EmployeeId,
        new EffectiveRange(
            EffectiveDates.FromColumn(row.EffectiveFrom),
            EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive)));

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
