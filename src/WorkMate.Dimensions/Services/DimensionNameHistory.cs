using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The two kinds of rename, as pure functions over a record's name history.
/// </summary>
/// <remarks>
/// Architecture section 5 makes the distinction a product requirement rather than an
/// implementation detail: "A typo fix should apply retrospectively; a genuine renaming should
/// leave last year's reports showing the old name. The engine supports both ... and the designer
/// asks the user which they mean rather than guessing."
///
/// These are static functions on an immutable history rather than methods that mutate a
/// document, because the awkward cases are all about which period is being touched and what
/// happens to its neighbours — and those are far easier to get right, and to test exhaustively,
/// without a database in the way. <c>IDimensionService</c> wires them to storage and the audit
/// trail; the rules live here.
/// </remarks>
public static class DimensionNameHistory
{
    /// <summary>
    /// Starts a record's name history with one open-ended period from
    /// <paramref name="effectiveFrom"/>.
    /// </summary>
    public static IReadOnlyList<DimensionNamePeriod> Start(BilingualText name, DateOnly effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(name);

        return [new DimensionNamePeriod(name, new EffectiveRange(effectiveFrom, null))];
    }

    /// <summary>
    /// A **corrective** rename: the record was always called this, and the old text was a
    /// mistake. Replaces the text of the period covering <paramref name="withinPeriodContaining"/>
    /// and leaves every date alone, so a report for a prior period shows the corrected name.
    /// </summary>
    /// <remarks>
    /// The target is any period, not only the open one. A typo can sit in a closed period — it is
    /// found precisely because somebody ran last year's report and did not recognise the name —
    /// and a correction that could only touch the current name would leave the error exactly
    /// where it was found. Pass a date inside the period to correct; today corrects the current
    /// name.
    /// </remarks>
    /// <returns>
    /// The corrected history, or null when no period covers that date and there is therefore
    /// nothing to correct.
    /// </returns>
    public static IReadOnlyList<DimensionNamePeriod>? Correct(
        IReadOnlyList<DimensionNamePeriod> periods,
        BilingualText name,
        DateOnly withinPeriodContaining)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(name);

        var index = IndexOfPeriodContaining(periods, withinPeriodContaining);

        if (index < 0)
        {
            return null;
        }

        var corrected = periods.ToArray();
        corrected[index] = corrected[index] with { Name = name };

        return corrected;
    }

    /// <summary>
    /// A **substantive** rename: the record genuinely became something else on
    /// <paramref name="effectiveFrom"/>. Closes the period in effect the day before and opens a
    /// new one, so a report for a prior period keeps showing the old name.
    /// </summary>
    /// <returns>
    /// The extended history, or null when the rename cannot be placed — see
    /// <see cref="SubstantiveRenameFailure"/> for why.
    /// </returns>
    public static IReadOnlyList<DimensionNamePeriod>? Rename(
        IReadOnlyList<DimensionNamePeriod> periods,
        BilingualText name,
        DateOnly effectiveFrom,
        out SubstantiveRenameFailure failure)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(name);

        failure = SubstantiveRenameFailure.None;

        if (periods.Count == 0)
        {
            failure = SubstantiveRenameFailure.NoHistory;
            return null;
        }

        var index = IndexOfPeriodContaining(periods, effectiveFrom);

        if (index < 0)
        {
            // Before the record had a name at all. Renaming from a date the record did not yet
            // exist on is a dating error, not a rename.
            failure = SubstantiveRenameFailure.BeforeTheHistoryStarts;
            return null;
        }

        var target = periods[index];

        if (target.Range.From == effectiveFrom)
        {
            // The rename starts exactly where the period does, so there is nothing of the old
            // name left on that day: replace it rather than leaving a zero-length period behind.
            var replaced = periods.Take(index).ToList();
            replaced.Add(target with { Name = name });
            replaced.AddRange(periods.Skip(index + 1));

            return replaced;
        }

        // Everything up to and including the target, with the target closed the day before.
        var rewritten = periods.Take(index).ToList();

        rewritten.Add(target with
        {
            Range = new EffectiveRange(target.Range.From, effectiveFrom.AddDays(-1)),
        });

        // The new name runs to wherever the period it displaced ran to, so a rename inside a
        // closed period does not swallow the periods after it.
        rewritten.Add(new DimensionNamePeriod(name, new EffectiveRange(effectiveFrom, target.Range.To)));
        rewritten.AddRange(periods.Skip(index + 1));

        return rewritten;
    }

    /// <summary>
    /// Whether a history is well formed: ordered, contiguous, non-overlapping, and open ended
    /// exactly once at the end.
    /// </summary>
    /// <remarks>
    /// Used by the tests and by the verification command. A gap in a name history means a date
    /// on which a report can find no name for a unit that certainly had one.
    /// </remarks>
    public static bool IsWellFormed(IReadOnlyList<DimensionNamePeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);

        if (periods.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < periods.Count; index++)
        {
            var period = periods[index];
            var isLast = index == periods.Count - 1;

            if (period.Range.To is null != isLast)
            {
                // Only the last period is open ended, and the last one always is.
                return false;
            }

            if (period.Range.To is not null && period.Range.To < period.Range.From)
            {
                return false;
            }

            if (!isLast && periods[index + 1].Range.From != period.Range.To!.Value.AddDays(1))
            {
                // Contiguous, with no gap and no overlap. The end is inclusive, so the next
                // period starts the following day.
                return false;
            }
        }

        return true;
    }

    private static int IndexOfPeriodContaining(IReadOnlyList<DimensionNamePeriod> periods, DateOnly date)
    {
        for (var index = 0; index < periods.Count; index++)
        {
            if (periods[index].Range.Contains(date))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>Why a substantive rename could not be placed.</summary>
public enum SubstantiveRenameFailure
{
    /// <summary>It was placed.</summary>
    None,

    /// <summary>The record has no name history to extend.</summary>
    NoHistory,

    /// <summary>The effective date falls before the record had any name.</summary>
    BeforeTheHistoryStarts,
}
