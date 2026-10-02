using Microsoft.Extensions.Localization;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// One rule violation: which rule, which thing broke it, and what to tell the user.
/// </summary>
/// <param name="Rule">The rule, in a form a caller can branch on and a test can assert.</param>
/// <param name="Subject">
/// The code or id of the offending node, type, structure or employee — whatever the rule is about.
/// Empty only for a rule that is about the request as a whole rather than about one thing.
/// </param>
/// <param name="Message">
/// The localised sentence for the user. Separate from <paramref name="Rule"/> on purpose: no
/// caller should ever have to match on message text, and no user should ever be shown an enum.
/// </param>
public sealed record DimensionError(DimensionRule Rule, string Subject, LocalizedString Message)
{
    /// <summary>
    /// Whether this is advisory rather than blocking. Architecture section 6 has exactly one such
    /// rule today — a parent that is not yet effective when its child is — and it exists because
    /// pre-building next year's structure is legitimate and must not be refused.
    /// </summary>
    public bool IsAdvisory => Rule == DimensionRule.ParentNotEffectiveWhenChildIs;
}
