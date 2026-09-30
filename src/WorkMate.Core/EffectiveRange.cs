namespace WorkMate.Core;

/// <summary>
/// A half-open effective range. <see cref="To"/> is null for an open-ended record.
/// Every dated write on the platform takes one of these explicitly.
/// </summary>
public readonly record struct EffectiveRange(DateOnly From, DateOnly? To)
{
    public bool Contains(DateOnly date) => date >= From && (To is null || date <= To);
}
