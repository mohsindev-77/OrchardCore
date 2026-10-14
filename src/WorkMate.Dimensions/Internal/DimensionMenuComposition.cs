using System.Globalization;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Internal;

/// <summary>
/// Turns a tenant's dimension types, their structures and their values into the Organisation menu.
/// ADR-0024, decision D23.
/// </summary>
/// <remarks>
/// <b>Pure, and separate from the service that feeds it, so the rules can be tested without a
/// tenant.</b> Every rule here is a product decision somebody will want to change — how names are
/// grouped, how many values a menu lists, when an organisation is named in brackets — and each is a
/// line in <c>DimensionMenuCompositionTests</c> rather than a scenario that needs a database, a
/// shell and four recipes to reach.
/// </remarks>
internal static class DimensionMenuComposition
{
    /// <summary>The English culture the menu's English ordering is computed in.</summary>
    /// <remarks>
    /// Fixed rather than <c>CurrentUICulture</c>, because this runs once per tenant and is cached
    /// for readers in both languages. Sorting in whatever culture happened to build the cache is how
    /// a menu ends up ordered for whoever opened the admin first after a restart.
    /// </remarks>
    private static readonly StringComparer English =
        StringComparer.Create(CultureInfo.GetCultureInfo("en"), ignoreCase: true);

    /// <inheritdoc cref="English"/>
    private static readonly StringComparer Arabic =
        StringComparer.Create(CultureInfo.GetCultureInfo("ar"), ignoreCase: true);

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    private static readonly CultureInfo ArabicCulture = CultureInfo.GetCultureInfo("ar");

    /// <summary>
    /// Builds the menu.
    /// </summary>
    /// <param name="types">
    /// Every dimension type in the tenant. Retired ones and ones hidden from the menu are filtered
    /// here rather than by the caller, so there is one statement of what earns an entry.
    /// </param>
    /// <param name="structuresOfType">
    /// The structures each type is a level of, keyed by dimension type id. A type absent from the
    /// map is on no structure, which is a real state — a type created on the Dimension types screen
    /// before anybody added it to a chart.
    /// </param>
    /// <param name="activeRecordsOfType">
    /// The values of each type that are effective on the date the menu is about, keyed by dimension
    /// type id. Already filtered: this method does no dating, because the date is the caller's and
    /// a pure function should not be reading a clock.
    /// </param>
    /// <param name="parentsOf">
    /// The unit each value sits under, keyed by record id, for the values that need one. Supplied
    /// by the caller because resolving a parent is a closure-index read and this method does no
    /// queries; absent for a value whose name is unique, which is almost all of them.
    /// </param>
    public static DimensionMenu Compose(
        IReadOnlyList<DimensionTypeDocument> types,
        IReadOnlyDictionary<string, IReadOnlyList<StructureRef>> structuresOfType,
        IReadOnlyDictionary<string, IReadOnlyList<DimensionNodeRef>> activeRecordsOfType,
        IReadOnlyDictionary<string, BilingualText>? parentsOf = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(structuresOfType);
        ArgumentNullException.ThrowIfNull(activeRecordsOfType);

        var parents = parentsOf ?? new Dictionary<string, BilingualText>(StringComparer.Ordinal);

        var shown = types
            .Where(type => type.ShowInMenu && type.RetiredOn is null)
            .ToList();

        var groups = shown
            .GroupBy(KeyOf, StringComparer.Ordinal)
            .Select(group => Build(group.Key, [.. group], structuresOfType, activeRecordsOfType, parents))
            .OrderBy(group => group.MenuOrder)
            // Then by the key, which is the English name lower-cased — never by the caption. A
            // caption is in the reader's language, and ordering by it is precisely the defect that
            // gave an Arabic reader a different menu from an English one.
            .ThenBy(group => group.GroupKey, StringComparer.Ordinal)
            .ToList();

        return new DimensionMenu(groups);
    }

