using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Flows.Models;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// What the base recipe actually produces for the employee record, read off a real tenant.
/// </summary>
/// <remarks>
/// Specification section 5 requires the fixed core to be beyond an administrator's reach, and the
/// brief asks for that to be impossible rather than discouraged. The policy is unit-tested in
/// <c>EmployeeContentDefinitionGuardTests</c>; this is the other half, which is whether the tenant
/// a customer actually gets matches it.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeContentDefinitionTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public EmployeeContentDefinitionTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// The fixed core carries no content fields at all.
    /// </summary>
    /// <remarks>
    /// Its eleven values are properties on <c>EmployeePart</c>, not fields on a definition, which is
    /// what makes them unreachable from the content-type editor in the first place. A field
    /// appearing here would mean something had started adding to the core through the definition —
    /// the exact route the guard exists to close.
    /// </remarks>
    [Fact]
    public async Task TheFixedCoreCarriesNoDefinedFields() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();
            var part = await definitions.GetPartDefinitionAsync(EmployeeFieldNames.PartName);

            part.Should().NotBeNull("the base recipe creates EmployeePart");
            part!.Fields.Should().BeEmpty(
                "the core's values are properties on EmployeePart, so there is nothing on the "
                + "definition for the content-type editor to extend");
        });

    /// <summary>
    /// <c>EmployeePart</c> is not attachable, which is what keeps it out of "Add parts".
    /// </summary>
    /// <remarks>
    /// The half of "impossible, not discouraged" that Orchard itself enforces: an administrator
    /// cannot weld the core onto another content type, and cannot take it off <c>Employee</c>.
    /// </remarks>
    [Fact]
    public async Task TheFixedCoreCannotBeAttachedToAnythingElse() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();
            var part = await definitions.GetPartDefinitionAsync(EmployeeFieldNames.PartName);

            part!.GetSettings<ContentPartSettings>().Attachable.Should().BeFalse();
        });

    [Fact]
    public async Task TheEmployeeTypeCarriesTheCoreAndTheSixStandardSections() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();
            var type = await definitions.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

            type.Should().NotBeNull("the base recipe creates the Employee content type");

            var parts = type!.Parts.Select(part => part.Name).ToList();

            parts.Should().Contain(EmployeeFieldNames.PartName);

            foreach (var (section, _) in EmployeeSections.All)
            {
                parts.Should().Contain(section, $"'{section}' is one of the six standard sections");
            }
        });

    /// <summary>
    /// Each section is a bag restricted to its own item type.
    /// </summary>
    /// <remarks>
    /// Without the restriction a bag offers every creatable type in the tenant, so a "Bank details"
    /// section would invite somebody to add a blog post to it — and would accept one.
    /// </remarks>
    [Fact]
    public async Task EachSectionAcceptsOnlyItsOwnKindOfItem() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();
            var type = await definitions.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

            foreach (var (section, itemType) in EmployeeSections.All)
            {
                var part = type!.Parts.Single(part => part.Name == section);

                part.PartDefinition.Name.Should().Be(nameof(BagPart));
                part.GetSettings<BagPartSettings>().ContainedContentTypes
                    .Should().BeEquivalentTo([itemType]);
            }
        });

    /// <summary>
    /// No section field takes a name the fixed core owns, or reintroduces placement as a field.
    /// </summary>
    /// <remarks>
    /// The migration routes each field through the guard as it declares it, so this would already
    /// have failed at tenant setup. Asserting it on the built tenant is the backstop for the other
    /// route in: a section definition arriving from a recipe or an import rather than from this
    /// module's own migration.
    /// </remarks>
    [Fact]
    public async Task NoSectionFieldTakesAReservedOrPlacementName() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            foreach (var (_, itemType) in EmployeeSections.All)
            {
                var part = await definitions.GetPartDefinitionAsync(itemType);

                part.Should().NotBeNull($"the base recipe creates {itemType}");

                foreach (var field in part!.Fields)
                {
                    EmployeeContentDefinitionGuard.Check(itemType, field.Name).Should().BeNull(
                        $"'{itemType}.{field.Name}' must not collide with the employee record's core "
                        + "or reintroduce placement as a field");
                }
            }
        });

    /// <summary>
    /// A section item type is never creatable or listable on its own.
    /// </summary>
    /// <remarks>
    /// A bank account with no employee is not a thing, and a listable one would appear in the
    /// content picker of every form in the tenant — including, in session B, in form definitions an
    /// administrator writes.
    /// </remarks>
    [Fact]
    public async Task ASectionItemCannotExistOnItsOwn() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            foreach (var (_, itemType) in EmployeeSections.All)
            {
                var type = await definitions.GetTypeDefinitionAsync(itemType);
                var settings = type!.GetSettings<ContentTypeSettings>();

                settings.Creatable.Should().BeFalse($"{itemType} belongs to an employee");
                settings.Listable.Should().BeFalse();
                settings.Stereotype.Should().Be(EmployeeSections.Stereotype);
            }
        });

    /// <summary>
    /// The employee record is not draftable.
    /// </summary>
    /// <remarks>
    /// A half-saved employee is not a thing, and a draft would be invisible to the employee index —
    /// and so to the uniqueness check, to every headcount and to the picker — while looking entirely
    /// real in the editor.
    /// </remarks>
    [Fact]
    public async Task TheEmployeeTypeIsNotDraftable() =>
        await _tenant.InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();
            var type = await definitions.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

            type!.GetSettings<ContentTypeSettings>().Draftable.Should().BeFalse();
        });
}
