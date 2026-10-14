using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The lookup that gives an existing tenant's shipped dimensions their plurals — decision D24,
/// applied by <c>Migrations.UpdateFrom11Async</c>.
/// </summary>
/// <remarks>
/// <b>Held to being a lookup and not a rule.</b> ADR-0024's position is that a plural is authored
/// and never derived, because deriving one produced <em>Branchs</em>. These tests exist to keep this
/// table on the right side of that line: it answers for the names the product ships and says nothing
/// at all about anything else, which is what lets the menu phrase itself around a singular rather
/// than invent a word.
/// </remarks>
public sealed class StandardDimensionPluralsTests
{
    [Theory]
    [InlineData("Business unit", "Business units")]
    [InlineData("Division", "Divisions")]
    [InlineData("Department", "Departments")]
    [InlineData("Section", "Sections")]
    [InlineData("Team", "Teams")]
    [InlineData("Region", "Regions")]
    [InlineData("Branch", "Branches")]
    [InlineData("Project", "Projects")]
    [InlineData("Cost centre", "Cost centres")]
    [InlineData("Position", "Positions")]
    [InlineData("Grade step", "Grade steps")]
    public void TheShippedNamesHaveTheirPlurals(string name, string expected) =>
        StandardDimensionPlurals.For(new BilingualText(name, string.Empty)).En.Should().Be(expected);

    /// <summary>
    /// <em>Branch</em> is the assertion, because <em>Branchs</em> is what the derived plural gave.
    /// </summary>
    [Fact]
    public void BranchPluralisesCorrectlyAndNothingAppendsAnS() =>
        StandardDimensionPlurals.For(new BilingualText("Branch", "فرع")).Should().BeEquivalentTo(
            new BilingualText("Branches", "الفروع"));

    /// <summary>A name the product does not ship gets nothing, in either language.</summary>
    /// <remarks>
    /// The whole point. There is no rule here to apply to <em>Workstream</em>, so it gets no plural
    /// and the menu says <em>All Workstream units</em> rather than guessing a word.
    /// </remarks>
    [Fact]
    public void AnUnknownNameGetsNoPluralAtAll()
    {
        var plural = StandardDimensionPlurals.For(new BilingualText("Workstream", "مسار العمل"));

        plural.En.Should().BeEmpty();
        plural.Ar.Should().BeEmpty();
    }

    /// <summary>
    /// The halves are looked up independently, English by the English name and Arabic by the Arabic.
    /// </summary>
    /// <remarks>
    /// <b>Two tenants can both be right about their own Division.</b> The reference recipes use
    /// قطاع and قسم for the same English word, and keying the Arabic plural off the English name
    /// would hand one of them the other's noun.
    /// </remarks>
    [Fact]
    public void EachHalfIsLookedUpByItsOwnName()
    {
        StandardDimensionPlurals.For(new BilingualText("Division", "قطاع")).Ar.Should().Be("القطاعات");
        StandardDimensionPlurals.For(new BilingualText("Division", "قسم")).Ar.Should().Be("الأقسام");

        // An English name it knows with an Arabic name it does not: the English half is still filled.
        var mixed = StandardDimensionPlurals.For(new BilingualText("Division", "شيء آخر"));

        mixed.En.Should().Be("Divisions");
        mixed.Ar.Should().BeEmpty();
    }

    [Theory]
    [InlineData("department")]
    [InlineData("DEPARTMENT")]
    [InlineData("  Department  ")]
    public void TheEnglishNameIsMatchedCaseInsensitivelyAndTrimmed(string name) =>
        StandardDimensionPlurals.For(new BilingualText(name, string.Empty)).En.Should().Be("Departments");

    /// <summary>
    /// What counts as a plural nobody authored: blank, or the singular repeated.
    /// </summary>
    /// <remarks>
    /// <b>The second is the one that matters.</b> <c>Migrations.UpdateFrom10Async</c> cleared
    /// session D's guessed plurals by writing the <em>singular</em> — a deliberate, visible
    /// placeholder — so every tenant that has upgraded holds "Department" rather than "". A rule
    /// that only filled blanks would have reached none of them, which is every tenant.
    /// </remarks>
    [Theory]
    [InlineData(null, "Department")]
    [InlineData("", "Department")]
    [InlineData("   ", "Department")]
    [InlineData("Department", "Department")]
    [InlineData("  Department ", "Department")]
    public void ABlankPluralOrOneThatRepeatsTheSingularMayBeFilled(string? plural, string singular) =>
        StandardDimensionPlurals.MayBeFilled(plural, singular).Should().BeTrue();

    /// <summary>Anything a person actually chose is left alone.</summary>
    [Theory]
    [InlineData("Departments", "Department")]
    [InlineData("Depts", "Department")]
    public void AnAuthoredPluralIsNeverFilled(string plural, string singular) =>
        StandardDimensionPlurals.MayBeFilled(plural, singular).Should().BeFalse();
}
