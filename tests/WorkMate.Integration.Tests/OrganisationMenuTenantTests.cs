using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Navigation;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The Organisation menu has exactly three entries, in one order, in every language.
/// </summary>
/// <remarks>
/// <b>Written after a fourth kind of entry was shipped and withdrawn.</b> One menu item per
/// dimension type, built from the tenant's own configuration, read on a real tenant as
/// <em>Branchs, Cost centres, Departments, Departments, Departments, Divisions, Divisions,
/// Divisions, Projects, Regions, Sections, Teams</em> — because several structures legitimately use
/// types of the same name, and because no rule pluralises an English noun correctly. This is the
/// test that stops it, or anything like it, coming back unnoticed.
///
/// <b>On a tenant with the demo companies loaded</b>, which is the point: the base recipe alone has
/// one dimension type and would not have shown the problem. Zenith and Crescent bring eight more
/// between them, including three types named Department and two named Division.
///
/// <b>The Arabic half is <c>OrganisationMenuBrowserTests</c>.</b> The order used to come from
/// <c>LocalizedString.PrefixPosition()</c>, which sorts by the caption — so it was alphabetical in
/// whichever language the reader was in, and an Arabic reader got a different menu from an English
/// one. Proving that is fixed needs a request whose culture has actually changed, and Orchard
/// resolves that through its localisation middleware from a cookie: the browser suite sets it the
/// way the admin's own culture picker does, and is where the existing Arabic tests live.
/// </remarks>
public sealed class OrganisationMenuTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>The entries, in the order the product owner asked for.</summary>
    private static readonly string[] English =
        ["Organisation designer", "Dimension types", "Structures"];

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        // Its own tenant, with both demo companies on it. The shared base tenant has one dimension
        // type; a menu that was wrong about twelve would look right on one.
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-crescent.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    /// <summary>
    /// Three entries, those three, in that order — on a tenant with nine dimension types.
    /// </summary>
    [Fact]
    public async Task TheOrganisationMenuHasExactlyThreeEntriesInOrder() =>
        await InTenantAsSystemAsync(async services =>
        {
            // The premise. If this tenant somehow had one type, the assertion below would hold for
            // the wrong reason.
            var types = await services.GetRequiredService<IDimensionTypeService>().ListAsync();

            types.Should().HaveCountGreaterThanOrEqualTo(
                9, "base.recipe.json's cost centre plus Zenith's four and Crescent's four");

            (await EntriesAsync(services)).Should().Equal(English);
        });

    /// <summary>
    /// The rendered shell offers exactly those three links into this module, in that order.
    /// </summary>
    /// <remarks>
    /// The provider test above asks what the menu would say; this asks the page what it says, which
    /// is what closes the gap between a withdrawn provider and a route, a permission or a theme
    /// still offering the thing.
    ///
    /// <b>By href rather than by caption</b>, so it says the same thing in any language. The Arabic
    /// half of this requirement is <c>OrganisationMenuBrowserTests</c>: Orchard resolves a request's
    /// culture through its localisation middleware and the browser suite is where a culture is
    /// actually switched, by the same cookie the admin's own culture picker writes.
    /// </remarks>
    [Fact]
    public async Task TheOrganisationGroupOffersExactlyThoseThreeLinks()
    {
        var html = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin");

        var links = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html)
            .QuerySelectorAll("a[href]")
            .Select(link => link.GetAttribute("href") ?? string.Empty)
            .Where(href => href.Contains("/Dimensions/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        links.Should().Equal(
            ["/Admin/Dimensions/Designer/Index", "/Admin/Dimensions/Types/Index", "/Admin/Dimensions/Structures/Index"],
            "nine dimension types on this tenant, and still three entries");
    }

    /// <summary>
    /// No entry anywhere in the group names a dimension type.
    /// </summary>
    /// <remarks>
    /// Belt and braces beside the count: a per-type entry added under a different caption, or
    /// nested one level down, would keep the count at three and still be the thing that was
    /// withdrawn.
    /// </remarks>
    [Fact]
    public async Task NoMenuEntryNamesADimensionType() =>
        await InTenantAsSystemAsync(async services =>
        {
            var typeNames = (await services.GetRequiredService<IDimensionTypeService>()
                    .ListAsync(includeRetired: true))
                .Select(type => type.Name.En)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            var captions = (await MenuAsync(services))
                .SelectMany(item => item.Items.Select(child => child.Text.Name).Append(item.Text.Name))
                .ToList();

            foreach (var typeName in typeNames)
            {
                captions.Should().NotContain(
                    caption => caption.Contains(typeName, StringComparison.OrdinalIgnoreCase),
                    "the menu is a short list somebody scans, not an enumeration of configuration");
            }
        });

    /// <summary>
    /// The rendered admin shell shows those three and offers no route into a type's units.
    /// </summary>
    /// <remarks>
    /// The end-to-end half: everything above asks the provider what it would say. A menu entry that
    /// pointed at the per-type list would show as that route in the markup, so its absence here is
    /// what proves the withdrawal reached the page rather than only the provider.
    /// </remarks>
    [Fact]
    public async Task TheAdminShellShowsTheThreeAndLinksToNoTypesUnits()
    {
        var html = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin");

        foreach (var entry in English)
        {
            html.Should().Contain(entry);
        }

        html.Should().NotContain(
            "Dimensions/Records/Index",
            "a type's units are reached from the Dimension types list, not from the menu");
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>The Organisation group's items, in the order the menu would draw them.</summary>
    /// <remarks>
    /// This module's providers only. Orchard's own <c>OrchardCore.Contents.AdminMenu</c> reads the
    /// ambient HTTP context while it builds and throws outside a request, so asking every provider
    /// would test the harness rather than the menu. Nothing else contributes to this group, and
    /// <see cref="TheAdminShellShowsTheThreeAndLinksToNoTypesUnits"/> is the check that the real
    /// shell agrees.
    /// </remarks>
    private static async Task<IReadOnlyList<MenuItem>> MenuAsync(IServiceProvider services)
    {
        var builder = new NavigationBuilder();
        var module = typeof(IDimensionTypeService).Assembly;

        foreach (var provider in services.GetServices<INavigationProvider>()
            .Where(provider => provider.GetType().Assembly == module))
        {
            await provider.BuildNavigationAsync("admin", builder);
        }

        return builder.Build();
    }

    private static async Task<IReadOnlyList<MenuItem>> OrganisationAsync(IServiceProvider services)
    {
        var organisation = (await MenuAsync(services))
            .FirstOrDefault(item => item.Items.Count > 0);

        organisation.Should().NotBeNull("this module contributes one group with entries under it");

        return organisation!.Items;
    }

    private static async Task<IReadOnlyList<string>> EntriesAsync(IServiceProvider services) =>
        [.. (await OrganisationAsync(services)).Select(item => item.Text.Name)];

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("organisation menu test"))
            {
                await work(services);
            }
        });

    private async Task ApplyAsync(string fileName)
    {
        await InTenantAsSystemAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                new RecipeDescriptor
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                    RequireNewScope = false,
                },
                new Dictionary<string, object>(),
                CancellationToken.None);
        });
    }

    private static string RecipesFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkMate.sln")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull("the test runs inside the repository");

            return Path.Combine(directory!.FullName, "src", "WorkMate.Dimensions", "Recipes");
        }
    }
}
