using System.Globalization;
using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// The Organisation menu's dimension section, as data: one group per dimension name, each with the
/// values a reader can jump straight to. ADR-0024.
/// </summary>
/// <remarks>
/// <b>Culture-neutral on purpose, because it is cached per tenant and read by both languages.</b>
/// Every caption is held as a <see cref="BilingualText"/> and resolved at render time, so one cached
/// instance serves an English reader and an Arabic one. The one thing that cannot be deferred is the
/// <em>order</em> of the values — alphabetical in the reader's language, which is a different list
/// in each — so both orders are computed when the menu is built and the renderer picks one. See
/// <see cref="DimensionMenuGroup.EnglishOrder"/>.
///
/// <b>Nothing here is a query.</b> It is a snapshot, built once and invalidated by signal
/// (ADR-0019), because a navigation provider runs on every admin request and must not put a
/// per-dimension query on that path.
/// </remarks>
public sealed record DimensionMenu(IReadOnlyList<DimensionMenuGroup> Groups)
{
    /// <summary>An empty menu — a tenant with no dimensions yet, and the safe default.</summary>
    public static DimensionMenu Empty { get; } = new([]);
}

/// <summary>
/// One entry in the Organisation menu: every dimension type sharing an English singular name,
/// behind one caption.
/// </summary>
/// <remarks>
/// <b>Why types are grouped by name rather than shown one each.</b> A menu entry per dimension
/// <em>type</em> read <em>Departments, Departments, Departments</em> on a tenant with three demo
/// organisations loaded, because each declared its own Department type. Decision D17 proposed fixing
/// that in the data — have the demos share one type — and that was built and reverted: a dimension
/// type is a level of every structure that declares it, so a shared type put every company's
/// divisions on every company's chart. Decision **D23** fixes it in the menu instead, which is where
/// the duplication was visible and where it does no harm to the graph.
///
/// <b>The group is a menu concept and nothing else.</b> It owns no records, it is not stored, and no
/// query is scoped by it except the list screen's "All <em>plural</em>", which simply asks for
/// several types at once. Two types named Department remain two types with two separate trees.
/// </remarks>
/// <param name="GroupKey">
/// The English singular name, trimmed and lower-cased — the grouping key, case-insensitive per D23.
/// It is also what the list screen's <c>group</c> query-string value carries, which is why it is a
/// name rather than a generated id: a URL a reader can read, and one that survives a type being
/// added to the group.
/// </param>
/// <param name="Name">The singular, from the group's first type. What a caption falls back to.</param>
/// <param name="PluralName">
/// The authored plural, from the group's first type, or empty. <b>Never derived.</b>
/// <see cref="DimensionTypeMenu.DefaultFor"/> records what deriving one cost.
/// </param>
/// <param name="DimensionTypeIds">
/// The types in the group, in the group's own order. One when a tenant has one Department.
/// </param>
/// <param name="MenuOrder">The lowest <c>MenuOrder</c> of its types: a group sorts where its first member would.</param>
/// <param name="TotalValues">
/// Every active value across the group's types, whether or not it is listed here. What
/// <em>Show all (N)</em> counts.
/// </param>
/// <param name="ShowsOrganisation">
/// Whether the group's types span more than one structure, in which case each value is suffixed with
/// its organisation and the list screen offers an Organisation column and filter (D23 C).
/// </param>
public sealed record DimensionMenuGroup(
    string GroupKey,
    BilingualText Name,
    BilingualText PluralName,
    IReadOnlyList<string> DimensionTypeIds,
    int MenuOrder,
    IReadOnlyList<DimensionMenuValue> EnglishOrder,
    IReadOnlyList<DimensionMenuValue> ArabicOrder,
    int TotalValues,
    bool ShowsOrganisation)
{
    /// <summary>
    /// How many values a dimension lists in the menu before it stops and offers <em>Show all</em>.
    /// </summary>
    /// <remarks>
    /// Fifteen, from decision D23. It is a menu, not a list screen: past about this many the entries
    /// stop being a shortcut and start being the thing the list screen already does better, and a
    /// sidebar that scrolls for a hundred departments is the <em>Departments, Departments,
    /// Departments</em> complaint in a new costume.
    /// </remarks>
    public const int MaxValues = 15;

    /// <summary>What the entry reads: the authored plural, else the singular. Never a guess.</summary>
    public string Caption(CultureInfo? culture = null)
    {
        var plural = PluralName.Display(culture);

        return string.IsNullOrWhiteSpace(plural) ? Name.Display(culture) : plural;
    }

    /// <summary>
    /// Whether this reader has a real plural to read, or is seeing the singular stand in for one.
    /// </summary>
    /// <remarks>
    /// <b>Asked per language, because a plural can be authored in one and not the other.</b> What
    /// it decides is how the menu phrases the entry that opens the whole dimension: with a plural it
    /// reads <em>All Departments</em>, and without one <em>All Workstream units</em> — a whole
    /// sentence the localiser owns, rather than <em>All Workstream</em>, which is the broken English
    /// that falling back to the singular produces the moment the word is put in a sentence.
    ///
    /// Decision D24. <c>StandardDimensionPlurals</c> gives the shipped names their plurals on
    /// upgrade, so this is the answer for a name the product does not ship and nobody has authored.
    /// </remarks>
    public bool HasPlural(CultureInfo? culture = null) =>
        !string.IsNullOrWhiteSpace(PluralName.Display(culture));

    /// <summary>The values to list, in this reader's alphabetical order.</summary>
    public IReadOnlyList<DimensionMenuValue> Values(CultureInfo? culture = null) =>
        BilingualText.IsArabic(culture ?? CultureInfo.CurrentUICulture) ? ArabicOrder : EnglishOrder;

    /// <summary>Whether there are more values than the menu lists, so <em>Show all</em> is offered.</summary>
    public bool HasMore => TotalValues > EnglishOrder.Count;
}

