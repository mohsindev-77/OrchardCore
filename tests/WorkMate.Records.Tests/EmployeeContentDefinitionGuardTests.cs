using FluentAssertions;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// The policy on what may be added to the employee record, and where.
/// </summary>
/// <remarks>
/// This is the rule prompt 4 session B's form-definition compiler will refuse against, so it is
/// worth pinning now, while it is cheap, rather than when something depends on it.
/// </remarks>
public sealed class EmployeeContentDefinitionGuardTests
{
    [Fact]
    public void TheFixedCoreTakesNoAdditionsAtAll() =>
        EmployeeContentDefinitionGuard.Check(EmployeeFieldNames.PartName, "FavouriteColour")
            !.Kind.Should().Be(EmployeeDefinitionViolationKind.CoreIsClosed);

    /// <summary>
    /// A section cannot take a core field's name.
    /// </summary>
    /// <remarks>
    /// A section field called <c>JoinDate</c> would read as the employee's join date on every screen
    /// and in every Liquid template while holding something else entirely — and the template that
    /// reads it would not error, it would just show the wrong date.
    /// </remarks>
    [Theory]
    [InlineData("JoinDate")]
    [InlineData("EmployeeCode")]
    [InlineData("Status")]
    [InlineData("joindate")]
    public void ASectionCannotTakeAReservedName(string fieldName) =>
        EmployeeContentDefinitionGuard.Check(EmployeeSections.DocumentType, fieldName)
            !.Kind.Should().Be(EmployeeDefinitionViolationKind.ReservedName);

    /// <summary>
    /// The form designer cannot reintroduce the department field by another name.
    /// </summary>
    /// <remarks>
    /// Architecture section 2's one rule that follows from the model. A tenant adding
    /// <c>DepartmentCode</c> to a section would have built exactly the field that decision rules
    /// out, with none of the dating that makes a transfer answerable — and it would look like it
    /// worked, which is the problem.
    /// </remarks>
    [Theory]
    [InlineData("Department")]
    [InlineData("DepartmentCode")]
    [InlineData("CostCentre")]
    [InlineData("CostCenterCode")]
    [InlineData("StructureId")]
    public void ASectionCannotReintroducePlacementAsAField(string fieldName) =>
        EmployeeContentDefinitionGuard.Check(EmployeeSections.JobDetailType, fieldName)
            !.Kind.Should().Be(EmployeeDefinitionViolationKind.PlacementBelongsInTheDimensionEngine);

    [Theory]
    [InlineData("DocumentNumber")]
    [InlineData("ExpiresOn")]
    [InlineData("Iban")]
    [InlineData("GradeCode")]
    public void AnOrdinarySectionFieldIsPermitted(string fieldName) =>
        EmployeeContentDefinitionGuard.Check(EmployeeSections.DocumentType, fieldName).Should().BeNull();

    [Fact]
    public void EnsureMayDefineThrowsWithASentenceThatNamesBothThePartAndTheField()
    {
        var act = () => EmployeeContentDefinitionGuard.EnsureMayDefine(
            EmployeeSections.DependantType, "CostCentre");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{EmployeeSections.DependantType}*CostCentre*");
    }

    /// <summary>
    /// Every field this module's own migration declares is permitted by its own rule.
    /// </summary>
    /// <remarks>
    /// The migration routes each one through <c>EnsureMayDefine</c> as it declares it, so this
    /// would already fail at tenant setup — but it would fail there as a 500 during setup, which is
    /// a long way from the person who added the field. Failing in the unit suite puts it in front
    /// of them.
    /// </remarks>
    [Fact]
    public void NoSectionThisModuleShipsDeclaresAForbiddenFieldName()
    {
        foreach (var (section, itemType) in EmployeeSections.All)
        {
            EmployeeContentDefinitionGuard.Check(itemType, section).Should().BeNull(
                $"the '{section}' section's own name must not collide with the core");
        }
    }
}
