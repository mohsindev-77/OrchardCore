using System.Globalization;

namespace WorkMate.Core;

/// <summary>Every user-facing name on the platform carries both languages.</summary>
/// <remarks>
/// Both halves, but not both required. Since the ADR-0003 addendum, English is required and Arabic
/// is optional unless a tenant turns "Require Arabic names" on. That makes the display rule part of
/// the type's job rather than each screen's: ask for the name in the reader's language and get
/// something readable, never a blank.
/// </remarks>
public sealed record BilingualText(string En, string Ar)
{
    /// <summary>The English half. Never null: an absent name is an empty string.</summary>
    /// <remarks>
    /// Normalised here rather than at every reader. Null and empty mean the same thing — "there is
    /// no name in this language" — and a type that can represent that two ways makes every
    /// consumer handle both, which is a guarantee nobody can keep. Records deserialise through
    /// their primary constructor, so a document written before this rule, or by any path that
    /// passed a null, is normalised on the way out of the database as well as on the way in.
    ///
    /// Not a cosmetic tidy-up: six 500s came from a null Arabic half reaching a <c>.Trim()</c>.
    /// </remarks>
    public string En { get; init; } = En ?? string.Empty;

    /// <inheritdoc cref="En"/>
    public string Ar { get; init; } = Ar ?? string.Empty;

    /// <summary>
    /// The half the reader of <paramref name="culture"/> can read, falling back to the other.
    /// </summary>
    /// <remarks>
    /// The fallback is the whole point. Arabic is optional, so a record may genuinely have no
    /// Arabic name, and an Arabic reader shown an empty string is shown nothing at all — worse
    /// than being shown the English, which they can at least act on. It falls back in both
    /// directions for symmetry, though only one direction can happen while English is required.
    ///
    /// Whitespace counts as absent: a name of three spaces is not a name, and trimming here is
    /// what stops it rendering as a gap where a heading should be.
    /// </remarks>
    public static string Display(string? en, string? ar, CultureInfo? culture = null) =>
        IsArabic(culture ?? CultureInfo.CurrentUICulture) ? Prefer(ar, en) : Prefer(en, ar);

    /// <inheritdoc cref="Display(string?, string?, CultureInfo?)"/>
    public string Display(CultureInfo? culture = null) => Display(En, Ar, culture);

    /// <summary>Whether this pair carries any Arabic at all.</summary>
    public bool HasArabic => !string.IsNullOrWhiteSpace(Ar);

    /// <summary>
    /// Whether a culture reads right to left in the sense that matters here: Arabic, in any of its
    /// regional forms. Checked on the two-letter name so ar-SA, ar-BH and plain ar all answer the
    /// same.
    /// </summary>
    public static bool IsArabic(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return culture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);
    }

    private static string Prefer(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? second?.Trim() ?? string.Empty : first.Trim();

    /// <summary>
    /// A new, empty instance every time it is read — never a shared one.
    /// </summary>
    /// <remarks>
    /// This was a <c>static readonly</c> field until ADR-0007: one object, handed out to every
    /// caller, including every document property that used it as a default value. A document
    /// property that defaults to the same shared instance as every other document of its kind is
    /// exactly the shape that let two unrelated dimension types read each other's name once a
    /// tenant held more than one — see <c>DimensionTypeDocument.Name</c>'s history. A property
    /// getter that allocates removes the hazard at the source: nothing that reads
    /// <see cref="Empty"/> can ever receive the same object another caller also holds, so there is
    /// nothing left for a deserialiser — or anything else — to write into on one caller's behalf
    /// and have a second caller see.
    /// </remarks>
    public static BilingualText Empty => new(string.Empty, string.Empty);
}
