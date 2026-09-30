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
}
