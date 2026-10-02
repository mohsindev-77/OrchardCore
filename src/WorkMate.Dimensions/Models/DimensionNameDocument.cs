using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// What a record has been called, and when.
/// </summary>
/// <remarks>
/// Architecture section 5: "A rename can be corrective or substantive, and these need different
/// answers. A typo fix should apply retrospectively; a genuine renaming should leave last year's
/// reports showing the old name."
///
/// That is why the name is kept twice. <see cref="DimensionRecordPart"/> carries the current
/// name, so the editor, the generated title, the pickers and the sort all work without a join.
/// This document is the authority for what the record was called on a given date, and it is the
/// only thing a historical report consults.
///
/// One document per record, not one per name: the rows for a record are read together, written
/// together, and validated together, so they are one unit of concurrency.
/// </remarks>
public sealed class DimensionNameDocument
{
    /// <summary>YesSql's document id. Assigned on first save; never meaningful to the business.</summary>
    public long Id { get; set; }

    /// <summary>YesSql's optimistic concurrency token, per ADR-0005.</summary>
    public long Version { get; set; }

    /// <summary>The record these names belong to, by content item id.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// The names in effect order, earliest first, contiguous and non-overlapping. Exactly one
    /// row is open ended, and it is the last.
    /// </summary>
    public IReadOnlyList<DimensionNamePeriod> Periods { get; set; } = [];

    /// <summary>What the record was called on <paramref name="asAt"/>, or null if it had no name then.</summary>
    public BilingualText? NameOn(DateOnly asAt) =>
        Periods.FirstOrDefault(period => period.Range.Contains(asAt))?.Name;

    /// <summary>The name in effect at the end of the history: what the record is called now.</summary>
    public BilingualText? CurrentName =>
        Periods.Count == 0 ? null : Periods[^1].Name;
}

/// <summary>One name, and the period it was the record's name for.</summary>
/// <param name="Name">The name in both platform languages.</param>
/// <param name="Range">
/// When it applied. The end is inclusive, as everywhere on this platform: a name that ran to
/// 15 March was replaced by one that ran from 16 March.
/// </param>
public sealed record DimensionNamePeriod(BilingualText Name, EffectiveRange Range);
