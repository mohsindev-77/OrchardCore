namespace WorkMate.Core;

/// <summary>
/// A closed effective range: both <see cref="From"/> and <see cref="To"/> are inclusive,
/// and <see cref="To"/> is null for an open-ended record. Two adjacent ranges therefore
/// end and begin on consecutive days, never on the same day — the mid-month transfer in
/// /docs/dimension-engine-architecture.md is the worked example: the old assignment runs
/// to 15 March and the new one from 16 March.
/// Every dated write on the platform takes one of these explicitly.
/// </summary>
public readonly record struct EffectiveRange(DateOnly From, DateOnly? To)
{
    public bool Contains(DateOnly date) => date >= From && (To is null || date <= To);

    /// <summary>
    /// True when the range covers no day at all, which happens when an end lands before its own
    /// start. A caller computing a range from others has to check this: an empty range is a
    /// legitimate answer meaning "these never overlapped", not an error.
    /// </summary>
    public bool IsEmpty => To is not null && To < From;

    /// <summary>
    /// The period both ranges cover, or null when they never overlap.
    /// </summary>
    /// <remarks>
    /// This is the arithmetic the dated closure index is built on, per ADR-0005: a closure row's
    /// range is the intersection of the ranges of every link along the path from ancestor to
    /// descendant. A section that joined its department in March, under a department that joined
    /// its division in June, is only under that division from June — and the intersection is
    /// what says so.
    ///
    /// Open ends are treated as "still running", so intersecting two open-ended ranges gives an
    /// open-ended range starting at the later of the two.
    /// </remarks>
    public EffectiveRange? Intersect(EffectiveRange other)
    {
        var from = From > other.From ? From : other.From;

        var to = (To, other.To) switch
        {
            (null, null) => (DateOnly?)null,
            (null, var right) => right,
            (var left, null) => left,
            var (left, right) => left < right ? left : right,
        };

        return to is not null && to < from ? null : new EffectiveRange(from, to);
    }

    /// <summary>True when the two ranges share at least one day.</summary>
    public bool Overlaps(EffectiveRange other) => Intersect(other) is not null;

    /// <summary>
    /// The same range ended on <paramref name="lastDay"/>, for closing an open period when
    /// something supersedes it.
    /// </summary>
    public EffectiveRange EndingOn(DateOnly lastDay) => this with { To = lastDay };
}
