using FluentAssertions;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// The employee code's format and comparison rules.
/// </summary>
/// <remarks>
/// The code is the natural key: it is what <c>employees</c>, <c>employee-assignments</c> and
/// <c>unit-heads</c> name an employee by, and what an export writes instead of a generated id. A
/// rule that was too strict would refuse the numbers a customer already uses and force them to
/// renumber their whole workforce to come on board; one that was too loose would let a space or a
/// trailing blank produce two records for one person.
/// </remarks>
public sealed class EmployeeCodeTests
{
    [Theory]
    [InlineData("emp-00412")]
    [InlineData("EMP-0042")]
    [InlineData("00412")]       // a leading digit, which a dimension code may not have
    [InlineData("1001")]
    [InlineData("HR/0042")]
    [InlineData("12.345")]
    [InlineData("e")]
    [InlineData("staff_7")]
    public void AWellFormedCodeIsAccepted(string code) =>
        EmployeeCodes.IsValid(code).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("emp 0042")]    // a space makes half the consumers trim it and half not
    [InlineData("-emp")]        // a leading separator is a typo, not a code
    [InlineData("/emp")]
    [InlineData(".emp")]
    [InlineData("emp#1")]
    [InlineData("موظف")]        // the code is an identifier; the bilingual name carries Arabic
    public void AMalformedCodeIsRejected(string code) =>
        EmployeeCodes.IsValid(code).Should().BeFalse();

    /// <summary>
    /// The rule is deliberately looser than the dimension engine's, and this is the difference that
    /// matters.
    /// </summary>
    /// <remarks>
    /// <c>DimensionCodes</c> requires a leading letter because a dimension code becomes a content
    /// type name and a type name cannot start with a digit. An employee code becomes nothing, and
    /// most HR departments and every payroll file already issue plain numbers. Carrying the stricter
    /// rule across would have refused them.
    /// </remarks>
    [Fact]
    public void ACodeMayStartWithADigitUnlikeADimensionCode()
    {
        EmployeeCodes.IsValid("00412").Should().BeTrue();
        WorkMate.Dimensions.Services.DimensionCodes.IsValidCode("00412").Should().BeFalse();
    }

    [Fact]
    public void ACodeIsRejectedWhenItIsLongerThanTheIndexedColumn()
    {
        EmployeeCodes.IsValid(new string('e', EmployeeCodes.MaxLength)).Should().BeTrue();
        EmployeeCodes.IsValid(new string('e', EmployeeCodes.MaxLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void SurroundingWhitespaceIsNotPartOfACode() =>
        EmployeeCodes.Normalise("  emp-1  ").Should().Be("emp-1");

    /// <summary>
    /// Case is kept on the way in and ignored on the way out.
    /// </summary>
    /// <remarks>
    /// A customer whose codes read <c>EMP-0042</c> sees them that way on every screen. An import
    /// that shouts the same code still finds the same person, so it cannot create a second record
    /// for them.
    /// </remarks>
    [Fact]
    public void TwoSpellingsOfOneCodeNameTheSameEmployee()
    {
        EmployeeCodes.Same("EMP-0042", "emp-0042").Should().BeTrue();
        EmployeeCodes.Same(" emp-0042 ", "EMP-0042").Should().BeTrue();
        EmployeeCodes.Normalise("EMP-0042").Should().Be("EMP-0042", "the customer's own capitalisation survives");
    }

    [Fact]
    public void TwoDifferentCodesAreNotTheSameEmployee() =>
        EmployeeCodes.Same("emp-0042", "emp-0043").Should().BeFalse();

    /// <summary>
    /// Comparison is ordinal, so no culture's casing rules get a say.
    /// </summary>
    /// <remarks>
    /// The Turkish dotless i is the standard example: under <c>tr-TR</c>, an
    /// invariant-culture-insensitive comparison can decide that <c>I</c> and <c>i</c> are different
    /// letters, which would make one tenant's uniqueness check disagree with another's depending on
    /// the server's culture. These are identifiers, not words.
    /// </remarks>
    [Fact]
    public void ComparisonDoesNotDependOnTheCurrentCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            EmployeeCodes.Same("EMPI-1", "empi-1").Should().BeTrue();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
