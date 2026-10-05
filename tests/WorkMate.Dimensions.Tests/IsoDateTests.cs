using System.Globalization;
using FluentAssertions;
using WorkMate.Dimensions.Internal;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The date on the wire means the same day to everyone.
/// </summary>
/// <remarks>
/// A date control shows 05/10/2026 to one user and 10/05/2026 to another while submitting the same
/// <c>2026-10-05</c> either way, so a culture-sensitive parse of that value is a defect that only
/// appears for users in some locales — and under a culture whose default calendar is not Gregorian,
/// such as ar-SA with Umm al-Qura, it is not even the same year. Every culture WorkMate ships for
/// is checked here, plus one that would fail loudly if the invariant parse were ever dropped.
/// </remarks>
public sealed class IsoDateTests
{
    public static TheoryData<string> Cultures() =>
    [
        "en",
        "en-US",
        "en-GB",
        "ar",
        // The one that matters most: ar-SA's default calendar is Umm al-Qura, so a parse or a
        // format that uses the ambient culture produces a completely different date here.
        "ar-SA",
    ];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void AWireDateIsTheSameDayInEveryCulture(string culture)
    {
        using var _ = new CultureScope(culture);

        IsoDate.TryParse("2026-10-05", out var date).Should().BeTrue();

        date.Year.Should().Be(2026);
        date.Month.Should().Be(10);
        date.Day.Should().Be(5);
    }

    /// <summary>
    /// The ambiguous one. 05/10/2026 and 10/05/2026 are the same day written two ways, and nothing
    /// in the round trip may ever decide which by looking at the current culture.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void TheFifthOfOctoberIsNeverReadAsTheTenthOfMay(string culture)
    {
        using var _ = new CultureScope(culture);

        IsoDate.TryParse("2026-10-05", out var october).Should().BeTrue();
        IsoDate.TryParse("2026-05-10", out var may).Should().BeTrue();

        october.Should().Be(new DateOnly(2026, 10, 5));
        may.Should().Be(new DateOnly(2026, 5, 10));
        october.Should().NotBe(may);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ADateIsWrittenBackExactlyAsItCameIn(string culture)
    {
        using var _ = new CultureScope(culture);

        new DateOnly(2026, 10, 5).ToIso().Should().Be("2026-10-05");
        new DateOnly(2026, 5, 10).ToIso().Should().Be("2026-05-10");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ADateWrittenOutAndReadBackIsTheSameDay(string culture)
    {
        using var _ = new CultureScope(culture);

        var original = new DateOnly(2026, 10, 5);

        IsoDate.TryParse(original.ToIso(), out var round).Should().BeTrue();
        round.Should().Be(original);
    }

    /// <summary>
    /// Anything that is not the wire format is refused rather than guessed at, including the two
    /// display formats a date control might show. Guessing is how 10/05/2026 becomes May.
    /// </summary>
    [Theory]
    [InlineData("05/10/2026")]
    [InlineData("10/05/2026")]
    [InlineData("5 October 2026")]
    [InlineData("2026-13-01")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatIsNotTheWireFormatIsRefused(string? value)
    {
        using var scope = new CultureScope("en-US");

        IsoDate.TryParse(value, out _).Should().BeFalse();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture;
        private readonly CultureInfo _uiCulture;

        public CultureScope(string name)
        {
            _culture = CultureInfo.CurrentCulture;
            _uiCulture = CultureInfo.CurrentUICulture;

            var culture = new CultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