    /// <summary>
    /// What two dimension types have to share to become one menu entry: their English singular
    /// name, trimmed, case-insensitively.
    /// </summary>
    /// <remarks>
    /// <b>The English name rather than the Arabic or the caption</b>, for the reason the ordering
    /// uses it: it is the one value that is the same for every reader, so an Arabic reader and an
    /// English one see the same grouping. ADR-0003 makes English the required half, so it is also
    /// the one that is always there.
    ///
    /// <b>A type with no English name keys on its code instead.</b> Only reachable on a tenant that
    /// has bypassed the editor, but the alternative is that every such type collapses into one
    /// entry captioned with whichever happened to be first — a worse answer than one entry each.
    /// The leading space is what keeps such a key out of the namespace of real ones: a real key is a
    /// trimmed name, so it can never begin with one.
    /// </remarks>
    public static string KeyOf(DimensionTypeDocument type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var english = type.Name.En?.Trim() ?? string.Empty;

        return english.Length == 0
            ? " code:" + type.Code
            : english.ToLowerInvariant();
    }

    /// <summary>
    /// The record ids whose display name in <paramref name="culture"/> is shared with another value
    /// in the same group — the ones that need their parent to be told apart.
    /// </summary>
    /// <remarks>
    /// Crescent is the case this exists for: two departments called <em>Credit</em>, one in the
    /// Lahore branch and one in Karachi, and two called <em>Operations</em> beside them. A name is
    /// not unique within a tenant — only a code is — so the menu listed <em>Credit (Crescent)</em>
    /// twice with nothing to choose between them.
    ///
    /// Compared on the resolved display name rather than on the stored half, because that is what
    /// the reader actually sees: a unit with no Arabic name falls back to its English, and can
    /// collide with an English name that way.
    /// </remarks>
    private static HashSet<string> Ambiguous(IReadOnlyList<DimensionMenuValue> values, CultureInfo culture) =>
    [
        .. values
            .GroupBy(value => value.Name.Display(culture), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(value => value.RecordId)),
    ];

    private static DimensionMenuGroup Build(
        string key,
        IReadOnlyList<DimensionTypeDocument> members,
        IReadOnlyDictionary<string, IReadOnlyList<StructureRef>> structuresOfType,
        IReadOnlyDictionary<string, IReadOnlyList<DimensionNodeRef>> activeRecordsOfType,
        IReadOnlyDictionary<string, BilingualText> parentsOf)
    {
        // The group's own order, which decides which member names it and which plural it reads.
        // MenuOrder first so a tenant can choose; code as the tiebreak so it is never arbitrary.
        var ordered = members
            .OrderBy(type => type.MenuOrder)
            .ThenBy(type => type.Code, StringComparer.Ordinal)
            .ToList();

        var first = ordered[0];

        // The first authored plural wins, not the first member's. Two types named Department should
        // carry identical plurals — the recipes ship them that way — but if one was left blank the
        // useful answer is the one somebody did author, not the blank that happened to sort first.
        var plural = ordered
            .Select(type => type.PluralName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name.En) || !string.IsNullOrWhiteSpace(name.Ar))
            ?? BilingualText.Empty;

        var structuresByTypeId = ordered.ToDictionary(
            type => type.DimensionTypeId,
            type => structuresOfType.GetValueOrDefault(type.DimensionTypeId) ?? [],
            StringComparer.Ordinal);

        // "Spans more than one organisation" is asked of the group, not of a type: one Department
        // type on one chart needs no suffix; three Department types on three charts need one on
        // every value, including the ones whose own type is unambiguous.
        var showsOrganisation = structuresByTypeId.Values
            .SelectMany(structures => structures.Select(structure => structure.StructureId))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count() > 1;

        var raw = new List<DimensionMenuValue>();

