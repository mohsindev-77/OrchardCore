using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// What happens when a dimension record is created the way an administrator, an API caller or
/// an importer would create one — through the content manager, not through a service that could
/// be trusted to have checked first.
/// </summary>
/// <remarks>
/// This is the test that matters most for the record layer. The generated types are creatable,
/// so Orchard's standard content screens, the REST API, GraphQL, a recipe and a bulk import can
/// all produce a record without the organisation designer ever being involved. The handler is
/// the only point all of those share, so these assertions are about the handler holding the
/// line — and they go through <c>IContentManager</c> precisely because that is the path that
/// bypasses everything else.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionRecordTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionRecordTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    /// <summary>Creates a dimension type and returns the content type generated for it.</summary>
    private static async Task<string> GivenADimensionTypeAsync(IServiceProvider services, string code)
    {
        var created = await services.GetRequiredService<IDimensionTypeService>().CreateAsync(
            code,
            new BilingualText(code, $"{code}-ar"),
            [],
            allowsSelfNesting: false);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.ContentTypeName;
    }

    private static async Task<ContentItem> NewRecordAsync(
        IServiceProvider services,
        string contentType,
        Action<DimensionRecordPart> configure)
    {
        var item = await services.GetRequiredService<IContentManager>().NewAsync(contentType);

        item.Alter<DimensionRecordPart>(configure);

        return item;
    }

    /// <summary>
    /// Creates a content item the way the content screens and the API do: update, validate,
    /// then create only if validation passed.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than using <c>UpdateValidateAndCreateAsync</c>, which is obsolete in
    /// Orchard Core 3.0.1 and marked for removal. The three steps are also what makes the
    /// assertion meaningful: a test that could not distinguish "rejected by the handler" from
    /// "threw on the way in" would pass for the wrong reason.
    /// </remarks>
    private static async Task<ContentValidateResult> CreateAsync(IServiceProvider services, ContentItem item)
    {
        var manager = services.GetRequiredService<IContentManager>();

        await manager.UpdateAsync(item);

        var validated = await manager.ValidateAsync(item);

        if (validated.Succeeded)
        {
            await manager.CreateAsync(item, VersionOptions.Published);
        }

        return validated;
    }

    [Fact]
    public async Task ARecordCreatedThroughTheContentManagerIsIndexed() =>
        await InTenantAsSystemAsync(async services =>
        {
            var contentType = await GivenADimensionTypeAsync(services, "rec-dept");

            var item = await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "DEPT-100";
                part.NameEn = "Finance";
                part.NameAr = "المالية";
                part.EffectiveFrom = new DateOnly(2026, 1, 1);
            });

            var result = await CreateAsync(services, item);

            result.Succeeded.Should().BeTrue(
                string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));

            var row = await services.GetRequiredService<ISession>()
                .QueryIndex<DimensionRecordPartIndex>(index => index.Code == "DEPT-100")
                .FirstOrDefaultAsync();

            row.Should().NotBeNull("the record must be resolvable without loading the content item");
            row!.NameEn.Should().Be("Finance");
            row.ContentType.Should().Be(contentType);
            EffectiveDates.FromColumn(row.EffectiveFrom).Should().Be(new DateOnly(2026, 1, 1));
            EffectiveDates.FromInclusiveEndColumn(row.EffectiveToInclusive).Should().BeNull();
        });

    [Fact]
    public async Task TheDimensionTypeIsStampedOnFromTheContentTypeRatherThanAskedFor() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var contentType = await GivenADimensionTypeAsync(services, "rec-stamped");
            var expected = (await types.GetByContentTypeAsync(contentType))!.DimensionTypeId;

            var item = await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "STAMP-1";
                part.NameEn = "Stamped";
                part.NameAr = "مختوم";
                part.EffectiveFrom = new DateOnly(2026, 1, 1);
            });

            await CreateAsync(services, item);

            item.Get<DimensionRecordPart>(nameof(DimensionRecordPart)).DimensionTypeId.Should().Be(
                expected,
                "the content type is the dimension type, so asking the user would be asking them to repeat themselves");
        });

    [Fact]
    public async Task ARecordWithNoEffectiveDateIsRejected() =>
        await InTenantAsSystemAsync(async services =>
        {
            var contentType = await GivenADimensionTypeAsync(services, "rec-undated");

            var item = await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "UNDATED-1";
                part.NameEn = "Undated";
                part.NameAr = "بلا تاريخ";
                // No EffectiveFrom. Specification section 2 rule 3 forbids defaulting it.
            });

            var result = await CreateAsync(services, item);

            result.Succeeded.Should().BeFalse(
                "a record saved with no effective date would resolve from 0001-01-01, which is "
                + "silently wrong rather than loudly wrong");
        });

    [Fact]
    public async Task ARecordWithNoArabicNameIsRejected() =>
        await InTenantAsSystemAsync(async services =>
        {
            var contentType = await GivenADimensionTypeAsync(services, "rec-monolingual");

            var item = await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "MONO-1";
                part.NameEn = "English only";
                part.EffectiveFrom = new DateOnly(2026, 1, 1);
            });

            var result = await CreateAsync(services, item);

            result.Succeeded.Should().BeFalse("every name field on the platform is bilingual");
        });

    [Fact]
    public async Task SavingTheSameRecordTwiceIsNotADuplicateOfItself() =>
        await InTenantAsSystemAsync(async services =>
        {
            var manager = services.GetRequiredService<IContentManager>();
            var contentType = await GivenADimensionTypeAsync(services, "rec-resaved");

            var item = await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "RESAVE-1";
                part.NameEn = "First";
                part.NameAr = "الأول";
                part.EffectiveFrom = new DateOnly(2026, 1, 1);
            });

            (await CreateAsync(services, item))
                .Succeeded.Should().BeTrue();

            item.Alter<DimensionRecordPart>(part => part.NameEn = "Renamed");

            (await manager.ValidateAsync(item)).Succeeded.Should().BeTrue(
                "editing an existing record must not report it as a duplicate of itself. This "
                + "is the case that makes record code uniqueness awkward to enforce from a "
                + "validation handler, and the reason it moves to IDimensionValidator");
        });

    [Fact]
    public async Task ARecordIsCreatedUnplacedRatherThanRefused() =>
        await InTenantAsSystemAsync(async services =>
        {
            // Creating from a generic content screen gives no parent. That is a legitimate
            // state, not an error: a new top-level unit genuinely has no parent. What it must
            // not do is place itself — placement needs MoveDimensionRecords and goes through
            // the designer.
            var contentType = await GivenADimensionTypeAsync(services, "rec-unplaced");

            var item = await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "UNPLACED-1";
                part.NameEn = "Unplaced";
                part.NameAr = "غير موضوع";
                part.EffectiveFrom = new DateOnly(2026, 1, 1);
            });

            var result = await CreateAsync(services, item);

            result.Succeeded.Should().BeTrue("unplaced is allowed");

            item.Get<DimensionRecordPart>(nameof(DimensionRecordPart)).Should().NotBeNull();
        });
}
