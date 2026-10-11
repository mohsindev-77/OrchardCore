using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The Organisation menu, in a real browser, in both languages, on a tenant with nine dimension
/// types on it.
/// </summary>
/// <remarks>
/// <b>Written after a fourth kind of entry was shipped and withdrawn.</b> One menu item per
/// dimension type read, on this very tenant, as <em>Branchs, Cost centres, Departments,
/// Departments, Departments, Divisions, Divisions, Divisions, Projects, Regions, Sections,
/// Teams</em> — several structures legitimately use types of the same name, and no rule pluralises
/// an English noun correctly.
///
/// <b>Why the Arabic half is here and not in the integration suite.</b> Orchard resolves a request's
/// culture through its localisation middleware, from the cookie its own culture picker writes. A
/// browser sets that cookie the way a person does; a hand-built <c>Cookie</c> header on a client
/// that already has a cookie container does not reliably survive, and a test that silently kept
/// reading English would be worse than no test. The existing Arabic tests in this suite set it the
/// same way.
///
/// The Zenith and Crescent tenant, because the base recipe's single dimension type would not have
/// shown the problem in the first place.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class OrganisationMenuBrowserTests
{
    private readonly DemoCompanyTenantFixture _tenant;

    public OrganisationMenuBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>The three links, in order, identified by where they go rather than by their words.</summary>
    private static readonly string[] Expected =
    [
        "/Admin/Dimensions/Designer/Index",
        "/Admin/Dimensions/Types/Index",
        "/Admin/Dimensions/Structures/Index",
    ];

    /// <summary>
    /// Exactly three entries into this module, in that order, whatever language the reader is in.
    /// </summary>
    /// <remarks>
    /// By href, so the same assertion holds in both languages — which is the point: the order used
    /// to come from <c>PrefixPosition()</c>, which sorts by the caption, so it was alphabetical in
    /// whichever language the reader was in and the two did not agree.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task TheOrganisationMenuOffersExactlyThreeEntriesInOrder(string culture)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, culture);

        var hrefs = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('a[href*="/Dimensions/"]')]
                .map(link => new URL(link.href, location.origin).pathname)
            """);

        hrefs.Should().Equal(Expected, "nine dimension types on this tenant, and still three entries");
    }

    /// <summary>
    /// An Arabic reader gets the Arabic captions, not the English strings falling through.
    /// </summary>
    /// <remarks>
    /// Separate from the order, because the two fail for different reasons: a missing resource shows
    /// as English text in the right place, and a positioning mistake shows as Arabic text in the
    /// wrong order. One assertion covering both would report either as the other.
    /// </remarks>
    [Fact]
    public async Task AnArabicReaderGetsTheArabicCaptions()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "ar");

        var captions = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('a[href*="/Dimensions/"]')]
                .map(link => link.textContent.trim())
            """);

        captions.Should().HaveCount(3);

        captions.Should().NotContain(
            caption => caption.Contains("Dimension types", StringComparison.Ordinal),
            "the caption is a resource, and an untranslated one would read as the English msgid");

        string.Concat(captions).Should().Contain("الهياكل", "Structures");
        string.Concat(captions).Should().Contain("أنواع الأبعاد", "Dimension types");
        string.Concat(captions).Should().Contain("مصمم الهيكل التنظيمي", "Organisation designer");
    }

    /// <summary>
    /// No menu entry anywhere opens a dimension type's unit list.
    /// </summary>
    /// <remarks>
    /// Belt and braces beside the count. An entry added under a different caption, or nested a level
    /// down, would leave three links into the three screens above and still be the thing that was
    /// withdrawn — what identifies it is where it goes.
    /// </remarks>
    [Fact]
    public async Task NoMenuEntryOpensATypesUnitList()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await Assertions.Expect(page.Locator("a[href*='Dimensions/Records/']")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Opens the admin in one culture, with the Organisation group expanded.
    /// </summary>
    /// <remarks>
    /// The culture cookie is set the way the admin's own picker sets it, then the page is loaded —
    /// the order matters, because the middleware reads it on the request rather than afterwards.
    /// The group is expanded if it is closed, because a collapsed group's links are in the DOM but
    /// the markup this reads is the same either way; expanding only keeps the page honest for
    /// anybody debugging a failure with the browser open.
    /// </remarks>
    private static async Task OpenAdminInAsync(IPage page, string culture)
    {
        await page.GotoAsync("/Admin");

        await page.EvaluateAsync(
            $"document.cookie = '.AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture};path=/'");

        await page.ReloadAsync();

        var group = page.GetByRole(AriaRole.Button, new() { NameString = "Organisation" }).First;

        if (await group.CountAsync() > 0 && await group.IsVisibleAsync())
        {
            var first = page.Locator("a[href*='/Dimensions/']").First;

            if (!await first.IsVisibleAsync())
            {
                await group.ClickAsync();
            }
        }
    }
}
