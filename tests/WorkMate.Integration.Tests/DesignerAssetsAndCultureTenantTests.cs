using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Two things about the designer that are true of the page itself, so they can be asserted without
/// a browser: its assets are versioned, and the date it round-trips does not depend on the culture
/// the admin is in.
/// </summary>
/// <remarks>
/// Both are also covered by the browser suite, which is where the behaviour they protect actually
/// lives. These are here because they are cheap, they run everywhere, and the asset one in
/// particular guards a defect whose symptom is invisible from the server: a browser running a
/// month-old script against correct HTML.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DesignerAssetsAndCultureTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DesignerAssetsAndCultureTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Every WorkMate asset on an admin screen carries a version token.
    /// </summary>
    /// <remarks>
    /// Static files are served with <c>cache-control: public, max-age=2592000</c>. At a URL that
    /// never changes, that is thirty days during which a rebuild cannot reach a browser that has
    /// already been to the screen once — it does not even send a conditional request. The token
    /// comes from registering the asset with the resource manager and asking for it by name; a view
    /// that goes back to <c>&lt;script src="~/WorkMate…"&gt;</c> fails here.
    /// </remarks>
    // The three screens that load a script or stylesheet of this module's own.
    [Theory]
    [InlineData("/Admin/Dimensions/Designer/Index")]
    [InlineData("/Admin/Dimensions/Types/Create")]
    [InlineData("/Admin/Dimensions/Structures/Create")]
    public async Task EveryWorkMateAssetOnAnAdminScreenIsVersioned(string path)
    {
        var html = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, path);

        html.Should().Contain("WorkMate.", "this screen is expected to load an asset of WorkMate's own");

        var assets = Regex.Matches(
                html,
                "(?:src|href)=\"(?<url>[^\"]*WorkMate\\.[^\"]*\\.(?:js|css)[^\"]*)\"",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["url"].Value)
            .ToList();

        assets.Should().AllSatisfy(url => url.Should().Contain(
            "?v=",
            "an unversioned WorkMate asset is cached by the browser for 30 days, so a rebuild never reaches it"));
    }

    /// <summary>
    /// The same requested date comes back as the same date in English and in Arabic. A
    /// culture-sensitive parse or format anywhere in the round trip would resolve a different day
    /// for one of them — and under a calendar that is not Gregorian, a different year.
    /// </summary>
    [Fact]
    public async Task TheDesignerResolvesTheSameDateInEnglishAndInArabic()
    {
        // Deliberately the ambiguous kind of date: 04/03 and 03/04 are the same day written two
        // ways, and only one of them is 4 March.
        const string requested = "2026-03-04";

        var structureId = await GivenAStructureAsync("culture");

        var english = await DesignerAsAtAsync("en", structureId, requested);
        var arabic = await DesignerAsAtAsync("ar", structureId, requested);

        english.Should().Be(requested);
        arabic.Should().Be(requested, "the date on the wire is not the date on display");
        arabic.Should().Be(english);
    }

    /// <summary>The date the page carries is the one every fetch it makes is built from.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task TheDatePickerAndTheSurfaceAgreeInEitherCulture(string culture)
    {
        const string requested = "2026-03-04";

        var structureId = await GivenAStructureAsync($"picker-{culture}");

        var html = await PageAsync(
            culture, $"/Admin/Dimensions/Designer/Index?structureId={structureId}&asAt={requested}");

        AttributeIn(html, "data-as-at").Should().Be(requested);

        Regex.Match(html, "id=\"asAt\"[^>]*value=\"(?<value>[^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Groups["value"].Value.Should().Be(requested);
    }

    private async Task<string> DesignerAsAtAsync(string culture, string structureId, string requested) =>
        AttributeIn(
            await PageAsync(culture, $"/Admin/Dimensions/Designer/Index?structureId={structureId}&asAt={requested}"),
            "data-as-at");

    /// <summary>
    /// A structure with one unit on it, because the designer renders nothing at all — no surface,
    /// no date control — for a tenant that has no structures, and this fixture's tenant is the
    /// plain base recipe.
    /// </summary>
    private async Task<string> GivenAStructureAsync(string prefix)
    {
        var structureId = string.Empty;

        await _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                var types = services.GetRequiredService<IDimensionTypeService>();
                var structures = services.GetRequiredService<IStructureService>();
                var records = services.GetRequiredService<IDimensionService>();

                var division = await types.CreateAsync(
                    $"{prefix}-division", new BilingualText($"{prefix} Division", "قسم"), [], allowsSelfNesting: false);
                division.Succeeded.Should().BeTrue(string.Join("; ", division.Errors.Select(e => e.Message.Value)));

                var structure = await structures.CreateAsync(
                    $"{prefix}-structure",
                    new BilingualText($"{prefix} Structure", "هيكل"),
                    [division.Value!.DimensionTypeId],
                    allowSkipLevel: false,
                    isStrict: true,
                    isPrimaryOrganisation: false);
                structure.Succeeded.Should().BeTrue(string.Join("; ", structure.Errors.Select(e => e.Message.Value)));
                structureId = structure.Value!.StructureId;

                var record = await records.CreateAsync(
                    division.Value.DimensionTypeId,
                    $"{prefix}-div-1",
                    new BilingualText($"{prefix} Division One", "واحد"),
                    new EffectiveRange(new DateOnly(2024, 1, 1), null));
                record.Succeeded.Should().BeTrue(string.Join("; ", record.Errors.Select(e => e.Message.Value)));
            }
        });

        return structureId;
    }

    private async Task<string> PageAsync(string culture, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        // How Orchard's culture picker records a choice: the same cookie the admin UI writes.
        request.Headers.Add("Cookie", $".AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture}");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        var response = await _fixture.Administrator.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"GET {path} under '{culture}' should render");

        return body;
    }

    private static string AttributeIn(string html, string attribute) =>
        Regex.Match(html, $"{attribute}=\"(?<value>[^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Groups["value"].Value;
}
