using System.Globalization;
using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.ViewModels;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// ADR-0003's addendum: Arabic is optional, and a reader is never shown a blank name.
/// </summary>
/// <remarks>
/// The fallback is the part most easily lost. "Arabic is optional" is a validation change and
/// shows up the moment anyone tries to save; "an Arabic reader sees the English name when there is
/// no Arabic one" is a display change that fails silently — the screen renders, the row is there,
/// and the name is simply missing. So it is asserted here against both cultures explicitly rather
/// than left to whichever culture the test host happens to run under.
/// </remarks>
public sealed class BilingualFallbackTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");

    [Fact]
    public void AnArabicReaderWithNoArabicNameSeesTheEnglishOne() =>
        BilingualText.Display("Support", string.Empty, Arabic).Should().Be("Support");

    [Fact]
    public void AnEnglishReaderWithNoEnglishNameSeesTheArabicOne() =>
        BilingualText.Display(string.Empty, "الدعم", English).Should().Be("الدعم");

    [Fact]
    public void EachReaderGetsTheirOwnLanguageWhenBothArePresent()
    {
        BilingualText.Display("Support", "الدعم", English).Should().Be("Support");
        BilingualText.Display("Support", "الدعم", Arabic).Should().Be("الدعم");
    }

    /// <summary>Whitespace is not a name, and must not render as a gap where a heading should be.</summary>
    [Fact]
    public void AWhitespaceOnlyNameCountsAsAbsent() =>
        BilingualText.Display("Support", "   ", Arabic).Should().Be("Support");

    [Fact]
    public void BothHalvesEmptyIsAnEmptyString() =>
        BilingualText.Display(string.Empty, null, Arabic).Should().BeEmpty();

    [Fact]
    public void RegionalArabicCulturesReadAsArabic()
    {
        BilingualText.IsArabic(CultureInfo.GetCultureInfo("ar-SA")).Should().BeTrue();
        BilingualText.IsArabic(CultureInfo.GetCultureInfo("ar-BH")).Should().BeTrue();
        BilingualText.IsArabic(CultureInfo.GetCultureInfo("en-GB")).Should().BeFalse();
    }

    [Fact]
    public void HasArabicIsFalseForAnEmptyOrWhitespaceHalf()
    {
        new BilingualText("Support", "الدعم").HasArabic.Should().BeTrue();
        new BilingualText("Support", string.Empty).HasArabic.Should().BeFalse();
        new BilingualText("Support", "  ").HasArabic.Should().BeFalse();
    }

    // ---- the sentences ------------------------------------------------------------------

    /// <summary>
    /// A bilingual sentence with a missing half renders with no empty brackets and no stray
    /// separator — the defect that prompted this whole rule in the first place.
    /// </summary>
    [Fact]
    public void AnIdentityLineOmitsTheBracketsWhenThereIsNothingToPutInThem()
    {
        Under(English, () => BilingualDisplay.NameWithAlternate("Support", "الدعم"))
            .Should().Be("Support (الدعم)");

        var withoutArabic = Under(English, () => BilingualDisplay.NameWithAlternate("Support", string.Empty));

        withoutArabic.Should().Be("Support");
        withoutArabic.Should().NotContain("(");
    }

    [Fact]
    public void AnArabicReaderGetsAnArabicIdentityLineAndFallsBackCleanly()
    {
        Under(Arabic, () => BilingualDisplay.NameWithAlternate("Support", "الدعم"))
            .Should().Be("الدعم (Support)");

        // No Arabic to lead with, so the English stands alone rather than appearing twice.
        Under(Arabic, () => BilingualDisplay.NameWithAlternate("Support", string.Empty))
            .Should().Be("Support");
    }

    [Fact]
    public void ASentenceInterpolatesTheReadableHalf()
    {
        Under(Arabic, () => BilingualDisplay.Name("Support", string.Empty)).Should().Be("Support");
        Under(Arabic, () => BilingualDisplay.Name("Support", "الدعم")).Should().Be("الدعم");
    }

    /// <summary>
    /// Runs <paramref name="read"/> as a reader of <paramref name="culture"/> would see it.
    /// </summary>
    /// <remarks>
    /// The UI culture is what decides which half is shown, and it is ambient — so the only honest
    /// way to assert on it is to set it, run, and put it back. Restored in a finally because xUnit
    /// shares the thread across tests in a class.
    /// </remarks>
    private static string Under(CultureInfo culture, Func<string> read)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = culture;
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