/// <summary>
/// One value under a dimension's menu entry: a department a reader can open in one click.
/// </summary>
/// <param name="Organisation">
/// The organisation this value's type belongs to, when the group spans more than one and the type
/// belongs to exactly one — so <em>Civil</em> under a Department group covering three companies
/// reads <em>Civil (Zenith)</em>. The structure's <b>short name</b> (D24), falling back to its full
/// name. Null when there is nothing to disambiguate, and null when the type is itself on several
/// structures, because then the suffix would be a guess about which one was meant.
/// </param>
/// <param name="Parent">
/// The unit this value sits under, used only when another value in the same group reads the same.
/// Null when the value's type is on more than one chart, for the reason
/// <paramref name="Organisation"/> is.
/// </param>
/// <param name="AmbiguousInEnglish">
/// Whether an English reader sees this name more than once in this group, and so needs the parent.
/// </param>
/// <param name="AmbiguousInArabic"><inheritdoc cref="AmbiguousInEnglish"/></param>
public sealed record DimensionMenuValue(
    string RecordId,
    string DimensionTypeId,
    BilingualText Name,
    BilingualText? Organisation,
    BilingualText? Parent = null,
    bool AmbiguousInEnglish = false,
    bool AmbiguousInArabic = false)
{
    /// <summary>
    /// The caption: the name, its parent when the name alone is ambiguous, and its organisation
    /// when the dimension spans more than one.
    /// </summary>
    /// <remarks>
    /// <b>The parent is added per language, not per value.</b> Two units can collide in one
    /// language and not the other — a tenant that translated one of them and not the other — so
    /// adding the parent to both readers because one of them needs it would put a disambiguator
    /// where there is nothing to disambiguate.
    ///
    /// <b>Why the parent rather than the code.</b> Crescent has two departments called
    /// <em>Credit</em>, one per branch; what tells a reader which is which is <em>Lahore Main
    /// Branch</em>, not <c>crescent-dept-lhr-credit</c>. The code is on the list screen, which is
    /// where somebody who wants the code is going.
    ///
    /// The order is name, parent, organisation — narrowest to widest, so the words a reader is
    /// scanning for come first and the qualifiers read as qualifiers.
    /// </remarks>
    public string Caption(CultureInfo? culture = null)
    {
        var resolved = culture ?? CultureInfo.CurrentUICulture;
        var caption = Name.Display(resolved);

        var ambiguous = BilingualText.IsArabic(resolved) ? AmbiguousInArabic : AmbiguousInEnglish;

        if (ambiguous && Parent is not null && Parent.Display(resolved) is { Length: > 0 } parent)
        {
            // An en dash with spaces, which reads as an apposition rather than as part of the name.
            // A hyphen would look like a compound unit name, which some of them are.
            caption = $"{caption} – {parent}";
        }

        if (Organisation is null)
        {
            return caption;
        }

        var organisation = Organisation.Display(resolved);

        return string.IsNullOrWhiteSpace(organisation) ? caption : $"{caption} ({organisation})";
    }
}
