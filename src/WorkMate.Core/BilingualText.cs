namespace WorkMate.Core;

/// <summary>Every user-facing name on the platform carries both languages.</summary>
public sealed record BilingualText(string En, string Ar)
{
    public static readonly BilingualText Empty = new(string.Empty, string.Empty);
}
