using System.Globalization;
using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// What a dimension's menu entry reads, and the one thing it must never do: invent a word.
/// </summary>
/// <remarks>
/// ADR-0024. A per-dimension menu shipped once with the plural derived — English gained an "s",
/// Arabic gained nothing — and the product owner withdrew it on sight because it read
/// <em>Branchs</em>. These are the rules that replaced the derivation, as a table rather than as
/// prose, because "no rule pluralises an English noun correctly" is only true if no rule is
/// applied.
/// </remarks>
public sealed class DimensionTypeMenuTests
{
    private static readonly CultureInfo English = new("en");
    private static readonly CultureInfo Arabic = new("ar");

    private static DimensionTypeDocument TypeWith(BilingualText name, BilingualText plural) => new()
    {
        Name = name,
        PluralName = plural,
    };

    [Fact]
    public void TheCaptionIsTheAuthoredPluralWhenThereIsOne()
    {
        var branch = TypeWith(new BilingualText("Branch", "فرع"), new BilingualText("Branches", "الفروع"));

        branch.MenuCaption(English).Should().Be("Branches");
        branch.MenuCaption(Arabic).Should().Be("الفروع");
    }

    /// <summary>
    /// No plural at all reads as the singular — short, and never wrong.
    /// </summary>
    /// <remarks>
    /// The trade ADR-0024 takes rather than deriving one. Two kinds of tenant land here: one
    /// created after the per-type menu was withdrawn, where nothing ever set a plural, and one
    /// whose guessed plural <c>Migrations.UpdateFrom10Async</c> replaced with the singular.
    /// </remarks>
    [Fact]
    public void NoAuthoredPluralReadsAsTheSingular()
    {
        var workstream = TypeWith(
            new BilingualText("Workstream", "مسار العمل"),
            new BilingualText(string.Empty, string.Empty));

        workstream.MenuCaption(English).Should().Be("Workstream");
        workstream.MenuCaption(Arabic).Should().Be("مسار العمل");
    }

    /// <summary>
    /// A plural authored in one language only falls back to the other, never to a blank.
    /// </summary>
    /// <remarks>
    /// Arabic is optional across the platform since ADR-0003's addendum, so this is the ordinary
    /// case for an English-speaking tenant rather than a corner of it. A blank menu entry is
    /// unclickable and says nothing, which is strictly worse than a word in the other language.
    /// </remarks>
    [Fact]
    public void APluralInOneLanguageOnlyFallsBackRatherThanBlanking()
    {
        var team = TypeWith(new BilingualText("Team", "فريق"), new BilingualText("Teams", string.Empty));

        team.MenuCaption(English).Should().Be("Teams");
        team.MenuCaption(Arabic).Should().Be("Teams", "a blank entry would be unclickable and silent");
    }

    /// <summary>
    /// Whitespace is not a plural.
    /// </summary>
    /// <remarks>
    /// A text box somebody tabbed through holds a space, and a space renders as a gap where a menu
    /// entry should be. <c>BilingualText</c> treats whitespace as absent for exactly this reason,
    /// and the fallback has to agree with it.
    /// </remarks>
    [Fact]
    public void WhitespaceIsNotAPlural()
    {
        var division = TypeWith(new BilingualText("Division", "قطاع"), new BilingualText("   ", "  "));

        division.MenuCaption(English).Should().Be("Division");
        division.MenuCaption(Arabic).Should().Be("قطاع");
    }

    /// <summary>
    /// A new dimension type is shown, unordered, and has no plural invented for it.
    /// </summary>
    [Fact]
    public void TheDefaultShowsTheDimensionAndInventsNothing()
    {
        DimensionTypeMenu.Default.ShowInMenu.Should().BeTrue(
            "a dimension somebody defined is one they use; off-by-default reads as a missing screen");

        DimensionTypeMenu.Default.PluralName.En.Should().BeEmpty();
        DimensionTypeMenu.Default.PluralName.Ar.Should().BeEmpty();
        DimensionTypeMenu.Default.MenuOrder.Should().Be(0);
    }

    /// <summary>
    /// The frozen derivation still derives exactly what it always did.
    /// </summary>
    /// <remarks>
    /// <b>This asserts a defect on purpose.</b> <c>DimensionTypeMenu.DefaultFor</c> is what the
    /// shipped <c>Migrations.UpdateFrom7Async</c> called, and ADR-0009 makes a shipped step
    /// unchangeable — so changing what this returns would change what an upgrade wrote on tenants
    /// that already ran it.
    ///
    /// It has a second job now, which is why it is tested rather than merely tolerated:
    /// <c>UpdateFrom10Async</c> uses it as the <em>detector</em> for its own output. A stored plural
    /// equal to what this produces is a guess nobody authored and is replaced by the singular; one
    /// that differs is something a person typed and is left alone. That only works while this keeps
    /// producing exactly what it produced then — including <em>Branchs</em>.
    /// </remarks>
    [Fact]
    public void TheFrozenDerivationStillProducesTheWordThatCausedTheWithdrawal()
    {
        var derived = DimensionTypeMenu.DefaultFor(new BilingualText("Branch", "فرع"));

        derived.PluralName.En.Should().Be("Branchs", "the exact guess the withdrawn menu displayed");
        derived.PluralName.Ar.Should().Be("فرع", "Arabic was never pluralised at all");
        derived.ShowInMenu.Should().BeTrue();
    }
}
