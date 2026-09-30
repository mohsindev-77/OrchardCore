namespace WorkMate.Platform.Models;

/// <summary>
/// The tenant's right-to-left preference. Orchard Core 3.0.1 already switches the admin
/// shell's direction with the active culture, so the platform's job is to say whether that
/// is what the tenant wants or whether one direction is pinned.
/// </summary>
public enum TextDirectionPreference
{
    /// <summary>Direction follows the active culture: right to left under <c>ar</c>.</summary>
    FollowCulture,

    /// <summary>Always right to left, whichever culture the user is in.</summary>
    RightToLeft,

    /// <summary>Always left to right, whichever culture the user is in.</summary>
    LeftToRight,
}
