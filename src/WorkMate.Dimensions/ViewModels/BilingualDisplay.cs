using System.Globalization;
using WorkMate.Core;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// Picking the right half of a bilingual name, and the right shape of a date, for a sentence the
/// reader is actually going to read.
/// </summary>
/// <remarks>
/// A card showing an English name on one line and an Arabic one on the next is bilingual display,
/// and it is correct: both names are the record's, and a reader of either language finds theirs.
/// A <em>sentence</em> is a different thing. "Support closed on 2026-10-06. الدعم" is not bilingual
/// — it is an English sentence with an ISO date and an Arabic word stranded after the full stop,
/// which reads as a bug to a reader of either language.
///
/// So anywhere a name or a date is interpolated into a localised string, it comes through here:
/// the name in the reader's language, the date in the reader's calendar and format, and the whole
/// sentence translated as one unit in the PO file rather than assembled from parts.
///
/// This is the display half of the rule <see cref="Internal.IsoDate"/> owns the other half of. On
/// the wire — a query string, a form field, an attribute the script reads — a date is always
/// ISO-8601, because there it has to mean the same day to everyone. On screen it is whatever the
/// reader's culture says a date looks like.
/// </remarks>
public static class BilingualDisplay
{
    /// <summary>
    /// The name to use in a sentence: Arabic when the page is Arabic, English otherwise, falling
    /// back to whichever half is populated.
    /// </summary>
    /// <remarks>
    /// The rule itself lives on <see cref="BilingualText.Display(string?, string?, CultureInfo?)"/>
    /// so that Platform's own templates answer it the same way. Arabic is optional since the
    /// ADR-0003 addendum, so this falls back rather than returning an empty string: a reader shown
    /// nothing is worse off than a reader shown the other language.
    /// </remarks>
    public static string Name(string? nameEn, string? nameAr) => BilingualText.Display(nameEn, nameAr);

    /// <inheritdoc cref="Name(string?, string?)"/>
    public static string Name(BilingualText? name) => Name(name?.En, name?.Ar);

    /// <summary>
    /// The name to use where the point is to identify the record rather than to read a sentence —
    /// the heading of an action screen, an entry in a picker — with the other language after it in
    /// brackets: "Support (الدعم)" to an English reader, "الدعم (Support)" to an Arabic one.
    /// </summary>
    /// <remarks>
    /// Brackets rather than a second line, because this appears mid-line. They are also what makes
    /// it legible: an unbracketed second name after the first reads as the stray word that started
    /// this class. When only one half is populated there is nothing to bracket and it is omitted.
    /// </remarks>
    public static string NameWithAlternate(string? nameEn, string? nameAr)
    {
        var arabic = BilingualText.IsArabic(CultureInfo.CurrentUICulture);
        var primary = Name(nameEn, nameAr);
        var secondary = (arabic ? nameEn : nameAr)?.Trim() ?? string.Empty;

        // No brackets when there is nothing to put in them. Arabic is optional, so this is the
        // ordinary case rather than the edge one, and "Support ()" is the empty-brackets defect
        // this method exists to avoid.
        return secondary.Length == 0 || secondary == primary
            ? primary
            : $"{primary} ({secondary})";
    }

    /// <summary>
    /// A date as the reader writes dates. Long form, because a bare numeric date is the one thing
    /// on a confirmation screen most worth being unambiguous about: 06/10 and 10/06 are the same
    /// day to two different readers and a different day to each of them.
    /// </summary>
    /// <remarks>
    /// Formatted against CurrentCulture, not CurrentUICulture: the UI culture decides which words
    /// the reader gets, the culture decides what a date looks like to them. Orchard's localisation
    /// middleware sets both from the request, so in practice they agree — but they are two different
    /// questions and the analyser is right to insist they be asked separately.
    /// </remarks>
    public static string Date(DateOnly date) =>
        date.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);

    /// <inheritdoc cref="Date(DateOnly)"/>
    public static string Date(DateOnly? date) => date is { } value ? Date(value) : string.Empty;

    /// <summary>
    /// The same, from the ISO-8601 string a form field carries, so a view holding a wire value can
    /// still render it as a sentence. Unparseable text is shown as it is rather than swallowed.
    /// </summary>
    public static string DateFromIso(string? iso) =>
        Internal.IsoDate.TryParse(iso, out var parsed) ? Date(parsed) : iso ?? string.Empty;

    /// <summary>
    /// The separator between the steps of a unit's path — "Crescent › Retail Banking › Credit".
    /// </summary>
    /// <remarks>
    /// Not localised, and not meant to be. It is a glyph rather than a word, it reads the same in
    /// both languages, and the admin shell already mirrors the whole line for an Arabic reader so
    /// the chevron ends up pointing the way the text runs.
    /// </remarks>
    public const string PathSeparator = " › ";

    /// <summary>
    /// A unit's full path, root first, in the reader's language.
    /// </summary>
    /// <remarks>
    /// <b>The path is what makes a picker usable on a real organisation.</b> Crescent has a
    /// "Credit" under Retail Banking and another under Corporate Banking, and an "Operations" under
    /// three different divisions. A flat list of names offers the reader several identical rows and
    /// no way to tell which is which; it is not a cosmetic improvement but the difference between a
    /// control that can be used correctly and one that cannot.
    /// </remarks>
    /// <param name="ancestors">The unit's ancestors, root first. Empty for a root.</param>
    public static string Path(IEnumerable<Services.DimensionNodeRef>? ancestors, string? nameEn, string? nameAr)
    {
        var steps = (ancestors ?? [])
            .Select(ancestor => Name(ancestor.NameEn, ancestor.NameAr))
            .Append(Name(nameEn, nameAr));

        return string.Join(PathSeparator, steps);
    }

    /// <inheritdoc cref="Path(IEnumerable{Services.DimensionNodeRef}, string, string)"/>
    public static string Path(IEnumerable<Services.DimensionNodeRef>? ancestors, Services.DimensionNodeRef unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return Path(ancestors, unit.NameEn, unit.NameAr);
    }
}
