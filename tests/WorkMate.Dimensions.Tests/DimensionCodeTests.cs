using FluentAssertions;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The code rules and the content type name derived from them.
/// </summary>
/// <remarks>
/// These matter more than their size suggests. A code that derives an invalid or colliding
/// content type name fails when the content definition is written, by which time the dimension
/// type document has already been saved and the tenant is half configured.
/// </remarks>
public sealed class DimensionCodeTests
{
    [Theory]
    [InlineData("DEPT")]
    [InlineData("d")]
    [InlineData("cost-centre")]
    [InlineData("cost_centre")]
    [InlineData("Level2")]
    public void AWellFormedCodeIsAccepted(string code) =>
        DimensionCodes.IsValidCode(code).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1DEPT")]        // must start with a letter: a content type name cannot
    [InlineData("-dept")]
    [InlineData("cost centre")]  // a space would not survive into a content type name
    [InlineData("cost.centre")]
    [InlineData("قسم")]          // the code is an identifier; the bilingual name carries Arabic
    public void AMalformedCodeIsRejected(string code) =>
        DimensionCodes.IsValidCode(code).Should().BeFalse();

    [Fact]
    public void ACodeIsRejectedWhenItIsLongerThanFiftyCharacters()
    {
        DimensionCodes.IsValidCode(new string('a', 50)).Should().BeTrue();
        DimensionCodes.IsValidCode(new string('a', 51)).Should().BeFalse();
    }

    [Fact]
    public void ANullCodeIsRejectedRatherThanThrowing() =>
        DimensionCodes.IsValidCode(null).Should().BeFalse();

    [Theory]
    [InlineData("dept", "Dept")]
    [InlineData("cost-centre", "CostCentre")]
    [InlineData("cost_centre", "CostCentre")]
    [InlineData("business-unit", "BusinessUnit")]
    [InlineData("Section", "Section")]
    public void AContentTypeNameIsDerivedFromTheCode(string code, string expected) =>
        DimensionCodes.ToContentTypeName(code).Should().Be(expected);

    [Fact]
    public void AnAbbreviationKeepsItsCapitalisation() =>
        // Customers use abbreviations as codes. Lower casing the tail would turn HR-BU into
        // HrBu, which the person who created it would not recognise as theirs.
        DimensionCodes.ToContentTypeName("HR-BU").Should().Be("HRBU");

    [Fact]
    public void TwoCodesCanDeriveTheSameContentTypeName()
    {
        // Not a defect, a known consequence, and not a far-fetched one: a customer choosing a
        // separator for a two-word code could plausibly pick either. This is why
        // DimensionTypeService checks the derived name against the tenant's existing content
        // types before it writes anything, rather than trusting code uniqueness to imply it.
        DimensionCodes.ToContentTypeName("cost-centre")
            .Should().Be(DimensionCodes.ToContentTypeName("cost_centre"));
    }

    [Fact]
    public void RemovingASeparatorDoesNotCollideWithTheSeparatedForm() =>
        // The derivation upper cases the first letter of each segment and leaves the rest alone,
        // so 'costcentre' is one segment and stays Costcentre rather than becoming CostCentre.
        DimensionCodes.ToContentTypeName("costcentre")
            .Should().NotBe(DimensionCodes.ToContentTypeName("cost-centre"));

    [Theory]
    [InlineData("HeadCount")]
    [InlineData("a")]
    public void AWellFormedAttributeNameIsAccepted(string name) =>
        DimensionCodes.IsValidAttributeName(name).Should().BeTrue();

    [Theory]
    [InlineData("head-count")]   // a hyphen is not addressable in Liquid or GraphQL
    [InlineData("head count")]
    [InlineData("2HeadCount")]
    [InlineData("")]
    public void AMalformedAttributeNameIsRejected(string name) =>
        DimensionCodes.IsValidAttributeName(name).Should().BeFalse();

    [Fact]
    public void EveryAttributeKindHasAFieldTypeBehindIt()
    {
        foreach (var kind in DimensionAttributeKinds.All)
        {
            DimensionAttributeKinds.FieldTypeNameFor(kind).Should().NotBeNullOrWhiteSpace(
                "the attribute kind {0} has to map to a field type the content definition can name",
                kind);
        }
    }

    [Fact]
    public void TheBilingualKindUsesThePlatformsOwnField() =>
        // ADR-0003 put BilingualTextField in WorkMate.Platform. If it is ever renamed, the
        // generated content definitions would name a field type nothing registers and records
        // would lose their labels silently.
        DimensionAttributeKinds.FieldTypeNameFor(DimensionAttributeKind.BilingualText)
            .Should().Be(nameof(WorkMate.Platform.Fields.BilingualTextField));
}
