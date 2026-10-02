using System.Reflection;
using FluentAssertions;
using OrchardCore.Security.Permissions;
using WorkMate.Platform;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The module's permissions, and who starts with them.
/// </summary>
/// <remarks>
/// Written in the style WorkMate.Platform.Tests established: a permission name is data two
/// separate mechanisms have to agree on, and a stereotype naming a role nothing creates grants
/// nothing to nobody without saying so.
/// </remarks>
public sealed class PermissionTests
{
    private static readonly Permissions Subject = new();

    [Fact]
    public async Task TheSevenPermissionsTheSpecificationNamesAreDeclared()
    {
        var declared = (await Subject.GetPermissionsAsync()).Select(permission => permission.Name);

        declared.Should().BeEquivalentTo(
        [
            "ManageDimensionTypes",
            "ManageStructures",
            "ManageDimensionRecords",
            "MoveDimensionRecords",
            "MergeDimensionRecords",
            "ViewDimensionHistory",
            "AssignEmployees",
        ],
            "specification section 4 lists exactly these, and a permission checked in a service "
            + "but not declared here can never be granted to anyone");
    }

    [Fact]
    public void EveryDeclaredPermissionIsReachableAsAConstant()
    {
        // The services reference these by constant. A permission that exists only inside
        // GetPermissionsAsync could be granted but never checked.
        var constants = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<Permission>()
            .Select(permission => permission.Name);

        constants.Should().HaveCount(7);
    }

    [Fact]
    public void APermissionNameMatchesTheConstantThatDeclaresIt()
    {
        foreach (var field in typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is Permission permission)
            {
                permission.Name.Should().Be(
                    field.Name,
                    "a recipe or a role grant names the permission as a string, and a mismatch "
                    + "between the constant and the name is invisible until someone cannot reach a screen");
            }
        }
    }

    [Fact]
    public void EveryStereotypeNamesARoleTheBaseRecipeCreates()
    {
        foreach (var stereotype in Subject.GetDefaultStereotypes())
        {
            PlatformRoles.All.Should().Contain(
                stereotype.Name,
                "a stereotype for a role nothing creates grants nothing to nobody");
        }
    }

    [Fact]
    public async Task EveryStereotypeGrantsOnlyPermissionsThisModuleDeclares()
    {
        var declared = (await Subject.GetPermissionsAsync()).Select(permission => permission.Name).ToList();

        foreach (var stereotype in Subject.GetDefaultStereotypes())
        {
            foreach (var permission in stereotype.Permissions ?? [])
            {
                declared.Should().Contain(permission.Name);
            }
        }
    }

    [Fact]
    public void ReshapingTheOrganisationIsNotGrantedToTheHrAdministratorByDefault()
    {
        // Specification section 4 splits move and merge out from managing records precisely so a
        // customer can decide who holds them. Granting them by default would make that split
        // decorative. A customer who wants their HR administrator to reorganise the company can
        // grant it; nobody gets it without having decided to.
        var hrAdministrator = Subject.GetDefaultStereotypes()
            .Single(stereotype => stereotype.Name == PlatformRoles.HrAdministrator)
            .Permissions!
            .Select(permission => permission.Name);

        hrAdministrator.Should().NotContain(Permissions.MoveDimensionRecords.Name);
        hrAdministrator.Should().NotContain(Permissions.MergeDimensionRecords.Name);
        hrAdministrator.Should().NotContain(Permissions.ManageDimensionTypes.Name);
        hrAdministrator.Should().NotContain(Permissions.ManageStructures.Name);
    }

    [Fact]
    public void TheAuditorReadsAndChangesNothing()
    {
        var auditor = Subject.GetDefaultStereotypes()
            .Single(stereotype => stereotype.Name == PlatformRoles.Auditor)
            .Permissions!
            .Select(permission => permission.Name);

        auditor.Should().BeEquivalentTo([Permissions.ViewDimensionHistory.Name]);
    }

    [Fact]
    public void CreatingADimensionTypeIsMarkedSecurityCritical() =>
        // It creates a content type on the tenant, which changes the schema rather than the data.
        Permissions.ManageDimensionTypes.IsSecurityCritical.Should().BeTrue();
}
