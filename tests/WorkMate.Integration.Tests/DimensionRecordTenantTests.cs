using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.Entities;
using OrchardCore.Settings;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Models;
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

    /// <summary>
    /// A record with no Arabic name is accepted, and refused once the tenant asks for one.
    /// </summary>
    /// <remarks>
    /// This test used to assert the opposite, on the grounds that "every name field on the
    /// platform is bilingual". The field still is — both halves exist on every record — but
    /// ADR-0003's addendum makes only the English half required by default: a customer setting a
    /// tenant up has an English chart and no Arabic, and refusing the first unit does not get the
    /// translation done, it stops the chart being built. The discipline is still available, as a
    /// tenant setting, and the second half of this test is that setting doing its job.
    /// </remarks>
    [Fact]
    public async Task ARecordWithNoArabicNameIsAcceptedUnlessTheTenantRequiresOne()
    {
        var contentType = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            contentType = await GivenADimensionTypeAsync(services, "rec-monolingual");

            var permitted = await CreateAsync(services, await NewRecordAsync(services, contentType, part =>
            {
                part.Code = "MONO-1";
                part.NameEn = "English only";
                part.EffectiveFrom = new DateOnly(2026, 1, 1);
            }));

            permitted.Succeeded.Should().BeTrue(
                "Arabic is optional by default, so an untranslated unit is a unit, not an error");
        });

        // Each phase in its own scope. Orchard caches the site settings document per shell scope,
        // so a setting written and then read back inside one scope is read from the copy that was
        // already in hand — which is not how anyone changes a setting in practice, and would have
        // this test proving the policy ignores the setting when it does not.
        await InTenantAsSystemAsync(services => RequireArabicNamesAsync(services, required: true));

        try
        {
            await InTenantAsSystemAsync(async services =>
            {
                var refused = await CreateAsync(services, await NewRecordAsync(services, contentType, part =>
                {
                    part.Code = "MONO-2";
                    part.NameEn = "English only, again";
                    part.EffectiveFrom = new DateOnly(2026, 1, 1);
                }));

                refused.Succeeded.Should().BeFalse(
                    "the tenant has asked for Arabic names, and the rule is enforced in the handler every path shares");
            });
        }
        finally
        {
            await InTenantAsSystemAsync(services => RequireArabicNamesAsync(services, required: false));
        }
    }

    /// <summary>
    /// Turns the tenant's "Require Arabic names" setting on or off for the rest of a test.
    /// </summary>
    /// <remarks>
    /// Put back in a <c>finally</c> by every caller: the tenant is shared by the whole collection,
    /// and a test that leaves it on makes every later test's missing Arabic name a failure with
    /// nothing in its own body to explain it.
    /// </remarks>
    private static async Task RequireArabicNamesAsync(IServiceProvider services, bool required)
    {
        // Written straight onto the site document rather than through IWorkMateSettingsService,
        // which authorises against the signed-in user's permissions and there is no signed-in user
        // in a service-level test. This is the same write the recipe step makes.
        var siteService = services.GetRequiredService<ISiteService>();
        var site = await siteService.LoadSiteSettingsAsync();
        var settings = site.GetOrCreate<WorkMateSettings>();

        settings.RequireArabicNames = required;

        site.Put(settings);

        await siteService.UpdateSiteSettingsAsync(site);
    }

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
