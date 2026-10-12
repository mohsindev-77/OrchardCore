using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Navigation;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The People menu has exactly two entries, in one order, and the form designer adds no others.
/// </summary>
/// <remarks>
/// <b>A product-owner constraint, not a preference.</b> The form designer is allowed one admin menu
/// entry — <c>People → Forms</c> — and everything else it brings is reached by clicking from an
/// existing screen. That is easy to honour on the day and easy to erode afterwards: a lookup list
/// screen, a reconciliation screen and a version history are each individually reasonable to put in
/// a menu, and the menu is how a product becomes unusable one reasonable decision at a time. This
/// is what stops it.
///
/// <b>The second entry is also what made the order a product decision rather than an accident.</b>
/// The group used <c>LocalizedString.PrefixPosition()</c>, which sorts by the caption in whatever
/// language the reader is in: "Employees" before "Forms" in English, "النماذج" before "الموظفون" in
/// Arabic. The dimension engine found this first and records it; the positions here are explicit
/// numbers for the same reason, and the Arabic half of the proof is
/// <c>PeopleMenuBrowserTests</c> — a culture is resolved by Orchard's localisation middleware from
/// a cookie, which the browser suite sets the way the admin's own culture picker does.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class PeopleMenuTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public PeopleMenuTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    /// <summary>The entries, in the order the product owner asked for.</summary>
    private static readonly string[] English = ["Employees", "Forms"];

    [Fact]
    public async Task ThePeopleMenuHasExactlyTwoEntriesInOrder() =>
        await _fixture.InTenantAsync(async services =>
        {
            var entries = await EntriesAsync(services);

            entries.Should().Equal(English);
        });

    /// <summary>
    /// The rendered shell offers exactly those two links into this module, in that order.
    /// </summary>
    /// <remarks>
    /// The provider test above asks what the menu would say; this asks the page what it says, which
    /// closes the gap between a provider and a route, a permission or a theme still offering
    /// something. By href rather than by caption, so it means the same in any language.
    /// </remarks>
    [Fact]
    public async Task ThePeopleGroupOffersExactlyThoseTwoLinks()
    {
        var html = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin");

        var links = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html)
            .QuerySelectorAll("a[href]")
            .Select(link => link.GetAttribute("href") ?? string.Empty)
            .Where(href =>
                href.StartsWith("/Admin/Employees/", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("/Admin/Forms", StringComparison.OrdinalIgnoreCase))
            .ToList();

        links.Should().Equal(["/Admin/Employees/Index", "/Admin/Forms/Index"]);
    }

    /// <summary>
    /// No menu entry anywhere reaches a lookup list screen.
    /// </summary>
    /// <remarks>
    /// Belt and braces beside the count: an entry added under a different caption, or nested a
    /// level down, would keep the count at two and still be the thing the constraint forbids.
    /// </remarks>
    [Fact]
    public async Task NoMenuEntryReachesTheLookupLists() =>
        await _fixture.InTenantAsync(async services =>
        {
            var menu = await MenuAsync(services);

            var urls = menu
                .SelectMany(item => item.Items.Append(item))
                .SelectMany(item => item.Items.Append(item))
                .Select(item => item.Href ?? string.Empty)
                .ToList();

            urls.Should().NotContain(
                url => url.Contains("Lookups", StringComparison.OrdinalIgnoreCase),
                "the lookup lists are reached from the Forms screen, not from the menu");
        });

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>
    /// This module's navigation providers only.
    /// </summary>
    /// <remarks>
    /// Orchard's own <c>OrchardCore.Contents.AdminMenu</c> reads the ambient HTTP context while it
    /// builds and throws outside a request, so asking every provider would test the harness rather
    /// than the menu. <see cref="ThePeopleGroupOffersExactlyThoseTwoLinks"/> is the check that the
    /// real shell agrees.
    /// </remarks>
    private static async Task<IReadOnlyList<MenuItem>> MenuAsync(IServiceProvider services)
    {
        var builder = new NavigationBuilder();
        var module = typeof(ILookupListService).Assembly;

        foreach (var provider in services.GetServices<INavigationProvider>()
            .Where(provider => provider.GetType().Assembly == module))
        {
            await provider.BuildNavigationAsync("admin", builder);
        }

        return builder.Build();
    }

    private static async Task<IReadOnlyList<string>> EntriesAsync(IServiceProvider services)
    {
        var people = (await MenuAsync(services)).FirstOrDefault(item => item.Items.Count > 0);

        people.Should().NotBeNull("this module contributes one group with entries under it");

        return [.. people!.Items.Select(item => item.Text.Name)];
    }
}
