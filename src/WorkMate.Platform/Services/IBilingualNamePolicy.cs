namespace WorkMate.Platform.Services;

/// <summary>
/// Whether this tenant insists on the Arabic half of a bilingual name.
/// </summary>
/// <remarks>
/// A seam of its own rather than every caller reading <c>WorkMateSettings.RequireArabicNames</c>
/// through <see cref="IWorkMateSettingsService"/>. Two reasons.
///
/// The rule is asked on every write path in the product — dimension types, structures, records,
/// attribute labels, recipe steps — and those callers need one boolean, not the whole settings
/// surface. Narrow it here and the question has one answer and one name wherever it is asked.
///
/// And it is the answer, not the setting, that matters: ADR-0003's addendum makes Arabic optional
/// by default, so a caller that forgets to ask gets the permissive behaviour, which is the right
/// way round for a rule being relaxed rather than tightened.
/// </remarks>
public interface IBilingualNamePolicy
{
    /// <summary>
    /// Whether a name with no Arabic text must be refused. False unless the tenant has turned
    /// "Require Arabic names" on.
    /// </summary>
    Task<bool> RequiresArabicAsync(CancellationToken cancellationToken = default);
}
