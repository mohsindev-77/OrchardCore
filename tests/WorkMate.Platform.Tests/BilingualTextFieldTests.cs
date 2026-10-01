using FluentAssertions;
using WorkMate.Core;
using WorkMate.Platform.Fields;
using Xunit;

namespace WorkMate.Platform.Tests;

public sealed class BilingualTextFieldTests
{
    [Fact]
    public void TheFieldConvertsToTheValueObjectServicesUse() =>
        new BilingualTextField { En = "Finance", Ar = "المالية" }
            .ToBilingualText().Should().Be(new BilingualText("Finance", "المالية"));

    [Fact]
    public void TheValueObjectSetsBothLanguagesAtOnce()
    {
        var field = new BilingualTextField { En = "old", Ar = "قديم" };

        field.Set(new BilingualText("Finance", "المالية"));

        field.En.Should().Be("Finance");
        field.Ar.Should().Be("المالية");
    }

    [Theory]
    [InlineData("ar", "المالية")]
    [InlineData("ar-BH", "المالية")]
    [InlineData("en", "Finance")]
    [InlineData("en-GB", "Finance")]
    [InlineData(null, "Finance")]
    public void TheFieldShowsTheReadersLanguage(string? culture, string expected) =>
        new BilingualTextField { En = "Finance", Ar = "المالية" }
            .ForCulture(culture).Should().Be(expected);

    [Fact]
    public void AnUntranslatedValueFallsBackRatherThanShowingNothing()
    {
        // A half-translated record should still be readable. An empty name in a picker reads as
        // missing data, which is worse than a name in the other language.
        var field = new BilingualTextField { En = "Finance", Ar = string.Empty };

        field.ForCulture("ar").Should().Be("Finance");
    }

    [Fact]
    public void TheFallbackWorksInBothDirections()
    {
        var field = new BilingualTextField { En = string.Empty, Ar = "المالية" };

        field.ForCulture("en").Should().Be("المالية");
    }

    [Fact]
    public void WhitespaceCountsAsUntranslated() =>
        new BilingualTextField { En = "Finance", Ar = "   " }
            .ForCulture("ar").Should().Be("Finance");

    [Fact]
    public void AFieldWithNeitherLanguageReturnsEmptyRatherThanThrowing() =>
        new BilingualTextField().ForCulture("ar").Should().BeEmpty();

    [Fact]
    public void TheFieldIsNotSealedBecauseOrchardProxiesIt() =>
        // Learned the hard way on the settings view model: Orchard builds shapes through Castle
        // DynamicProxy, which subclasses the type, and a sealed one fails at render.
        typeof(BilingualTextField).IsSealed.Should().BeFalse();

    [Fact]
    public void RequiringEnglishIsTheDefaultBecauseItIsTheSortKeyAndTheFallback()
    {
        var settings = new BilingualTextFieldSettings();

        settings.RequireEnglish.Should().BeTrue();
        settings.RequireArabic.Should().BeFalse();
        settings.MaxLength.Should().Be(0, "zero means no limit");
    }
}