        foreach (var type in ordered)
        {
            // Named only when this type is on exactly one chart. On several, "which organisation is
            // this department in" has more than one true answer and a bracket would pick one. The
            // SHORT name (D24): "Civil (Zenith)" is a menu entry, "Civil (Zenith Engineering &
            // Construction)" is a sentence in a sidebar.
            var structures = structuresByTypeId[type.DimensionTypeId];
            var onOneChart = structures.Count == 1;

            foreach (var record in activeRecordsOfType.GetValueOrDefault(type.DimensionTypeId) ?? [])
            {
                raw.Add(new DimensionMenuValue(
                    record.RecordId,
                    type.DimensionTypeId,
                    new BilingualText(record.NameEn, record.NameAr),
                    showsOrganisation && onOneChart ? structures[0].ShortName : null,
                    onOneChart ? parentsOf.GetValueOrDefault(record.RecordId) : null));
            }
        }

        // Which names a reader of each language meets twice. Asked per language because a tenant
        // can translate one of two same-named units and not the other, and because the whole point
        // of the parent suffix is to be absent where there is nothing to disambiguate.
        var ambiguousInEnglish = Ambiguous(raw, EnglishCulture);
        var ambiguousInArabic = Ambiguous(raw, ArabicCulture);

        var values = raw
            .Select(value => value with
            {
                AmbiguousInEnglish = ambiguousInEnglish.Contains(value.RecordId),
                AmbiguousInArabic = ambiguousInArabic.Contains(value.RecordId),
            })
            .ToList();

        return new DimensionMenuGroup(
            key,
            first.Name,
            plural,
            [.. ordered.Select(type => type.DimensionTypeId)],
            ordered.Min(type => type.MenuOrder),
            FirstBy(values, EnglishCulture, English),
            FirstBy(values, ArabicCulture, Arabic),
            values.Count,
            showsOrganisation);
    }

    /// <summary>
    /// The first <see cref="DimensionMenuGroup.MaxValues"/> values, alphabetical in one language.
    /// </summary>
    /// <remarks>
    /// <b>Sorted, then cut — not cut, then sorted.</b> The fifteen a reader sees are the first
    /// fifteen of the dimension, so <em>Show all</em> means "and the rest after these", not "and
    /// fifteen arbitrary others as well".
    ///
    /// <b>By the caption, not by the name.</b> The caption is what the reader is scanning, and it
    /// is what makes two same-named units sort meaningfully: <em>Credit – Karachi</em> before
    /// <em>Credit – Lahore</em> rather than in whichever order their generated ids happen to fall.
    /// Decision D24; the parent is already on the value by the time this runs.
    ///
    /// <b>Both orders are computed and both are kept</b>, because they are genuinely different
    /// lists: Arabic sorts الفروع where English sorts Branches, so taking fifteen in one language
    /// and rendering them in the other would show an Arabic reader a set chosen by English spelling.
    /// Thirty entries of two fields is a cheap price for a menu that is correct in both.
    ///
    /// Ties break on the record id, so a tenant with two departments of the same name gets a stable
    /// order rather than one that changes between cache fills.
    /// </remarks>
    private static IReadOnlyList<DimensionMenuValue> FirstBy(
        IReadOnlyList<DimensionMenuValue> values,
        CultureInfo culture,
        StringComparer comparer) =>
        [
            .. values
                .OrderBy(value => value.Caption(culture), comparer)
                .ThenBy(value => value.RecordId, StringComparer.Ordinal)
                .Take(DimensionMenuGroup.MaxValues),
        ];
}

/// <summary>A structure, as the menu needs to know it: enough to name it and tell it from another.</summary>
/// <param name="ShortName">
/// What the menu puts in brackets: the structure's authored short name, or its full name when there
/// is none. Decision D24 — <em>Civil (Zenith)</em> rather than
/// <em>Civil (Zenith Engineering &amp; Construction)</em>.
/// </param>
internal sealed record StructureRef(string StructureId, BilingualText Name, BilingualText ShortName);
