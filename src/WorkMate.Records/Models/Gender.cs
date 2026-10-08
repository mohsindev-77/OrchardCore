namespace WorkMate.Records.Models;

/// <summary>
/// The gender recorded on an employee record, for the statutory returns and the entitlements that
/// depend on it.
/// </summary>
/// <remarks>
/// <see cref="Unspecified"/> is the default and is a legitimate stored value, not a missing one: an
/// employee record created from an import that did not carry it must be able to say so rather than
/// assert something nobody told it. A rule that needs the answer — a maternity entitlement, a GOSI
/// return — asks for it at the point it is needed and reports its absence there, where somebody can
/// act on it.
/// </remarks>
public enum Gender
{
    /// <summary>Not recorded. Never guessed from a name or a title.</summary>
    Unspecified,

    Female,

    Male,
}
