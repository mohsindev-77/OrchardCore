using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.ContentManagement.Metadata;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Creating a dimension type and then records of it in one unit of work.
/// </summary>
/// <remarks>
/// This is the shape prompt 3's recipe import uses, and it did not work. Orchard Core 3.0.1's
/// <c>IContentManager.NewAsync</c> reads the <em>cached</em> content definition, which does not
/// refresh until the scope commits, so for a type created moments earlier it read nothing and
/// returned an item with no parts welded at all. Nothing threw: the caller got an item that
/// looked usable, silently dropped every field it was given, and failed validation with "this
/// content type is not a dimension type" — a message pointing at the wrong thing entirely.
///
/// ADR-0006 records the constraint and the rule that follows from it. These tests are what hold
/// the fix.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class SameScopeCreationTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public SameScopeCreationTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2026, 1, 1);

    private static readonly string[] ThreeCodes = ["SCOPE-1", "SCOPE-2", "SCOPE-3"];

    [Fact]
    public async Task TheCachedDefinitionIsStillBlindToATypeCreatedInThisScope() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // Not a test of our code: a test of the Orchard behaviour the fix exists for. If a
            // future version makes the cached read see a same-scope write, this fails and the
            // repair in DimensionRecordHandler.ActivatingAsync can be deleted. That is a much
            // better way to find out than discovering the repair is dead weight years later.
            var created = await services.GetRequiredService<IDimensionTypeService>().CreateAsync(
                "scope-canary",
                new BilingualText("Canary", "كناري"),
                [],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue();

            var definitions = services.GetRequiredService<IContentDefinitionManager>();
            var contentType = created.Value!.ContentTypeName;

            (await definitions.GetTypeDefinitionAsync(contentType)).Should().BeNull(
                "the cached content definition does not refresh until the scope commits");

            (await definitions.LoadTypeDefinitionAsync(contentType)).Should().NotBeNull(
                "the uncached read sees it, which is why every write path in this module uses Load");
        });

    [Fact]
    public async Task ATypeAndItsRecordsCanBeCreatedInOneScope() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            var created = await types.CreateAsync(
                "scope-dept",
                new BilingualText("Department", "الإدارة"),
                [],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            // Three records of a type that did not exist when this scope opened. This is what a
            // recipe step does, one row after another.
            foreach (var code in ThreeCodes)
            {
                var record = await records.CreateAsync(
                    created.Value!.DimensionTypeId,
                    code,
                    new BilingualText(code, code + "-ar"),
                    new EffectiveRange(Opened, null));

                record.Succeeded.Should().BeTrue(
                    "a record of a type created in this same scope must save: {0}",
                    string.Join("; ", record.Errors.Select(error => error.Message.Value)));
            }

            var rows = await services.GetRequiredService<ISession>()
                .QueryIndex<DimensionRecordPartIndex>(index => index.ContentType == created.Value!.ContentTypeName)
                .ListAsync();

            rows.Select(row => row.Code).Should().BeEquivalentTo(ThreeCodes);

            rows.Should().AllSatisfy(row =>
                row.DimensionTypeId.Should().Be(
                    created.Value!.DimensionTypeId,
                    "the dimension type must be stamped on even though no part handler ran"));
        });

    [Fact]
    public async Task ACustomAttributeFieldIsWeldedOnARecordCreatedInTheSameScope() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The subtle half of the bug. Welding only DimensionRecordPart would make the
            // record save and look right, while every field the dimension type's own attribute
            // schema declares was quietly discarded — the part carrying them was never welded.
            var types = services.GetRequiredService<IDimensionTypeService>();
            var manager = services.GetRequiredService<IContentManager>();

            var created = await types.CreateAsync(
                "scope-attributes",
                new BilingualText("With attributes", "بسمات"),
                [
                    new DimensionAttributeDefinition(
                        "GlPrefix", new BilingualText("GL prefix", "بادئة الحساب"), DimensionAttributeKind.Text),
                    new DimensionAttributeDefinition(
                        "HeadCount", new BilingualText("Head count", "عدد الموظفين"), DimensionAttributeKind.Number),
                ],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            var contentType = created.Value!.ContentTypeName;
            var item = await manager.NewAsync(contentType);

            // Every part the definition declares, not only the one this module owns.
            item.Has(nameof(DimensionRecordPart)).Should().BeTrue("the standard part");
            item.Has("TitlePart").Should().BeTrue("the generated title");

            item.Has(contentType).Should().BeTrue(
                "the part named for the content type carries the attribute schema's fields, and "
                + "a record without it accepts values for them and stores none");
        });

    [Fact]
    public async Task ARecordCreatedThroughTheContentManagerInTheSameScopeIsStampedAndValid() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The path a recipe's generic Content step takes: straight through IContentManager,
            // with no help from IDimensionService.
            var types = services.GetRequiredService<IDimensionTypeService>();
            var manager = services.GetRequiredService<IContentManager>();

            var created = await types.CreateAsync(
                "scope-generic",
                new BilingualText("Generic", "عام"),
                [],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue();

            var item = await manager.NewAsync(created.Value!.ContentTypeName);

            item.Alter<DimensionRecordPart>(part =>
            {
                part.Code = "GENERIC-1";
                part.NameEn = "Generic";
                part.NameAr = "عام";
                part.EffectiveFrom = Opened;
            });

            await manager.UpdateAsync(item);

            var validated = await manager.ValidateAsync(item);

            validated.Succeeded.Should().BeTrue(
                string.Join("; ", validated.Errors.Select(error => error.ErrorMessage)));

            item.Get<DimensionRecordPart>(nameof(DimensionRecordPart))!.DimensionTypeId
                .Should().Be(created.Value.DimensionTypeId);
        });

    [Fact]
    public async Task ARecordOfATypeThatDoesNotExistIsStillRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The repair must not turn the guard into a rubber stamp. A part welded by hand onto
            // a content type this module never generated is still not a dimension record.
            var manager = services.GetRequiredService<IContentManager>();
            var item = await manager.NewAsync("Site");

            item.Alter<DimensionRecordPart>(part =>
            {
                part.Code = "IMPOSTER-1";
                part.NameEn = "Imposter";
                part.NameAr = "دخيل";
                part.EffectiveFrom = Opened;
            });

            await manager.UpdateAsync(item);

            (await manager.ValidateAsync(item)).Succeeded.Should().BeFalse(
                "'Site' is not a dimension type, so it cannot carry a dimension record");
        });
}
