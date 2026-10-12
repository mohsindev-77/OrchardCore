using FluentAssertions;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// The two code rules a lookup list lives by, and the one comparison.
/// </summary>
/// <remarks>
/// A unit test rather than a tenant one, for the reason <c>EmployeeCodeTests</c> gives: the rule
/// has to be checkable before anything is written, and the awkward cases are worth enumerating
/// without standing a tenant up.
///
/// The cases that matter are the ones where the two rules differ. A list code is an identifier we
/// author and that ends up in a URL segment; an entry code is whatever the customer already calls
/// the thing. Holding both to one rule would either refuse <c>001</c> and <c>ID.CARD</c>, which
/// customers arrive with, or let a slash into a route value.
/// </remarks>
public sealed class LookupCodeTests
{
    [Theory]
    [InlineData("grade")]
    [InlineData("job-title")]
    [InlineData("employment_type")]
    [InlineData("documentType9")]
    [InlineData("a")]
    public void AWellFormedListCodeIsAccepted(string code) =>
        LookupCodes.IsValidListCode(code).Should().BeTrue();

    [Theory]
    [InlineData("", "empty")]
    [InlineData("1grade", "a leading digit reads as an index when a recipe is scanned")]
    [InlineData("-grade", "a leading hyphen is not a name")]
    [InlineData("job title", "a space in a code is a code half of whose uses will trim it")]
    [InlineData("job/title", "a slash would split the URL segment this becomes")]
    [InlineData("job.title", "a dot is reserved for entry codes, where customers already use it")]
    public void AMalformedListCodeIsRefused(string code, string because) =>
        LookupCodes.IsValidListCode(code).Should().BeFalse(because);

    /// <summary>
    /// An entry code is as loose as an employee code, and for the same reason: it is the value the
    /// customer already holds.
    /// </summary>
    [Theory]
    [InlineData("G1")]
    [InlineData("001")]
    [InlineData("FT")]
    [InlineData("ID.CARD")]
    [InlineData("HR/0042")]
    [InlineData("passport-renewal")]
    public void AWellFormedEntryCodeIsAccepted(string code) =>
        LookupCodes.IsValidItemCode(code).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("full time")]
    [InlineData(".leading")]
    [InlineData("/leading")]
    [InlineData("-leading")]
    public void AMalformedEntryCodeIsRefused(string code) =>
        LookupCodes.IsValidItemCode(code).Should().BeFalse();

    [Fact]
    public void ACodeLongerThanTheColumnIsRefused()
    {
        var tooLong = new string('a', LookupCodes.MaxLength + 1);

        LookupCodes.IsValidListCode(tooLong).Should().BeFalse();
        LookupCodes.IsValidItemCode(tooLong).Should().BeFalse();

        LookupCodes.IsValidListCode(new string('a', LookupCodes.MaxLength)).Should().BeTrue();
        LookupCodes.IsValidItemCode(new string('a', LookupCodes.MaxLength)).Should().BeTrue();
    }

    /// <summary>
    /// Case is preserved on the way in and ignored on the way out, so a recipe that shouts a code
    /// finds the entry that is there rather than creating a second one beside it.
    /// </summary>
    [Fact]
    public void TwoSpellingsOfOneCodeAreTheSameCode()
    {
        LookupCodes.Same("G1", "g1").Should().BeTrue();
        LookupCodes.Same(" G1 ", "g1").Should().BeTrue();
        LookupCodes.Same("G1", "G2").Should().BeFalse();

        LookupCodes.Normalise("  grade  ").Should().Be("grade");
        LookupCodes.Normalise(null).Should().BeEmpty();
    }

    /// <summary>
    /// Ordinal, not culture-aware. The Turkish dotless i is the standard reason
    /// <c>InvariantCultureIgnoreCase</c> is not the same thing as ordinal case-insensitivity, and
    /// a code is an identifier rather than a word.
    /// </summary>
    [Fact]
    public void TheComparerIsOrdinalRatherThanCultureAware() =>
        LookupCodes.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
}
