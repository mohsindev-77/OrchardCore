using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// The plurals of the dimension names the product ships and customers overwhelmingly use, in both
/// languages. Decision D24.
/// </summary>
/// <remarks>
/// <b>This is a lookup, not a rule, and the difference is the whole point.</b> ADR-0024 took the
/// position that a plural is <em>authored</em> and never derived, because deriving one produced
/// <em>Branchs</em> in English and nothing at all in Arabic. Nothing here contradicts that: there is
/// no algorithm, no suffix, and no attempt to pluralise a word that is not on the list. A name that
/// is not here gets no plural, and the menu phrases itself around the singular instead.
///
/// <b>What it is for.</b> A tenant created before the dimension type editor had a plural field has
/// types whose plural is blank — or, if it upgraded through <c>Migrations.UpdateFrom10Async</c>,
/// whose plural is the singular, which that step wrote as a visible placeholder. Either way the menu
/// reads <em>Department</em> and <em>All Department</em>. <c>Migrations.UpdateFrom11Async</c> fills
/// those in from this table so an existing tenant is correct without anybody editing twelve types by
/// hand.
///
/// <b>Frozen once shipped, like <see cref="DimensionTypeMenu.DefaultFor"/> and for the same reason
/// (ADR-0009).</b> A migration that has run on a tenant will never run again, so changing what this
/// returns changes what new tenants get and silently leaves upgraded ones on the old answer.
/// Correcting a word here therefore needs a <em>new</em> migration step, not an edit to this one.
///
/// <b>The two halves are looked up independently</b>, English by the English name and Arabic by the
/// Arabic name. A tenant whose Division is <em>قطاع</em> and one whose Division is <em>قسم</em> are
/// both right about their own organisation, and keying the Arabic plural off the English name would
/// give one of them the other's word.
/// </remarks>
public static class StandardDimensionPlurals
{
    /// <summary>
    /// The shipped English names and their plurals. Matched case-insensitively on the trimmed name.
    /// </summary>
    private static readonly Dictionary<string, string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Business unit"] = "Business units",
        ["Division"] = "Divisions",
        ["Department"] = "Departments",
        ["Section"] = "Sections",
        ["Team"] = "Teams",
        ["Region"] = "Regions",
        ["Branch"] = "Branches",
        ["Project"] = "Projects",
        ["Cost centre"] = "Cost centres",
        ["Position"] = "Positions",
        ["Grade step"] = "Grade steps",
    };

    /// <summary>
    /// The Arabic singulars this product ships or seeds, and their plurals.
    /// </summary>
    /// <remarks>
    /// Keyed on the Arabic name because that is what the plural is the plural <em>of</em>. Two
    /// entries map to Division — قطاع and قسم — because the reference recipes use both, and each
    /// pluralises its own way.
    ///
    /// A name that is not here leaves the Arabic plural empty, which reads as the Arabic singular:
    /// short, and never a word Arabic does not have.
    /// </remarks>
    private static readonly Dictionary<string, string> Arabic = new(StringComparer.Ordinal)
    {
        ["وحدة أعمال"] = "وحدات الأعمال",
        ["قطاع"] = "القطاعات",
        ["قسم"] = "الأقسام",
        ["إدارة"] = "الإدارات",
        ["شعبة"] = "الشعب",
        ["فريق"] = "الفرق",
        ["منطقة"] = "المناطق",
        ["فرع"] = "الفروع",
        ["مشروع"] = "المشاريع",
        ["مركز التكلفة"] = "مراكز التكلفة",
        ["منصب"] = "المناصب",
        ["درجة"] = "الدرجات",
    };

    /// <summary>
    /// The plural for this name, each half empty when the name is not one this table knows.
    /// </summary>
    public static BilingualText For(BilingualText name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new BilingualText(
            English.GetValueOrDefault(name.En?.Trim() ?? string.Empty, string.Empty),
            Arabic.GetValueOrDefault(name.Ar?.Trim() ?? string.Empty, string.Empty));
    }

    /// <summary>
    /// Whether a stored plural is one nobody authored, and so may be filled in.
    /// </summary>
    /// <remarks>
    /// <b>Two states count as unauthored, and the second is the one that matters.</b> Blank is the
    /// obvious case — a type created before the editor had the field. The other is a plural that is
    /// <em>identical to the singular</em>, which is exactly what <c>UpdateFrom10Async</c> wrote when
    /// it cleared session D's guesses: a visible placeholder, chosen so the editor showed what the
    /// menu would say. Neither is a word a person chose, and leaving both alone would mean the fix
    /// reached no tenant that had already upgraded — which is every tenant.
    ///
    /// Filling one cannot damage a name that really does pluralise to itself, because this is only
    /// ever consulted together with <see cref="For"/>, and a name whose standard plural equals its
    /// singular is not in that table.
    /// </remarks>
    public static bool MayBeFilled(string? plural, string? singular) =>
        string.IsNullOrWhiteSpace(plural) ||
        string.Equals(plural.Trim(), singular?.Trim() ?? string.Empty, StringComparison.Ordinal);
}
