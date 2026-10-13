using System.Reflection;
using FluentAssertions;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The shape of a dimension record, and the one thing that must never appear on it.
/// </summary>
public sealed class DimensionRecordPartTests
{
    /// <remarks>
    /// Eleven in specification section 4, plus the two FRS v2.5 §4.1 adds: a dimension's list and
    /// tree carry an Alias column, so the record has to carry one, bilingual like every other name
    /// field on the platform (CLAUDE.md rule 7).
    /// </remarks>
    [Fact]
    public void ThePartCarriesTheStandardFieldsTheSpecificationAndTheFrsName()
    {
        var declared = typeof(DimensionRecordPart)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite)
            .Select(property => property.Name);

        declared.Should().BeEquivalentTo(
        [
            "Code",
            "NameEn",
            "NameAr",
            "AliasEn",
            "AliasAr",
            "DimensionTypeId",
            "EffectiveFrom",
            "EffectiveTo",
            "IsActive",
            "SortOrder",
            "CostCentreCode",
            "GlAccountRef",
            "HeadEmployeeId",
        ],
            "specification section 4 lists eleven and FRS v2.5 adds the alias");
    }

    /// <summary>
    /// The alias is not dated, and nothing about it pretends to be.
    /// </summary>
    /// <remarks>
    /// Written as a test rather than left to the doc comment because the way this decision gets
    /// undone is not somebody adding <c>AliasHistory</c> — nobody would — it is somebody adding
    /// <c>AliasEffectiveFrom</c> in good faith while making one screen consistent with the name
    /// field beside it. The name has a dated history because "what was this called last March"
    /// changes what a historical report prints; nothing resolves an alias as at a date, and a
    /// second parallel history to answer a question nobody asks is the cost of getting this wrong.
    /// </remarks>
    [Fact]
    public void TheAliasCarriesNoDateOfItsOwn()
    {
        var aliasFields = typeof(DimensionRecordPart)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => name.Contains("Alias", StringComparison.OrdinalIgnoreCase));

        aliasFields.Should().BeEquivalentTo(["AliasEn", "AliasAr"]);
    }

    [Fact]
    public void ThePartHasNoParentAndNoStructureField()
    {
        // The rule the whole design rests on. A record sits on several axes at once, so a single
        // parent field would collapse them into one tree — decision 4 of the architecture. The
        // employee record is held to the same rule for the same reason, and both are the kind of
        // field someone adds in good faith while fixing something else.
        var names = typeof(DimensionRecordPart)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        names.Should().NotContain(name =>
            name.Contains("Parent", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Structure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheStandardFieldNamesTheTypeServiceGuardsMatchThePartItself()
    {
        // DimensionTypeService refuses an attribute schema that reuses a standard field name,
        // from a hard-coded list it can check before the part exists. If the part gains a field
        // and the list does not, a customer could declare an attribute that collides with it and
        // the editor would show two inputs with the same name.
        var onThePart = typeof(DimensionRecordPart)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite)
            .Select(property => property.Name);

        DimensionTypeService.StandardFieldNames.Should().BeEquivalentTo(onThePart);
    }

    [Fact]
    public void ARecordIsEffectiveOnItsFirstAndLastDay()
    {
        var part = new DimensionRecordPart
        {
            EffectiveFrom = new DateOnly(2026, 3, 1),
            EffectiveTo = new DateOnly(2026, 3, 15),
        };

        part.IsEffectiveOn(new DateOnly(2026, 2, 28)).Should().BeFalse();
        part.IsEffectiveOn(new DateOnly(2026, 3, 1)).Should().BeTrue();
        part.IsEffectiveOn(new DateOnly(2026, 3, 15)).Should().BeTrue("the end is inclusive");
        part.IsEffectiveOn(new DateOnly(2026, 3, 16)).Should().BeFalse();
    }

    [Fact]
    public void AnOpenEndedRecordStaysEffective()
    {
        var part = new DimensionRecordPart { EffectiveFrom = new DateOnly(2026, 3, 16) };

        part.IsEffectiveOn(new DateOnly(2099, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void ThePartIsNotSealed() =>
        // Orchard builds parts and their editor shapes through Castle DynamicProxy, which
        // subclasses them. WorkMate.Platform hit the same constraint with its field type.
        typeof(DimensionRecordPart).IsSealed.Should().BeFalse();
}
