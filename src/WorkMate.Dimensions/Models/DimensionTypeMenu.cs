using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// <b>Retired.</b> The defaults a withdrawn feature gave a dimension type's menu entry.
/// </summary>
/// <remarks>
/// Nothing uses this but <c>Migrations.UpdateFrom7Async</c>, which has shipped and so cannot be
/// edited or removed — ADR-0009, migrations are append-only. See
/// <see cref="DimensionTypeDocument.ShowInMenu"/> for what the feature was, why it was withdrawn,
/// and why its three fields are left on the document rather than cleared.
///
/// Kept deliberately small: the parts the frozen step calls, and nothing else. The settings reader,
/// the service parameters and the editor that went with them are gone.
/// </remarks>
/// <param name="PluralName">What the menu entry read, in both platform languages.</param>
public sealed record DimensionTypeMenu(bool ShowInMenu, BilingualText PluralName, int MenuOrder)
{
    /// <summary>
    /// What a type got when nobody had said: in the menu, named for the singular, unordered.
    /// </summary>
    /// <remarks>
    /// <b>English gained an "s" and Arabic did not</b>, which is a fair summary of why the feature
    /// did not survive contact with a real tenant: "Branch" became "Branchs", and Arabic has no
    /// suffix rule to guess with at all — قسم pluralises to أقسام by changing the word.
    ///
    /// Frozen along with the step that calls it. Changing what this returns would change what an
    /// upgrade wrote on tenants that have already run it, which is the thing append-only
    /// migrations exist to make impossible.
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
