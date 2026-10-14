using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// How a dimension appears in the Organisation menu: whether it is shown, what it reads, and
/// where it sits among the others.
/// </summary>
/// <remarks>
/// <b>One value rather than three parameters on two service methods</b>, for the reason
/// <c>EmployeeDetails</c> is one on <c>IEmployeeService</c>: without it, <c>CreateAsync</c> and
/// <c>UpdateAsync</c> each grow a parameter every time the menu does, and a caller that cares about
/// none of it has three defaults to pass.
///
/// <b>History worth knowing before changing this.</b> It was written in prompt 4 session D for a
/// per-type menu that shipped and was withdrawn by the product owner on sight, and it spent one
/// slice reduced to nothing but the parts <c>Migrations.UpdateFrom7Async</c> called — a frozen step
/// ADR-0009 will not let anybody remove. FRS v2.5 §1.2 and decision D3 ask for the feature again,
/// and ADR-0024 brings it back with the three things that killed it fixed: an authored plural
/// instead of a guessed one, one entry per dimension type rather than per type per structure, and a
/// switch to turn one off.
/// </remarks>
/// <param name="ShowInMenu">Whether the dimension gets its own entry.</param>
/// <param name="PluralName">
/// What the entry reads, in both languages. Empty means "use the singular" — a dimension type with
/// no authored plural reads as <em>Department</em> rather than as <em>Departments</em>, which is
/// wrong-looking and never wrong.
/// </param>
/// <param name="MenuOrder">
/// Lower first. Ties break on the <b>English name</b>, never on the caption — a caption is in the
/// reader's language, and ordering by it is the defect that gave an Arabic reader a different menu
/// from an English one. See <c>DimensionMenuComposition</c>.
/// </param>
public sealed record DimensionTypeMenu(bool ShowInMenu, BilingualText PluralName, int MenuOrder)
{
    /// <summary>
    /// What a dimension type gets when nobody has said: in the menu, no authored plural, unordered.
    /// </summary>
    /// <remarks>
    /// <b>No plural is invented here, and that is the change from <see cref="DefaultFor"/>.</b> A
    /// type created without one reads as its singular until somebody authors one, which is the
    /// fallback <see cref="DimensionTypeDocument.MenuCaption"/> implements.
    /// </remarks>
    public static DimensionTypeMenu Default { get; } =
        new(ShowInMenu: true, new BilingualText(string.Empty, string.Empty), MenuOrder: 0);

    /// <summary>
    /// <b>Frozen.</b> What a dimension type got from <c>Migrations.UpdateFrom7Async</c>, which has
    /// shipped and so can never be edited (ADR-0009).
    /// </summary>
    /// <remarks>
    /// <b>English gained an "s" and Arabic did not</b>, which is a fair summary of why the per-type
    /// menu did not survive contact with a real tenant: "Branch" became "Branchs", and Arabic has no
    /// suffix rule to guess with at all — قسم pluralises to أقسام by changing the word.
    ///
    /// Changing what this returns would change what an upgrade wrote on tenants that have already
    /// run it, which is the thing append-only migrations exist to make impossible.
    ///
    /// <b>It has a second job now, and it is the reason this is still here rather than merely
    /// unremovable.</b> <c>Migrations.UpdateFrom10Async</c> uses it as the <em>detector</em> for its
    /// own output: a stored plural that is exactly what this function would have produced is a guess
    /// nobody authored, and is replaced by the singular. One that differs is something a person
    /// typed on session D's editor, and is left alone. A frozen function is the only safe way to ask
    /// "did the frozen step write this?", because any other rule would drift from what it actually
    /// did.
    /// </remarks>
    public static DimensionTypeMenu DefaultFor(BilingualText name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var english = name.En?.Trim() ?? string.Empty;

        return new DimensionTypeMenu(
            ShowInMenu: true,
            new BilingualText(
                english.Length == 0 ? string.Empty : english + "s",
                name.Ar?.Trim() ?? string.Empty),
            MenuOrder: 0);
    }
}
