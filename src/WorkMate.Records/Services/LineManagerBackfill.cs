using WorkMate.Dimensions.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using YesSql;

namespace WorkMate.Records.Services;

/// <inheritdoc />
/// <remarks>
/// <b>It derives the answer as if nothing were stored, which is the whole trick.</b>
/// <see cref="IEmployeeOrganisationService.GetLineManagerAsync"/> cannot be used to decide this: it
/// reads the stored value first, so the sole holder of the post above comes back as
/// <see cref="LineManagerHow.Chosen"/> rather than <see cref="LineManagerHow.Derived"/> — the stored
/// person is a holder, and from the outside a frozen derivation and a deliberate pick of the only
/// candidate are the same two bytes. Asking that read what to clear would therefore clear nothing,
/// which is exactly the population this exists for. So the holders are read straight off the
/// position and compared to what is stored.
///
/// <b>Chosen and Override are told apart by the holders, not by a stored kind.</b> Data written
/// before this addendum has no kind — <c>EmployeePart.LineManagerKind</c> did not exist — so for
/// those records the only evidence is whether the stored person holds the post above. Among the
/// holders is a pick; off the list entirely is a line somebody drew by hand. Records written since
/// carry their kind and are taken at their word, which is what keeps an override that happens to
/// name the sole holder from being mistaken for a derivation and cleared.
/// </remarks>
internal sealed class LineManagerBackfill : ILineManagerBackfill
{
    private readonly ISession _session;
    private readonly IEmployeeOrganisationService _organisation;
    private readonly IEmployeeService _employees;

    public LineManagerBackfill(
        ISession session,
        IEmployeeOrganisationService organisation,
        IEmployeeService employees)
    {
        _session = session;
        _organisation = organisation;
        _employees = employees;
    }

    public async Task<LineManagerBackfillCounts> RunAsync(CancellationToken cancellationToken = default)
    {
        // Only the employees who have a stored line manager at all. The index answers that without
        // loading a content item, so a tenant where nobody has one does no work.
        var withAManager = (await _session
                .QueryIndex<EmployeeIndex>(index =>
                    index.Latest && index.LineManagerEmployeeId != string.Empty)
                .ListAsync(cancellationToken))
            .Select(row => row.ContentItemId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (withAManager.Count == 0)
        {
            return LineManagerBackfillCounts.Nothing;
        }

        // Reads default to today; this one is a read about the present, because the question it
        // answers is "does the chart already say this?" and the chart only says it now.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var cleared = 0;
        var chosen = 0;
        var overridden = 0;
        var untouched = 0;

        foreach (var employeeId in withAManager)
        {
            var record = await _employees.GetAsync(employeeId, cancellationToken);
            var storedId = record?.LineManagerEmployeeId;

            if (record is null || string.IsNullOrEmpty(storedId))
            {
                // The index row is ahead of the item, or the value has gone since. Nothing to do
                // and nothing to count: there is no stored manager to decide about.
                continue;
            }

            var organisation = await _organisation.GetAsync(employeeId, today, cancellationToken);

            if (organisation?.Position is not { } position)
            {
                // NOT THIS STEP'S BUSINESS AT ALL. Every pre-F6 employee and every tenant with no
                // positions is here. There is no reporting line to derive from, so the stored value
                // is the only answer there is and clearing it would destroy information.
                untouched++;
                continue;
            }

            if (record.LineManagerKind == StoredLineManagerKind.Override)
            {
                // Stated as deliberate since the addendum. Taken at its word even where the person
                // happens to hold the post above, which is the one case the holder comparison below
                // would get wrong.
                overridden++;
                continue;
            }

            if (record.LineManagerKind is StoredLineManagerKind.Chosen
                or StoredLineManagerKind.ChosenActing)
            {
                // Written since the addendum, so it is already only stored because somebody chose.
                // There is nothing here for a repair of the old write to do.
                chosen++;
                continue;
            }

            // Who the chart would give if nothing were stored: the holders of the post above, or —
            // while that post is vacant — the holders of the nearest held post above the gap. The
            // same two lines GetLineManagerAsync uses, from the same model.
            var reportsTo = position.ReportsTo;
            var acting = reportsTo.Kind == ReportsToKind.Vacant ? reportsTo.Acting : null;
            var candidates = acting?.Holders ?? reportsTo.Holders;

            if (candidates.Count == 1 &&
                string.Equals(candidates[0].EmployeeId, storedId, StringComparison.Ordinal))
            {
                // A WRITTEN-DOWN DERIVATION. One holder, and the record names them — which is what
                // the old code wrote on every save, and what froze when that post changed hands.
                // The answer this employee shows does not change; it starts being recomputed.
                await _employees.SetLineManagerAsync(
                    employeeId, null, StoredLineManagerKind.None, cancellationToken);

                cleared++;
                continue;
            }

            if (candidates.Any(holder =>
                    string.Equals(holder.EmployeeId, storedId, StringComparison.Ordinal)))
            {
                // One of several, which nothing but a person could have picked. D28 A: kept.
                chosen++;
                continue;
            }

            // Off the list entirely — including the top of the line, where there is no list. Kept,
            // and read afterwards by GetLineManagerAsync's own rules.
            overridden++;
        }

        return new LineManagerBackfillCounts(cleared, chosen, overridden, untouched);
    }
}
