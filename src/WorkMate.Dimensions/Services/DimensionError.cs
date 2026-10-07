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
/// <param name="Field">
/// The name of the form field this belongs under, when there is one, so a screen can show it where
/// the reader has to act rather than only in the summary at the top.
/// </param>
/// <remarks>
/// <paramref name="Field"/> is a hint, not a contract: a service has no business knowing a screen's
/// field names, but "the English name is missing" is about one box and saying so is the difference
/// between a message a user can act on and a list they have to match up themselves. A caller that
/// has no such field, like a recipe step, ignores it.
/// </remarks>
public sealed record DimensionError(
    DimensionRule Rule,
    string Subject,
    LocalizedString Message,
    string? Field = null)
{
    /// <summary>
    /// Whether this is advisory rather than blocking.
    /// </summary>
    /// <remarks>
    /// Two rules are. A parent that is not yet effective when its child is, because pre-building
    /// next year's structure is legitimate and must not be refused. And a pairing a non-strict
    /// structure has no rule for, because that is exactly what a structure says when it declares
    /// itself non-strict — see ADR-0010.
    ///
    /// Derived from the rule rather than carried as a flag on purpose: whether something blocks is
    /// a property of the rule, not of the occasion, so it cannot be set differently in two places.
    /// </remarks>
    public bool IsAdvisory =>
        Rule is DimensionRule.ParentNotEffectiveWhenChildIs or DimensionRule.ParentTypeNotDeclared;
}
