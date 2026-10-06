using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.AuditTrail.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// What the dimension engine's configuration layer does on a real tenant.
///
/// The unit suite checks the rules in isolation. It cannot check the two things most likely to
/// break in a way nobody sees until a customer's setup fails: whether the migration's tables
/// actually get created by the pinned YesSql, and whether the content type the service generates
/// is the one specification section 4 describes.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionConfigurationTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionConfigurationTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Runs <paramref name="work"/> inside the tenant, on the platform's own authority.
    /// </summary>
    /// <remarks>
    /// A shell scope opened outside a request has no user, and the dimension services refuse a
    /// caller they cannot authorise. That is the rule, not an obstacle to work around: these
    /// tests stand in for a recipe step or a background task, and both of those have to say so
    /// explicitly. This is the same two lines a recipe step will write.
    /// </remarks>
    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    [Fact]
    public async Task AScopeWithNoUserAndNoSystemAuthorityIsRefused() =>
        await _tenant.InTenantAsync(async services =>
        {
            // Deliberately not InTenantAsSystemAsync. This is a background job that forgot to
            // declare itself, or any other code reaching the service from a scope with no
            // request behind it. It gets nothing.
            //
            // The unit suite pins this rule against a stub. This pins it against the real
            // tenant, the real authorisation service and the real DI wiring, because the rule is
            // only worth anything if it survives all three.
            var types = services.GetRequiredService<IDimensionTypeService>();

            var refused = await types.CreateAsync(
                "smuggled",
                new BilingualText("Smuggled", "مهرب"),
                [],
                allowsSelfNesting: false);

            refused.IsAuthorised.Should().BeFalse(
                "no user and no system scope is refused; the absence of a user is not authority");

            refused.Succeeded.Should().BeFalse();

            (await types.GetByCodeAsync("smuggled")).Should().BeNull("nothing was written");
        });

    [Fact]
    public async Task TheModulesAuditTrailCategoryIsRegisteredOnTheTenant() =>
        await _tenant.InTenantAsync(async services =>
        {
            var manager = services.GetRequiredService<IAuditTrailManager>();

            var category = manager.DescribeCategory(DimensionAuditTrail.Category);

            category.Events.Keys.Should().Contain(
            [
                DimensionAuditTrail.DimensionTypeChanged,
                DimensionAuditTrail.StructureChanged,
                DimensionAuditTrail.ContentDefinitionChanged,
                DimensionAuditTrail.DimensionRecordChanged,
                DimensionAuditTrail.MoveCancelled,
            ],
                "an event that is recorded but not described is filed under a category the admin "
                + "screen will not offer as a filter");

            await Task.CompletedTask;
        });

    [Fact]
    public async Task ADimensionTypeCanBeCreatedAndItsTablesHoldIt() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            var created = await types.CreateAsync(
                "it-department",
                new BilingualText("Department", "الإدارة"),
                [
                    new DimensionAttributeDefinition(
                        "HeadCount",
                        new BilingualText("Head count", "عدد الموظفين"),
                        DimensionAttributeKind.Number),
                ],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(
                "the migration's tables must exist and accept a write: {0}",
                string.Join("; ", created.Errors.Select(error => $"{error.Rule} {error.Message.Value}")));

            // Read it back through the index rather than from the returned document, which is
            // what proves the table and its provider are wired up.
            var byCode = await types.GetByCodeAsync("it-department");

            byCode.Should().NotBeNull();
            byCode!.ContentTypeName.Should().Be("ItDepartment");

            var byContentType = await types.GetByContentTypeAsync("ItDepartment");

            byContentType.Should().NotBeNull();
            byContentType!.DimensionTypeId.Should().Be(created.Value!.DimensionTypeId);
        });

    [Fact]
    public async Task TheGeneratedContentTypeIsTheOneTheSpecificationDescribes() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var definitions = services
                .GetRequiredService<OrchardCore.ContentManagement.Metadata.IContentDefinitionManager>();

            var created = await types.CreateAsync(
                "cost-centre",
                new BilingualText("Cost centre", "مركز التكلفة"),
                [
                    new DimensionAttributeDefinition(
                        "GlPrefix",
                        new BilingualText("GL prefix", "بادئة الحساب"),
                        DimensionAttributeKind.Text),
                ],
                allowsSelfNesting: true);

            created.Succeeded.Should().BeTrue();

            var definition = await definitions.GetTypeDefinitionAsync("CostCentre");

            definition.Should().NotBeNull("the content type is named for the code");

            var settings = definition!
                .GetSettings<OrchardCore.ContentManagement.Metadata.Settings.ContentTypeSettings>();

            settings.Creatable.Should().BeTrue();
            settings.Listable.Should().BeTrue();
            settings.Securable.Should().BeTrue();
            settings.Draftable.Should().BeFalse("section 4 says not draftable");

            var parts = definition.Parts.Select(part => part.PartDefinition.Name).ToList();

            parts.Should().Contain("DimensionRecordPart");
            parts.Should().Contain("TitlePart");
            parts.Should().Contain("CostCentre", "the type's own fields go on a part named for it");

            // The attribute schema's field, on the type's own part and of the field type the
            // kind maps to.
            var ownPart = definition.Parts.Single(part => part.PartDefinition.Name == "CostCentre");

            ownPart.PartDefinition.Fields.Should().ContainSingle()
                .Which.Name.Should().Be("GlPrefix");

            ownPart.PartDefinition.Fields.Single().FieldDefinition.Name.Should().Be("TextField");
        });

    [Fact]
    public async Task TheTitleIsGeneratedFromTheEnglishNameRatherThanTypedSeparately() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var definitions = services
                .GetRequiredService<OrchardCore.ContentManagement.Metadata.IContentDefinitionManager>();

            (await types.CreateAsync(
                "branch",
                new BilingualText("Branch", "الفرع"),
                [],
                allowsSelfNesting: false)).Succeeded.Should().BeTrue();

            var titlePart = (await definitions.GetTypeDefinitionAsync("Branch"))!
                .Parts.Single(part => part.PartDefinition.Name == "TitlePart");

            var settings = titlePart.GetSettings<OrchardCore.Title.Models.TitlePartSettings>();

            settings.Options.Should().Be(
                OrchardCore.Title.Models.TitlePartOptions.GeneratedDisabled,
                "an editable title would drift away from the name it is supposed to show");

            settings.Pattern.Should().Contain("DimensionRecordPart.NameEn");
        });

    [Fact]
    public async Task ASecondStructureCannotAlsoClaimToBeThePrimaryOrganisation() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();

            var division = await types.CreateAsync(
                "division",
                new BilingualText("Division", "القطاع"),
                [],
                allowsSelfNesting: false);

            division.Succeeded.Should().BeTrue();

            var first = await structures.CreateAsync(
                "organisation",
                new BilingualText("Organisation", "الهيكل التنظيمي"),
                [division.Value!.DimensionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: true);

            first.Succeeded.Should().BeTrue(
                string.Join("; ", first.Errors.Select(error => error.Message.Value)));

            var second = await structures.CreateAsync(
                "location",
                new BilingualText("Location", "الموقع"),
                [division.Value.DimensionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: true);

            second.Succeeded.Should().BeFalse();
            second.Errors.Should().ContainSingle()
                .Which.Rule.Should().Be(DimensionRule.SinglePrimaryOrganisation);

            (await structures.GetPrimaryOrganisationAsync())!.Code.Should().Be("organisation");
        });
}
