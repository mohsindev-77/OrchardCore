namespace WorkMate.Records.Services;

/// <summary>Which rule about a split a row or a form broke.</summary>
/// <remarks>
/// A code rather than a sentence, because this type has no localiser and must not grow one: it is
/// the half of the rule that is decidable without a tenant, a session or a culture, and that is
/// what lets <c>PlacementSplitTests</c> state every case as a unit test. The words live in
/// <see cref="PlacementSplitMessages"/>.
/// </remarks>
public enum PlacementSplitRule
{
    /// <summary>A row with no unit chosen. The picker refuses typed text, so this is an empty row.</summary>
    UnitMissing,

    /// <summary>The same unit on two rows, which is two answers to one question.</summary>
    DuplicateUnit,

    /// <summary>An allocation of zero or less. Removing the row is what the reader meant.</summary>
    AllocationNotPositive,

    /// <summary>The allocations do not total 100 percent.</summary>
    AllocationTotal,

    /// <summary>Not exactly one row is marked as the main placement.</summary>
    MainCount,

    /// <summary>Every row was removed. Ending the placement is its own dated operation.</summary>
    NoRows,

    /// <summary>No effective date, or one that is not a date.</summary>
    EffectiveDateMissing,

    /// <summary>
    /// An effective date earlier than a change already on record for this employee on this
    /// structure.
    /// </summary>
    /// <remarks>
    /// Refused rather than recorded. <c>IEmployeeAssignmentService.ReallocateAsync</c> states the
    /// whole picture from its effective date onwards — rows that start on or after it are replaced
    /// outright — so a split backdated behind a later change would silently swallow that change.
    /// That is the "overwrite" behaviour ADR-0005's addendum examined and rejected for moves,
    /// reached through a different door: a second, undocumented way to destroy a recorded decision
    /// without going through the one mechanism that is audited and shows a dry run.
    ///
    /// ADR-0005 chose split for moves because a link carries one parent at a time and the arithmetic
    /// is unambiguous. A split is a set of rows that must total 100 on every date, so "claim only
    /// up to the next change" would have to re-derive the later set as well — which is not a
    /// correction, it is a guess about what somebody else meant. So this screen refuses and says
    /// what to do instead, which is the third option ADR-0005 listed and the only one that is
    /// honest here.
    /// </remarks>
    EffectiveDateBeforeLatestChange,
}

/// <summary>One row of a split as a form posts it, before anything has been resolved.</summary>
/// <param name="RecordId">The unit, or null when the row is empty.</param>
/// <param name="IsMain">
/// Whether this is the placement that answers "where does this person work". Exactly one of a set
/// must be.
/// </param>
public sealed record PlacementSplitRow(string? RecordId, decimal AllocationPercent, bool IsMain);

/// <summary>One rule a split broke, and where.</summary>
/// <param name="RowIndex">
/// The row it is about, or null when it is about the set as a whole. The screen uses it to put the
/// message under the control the reader has to change, rather than in a list at the top they have
/// to match up themselves.
/// </param>
/// <param name="Arguments">What the message interpolates: a total, a count, a date.</param>
public sealed record PlacementSplitViolation(
    PlacementSplitRule Rule,
    int? RowIndex = null,
    IReadOnlyList<object?>? Arguments = null);

/// <summary>
/// The rules a split has to satisfy before anything is written.
/// </summary>
/// <remarks>
/// <b>These are not a second copy of the engine's rules.</b>
/// <c>IDimensionValidator.ValidateAssignmentAsync</c> remains the authority and runs again on the
/// write; it refuses the same things and the screen surfaces what it says. What this adds is the
/// two things a validator working on a resolved split cannot do: say <em>which row</em> is wrong,
/// so the message lands under the control, and catch an empty row before it reaches a service that
/// would report it as an unknown record id.
///
/// It also owns the one rule that is the screen's alone — the backdating refusal — because it is a
/// rule about what this operation would destroy rather than about whether the result is valid. See
/// <see cref="PlacementSplitRule.EffectiveDateBeforeLatestChange"/>.
///
/// Pure: no session, no localiser, no clock. Every case is a unit test.
/// </remarks>
public static class PlacementSplit
{
    /// <param name="latestChange">
    /// The date of the latest change already recorded for this employee on this structure, or null
    /// when there is none.
    /// </param>
    public static IReadOnlyList<PlacementSplitViolation> Validate(
        IReadOnlyList<PlacementSplitRow> rows,
        DateOnly? effectiveFrom,
        DateOnly? latestChange)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var violations = new List<PlacementSplitViolation>();

        if (effectiveFrom is null)
        {
            violations.Add(new PlacementSplitViolation(PlacementSplitRule.EffectiveDateMissing));
        }
        else if (latestChange is { } latest && effectiveFrom < latest)
        {
            violations.Add(new PlacementSplitViolation(
                PlacementSplitRule.EffectiveDateBeforeLatestChange, null, [latest]));
        }

        if (rows.Count == 0)
        {
            violations.Add(new PlacementSplitViolation(PlacementSplitRule.NoRows));

            return violations;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            if (string.IsNullOrEmpty(row.RecordId))
            {
                violations.Add(new PlacementSplitViolation(PlacementSplitRule.UnitMissing, index));
            }
            else if (!seen.Add(row.RecordId))
            {
                violations.Add(new PlacementSplitViolation(PlacementSplitRule.DuplicateUnit, index));
            }

            if (row.AllocationPercent <= 0m)
            {
                violations.Add(new PlacementSplitViolation(PlacementSplitRule.AllocationNotPositive, index));
            }
        }

        var total = rows.Sum(row => row.AllocationPercent);

        if (total != 100m)
        {
            violations.Add(new PlacementSplitViolation(PlacementSplitRule.AllocationTotal, null, [total]));
        }

        var mains = rows.Count(row => row.IsMain);

        if (mains != 1)
        {
            violations.Add(new PlacementSplitViolation(PlacementSplitRule.MainCount, null, [mains]));
        }

        return violations;
    }
}
