using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The organisation designer driven the way a person drives it: a real browser, running the page's
/// own script, against the demo organisation seeded by the shipped demo recipe.
/// </summary>
/// <remarks>
/// Expanding a branch is the behaviour these tests exist for. It is the one thing on the screen
/// that no server-level test can see: the click, the request it makes, and the cards it puts in the
/// page all happen in the browser. The HTTP tests in WorkMate.Integration.Tests prove the Children
/// endpoint answers correctly and that the page carries the markup to call it — and both of those
/// passed while the control that calls it did nothing at all.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class OrganisationDesignerBrowserTests
{
    private readonly BrowserTenantFixture _tenant;

    public OrganisationDesignerBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task ADivisionExpandsToItsDepartmentsAndADepartmentToItsSections(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");

        // The structure itself is the top card, with the divisions already under it.
        await Assertions.Expect(Node(page, "WorkMate Demo Organisation")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Sales")).ToBeVisibleAsync();

        // Nothing below a division has been fetched yet.
        await Assertions.Expect(Node(page, "Retail")).ToHaveCountAsync(0);

        await Toggle(page, "Sales").ClickAsync();
        await Assertions.Expect(Node(page, "Retail")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Wholesale")).ToBeVisibleAsync();

        await Toggle(page, "Retail").ClickAsync();
        await Assertions.Expect(Node(page, "In-Store Sales")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Online Sales")).ToBeVisibleAsync();

        problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task CollapsingADivisionHidesTheSectionsUnderItAndExpandingItAgainBringsThemBack(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");

        await Toggle(page, "Operations").ClickAsync();
        await Toggle(page, "Logistics").ClickAsync();
        await Assertions.Expect(Node(page, "Warehousing")).ToBeVisibleAsync();

        await Toggle(page, "Operations").ClickAsync();
        await Assertions.Expect(Node(page, "Warehousing")).ToBeHiddenAsync();

        // Expanding again shows what was already fetched; it does not fetch a second copy.
        await Toggle(page, "Operations").ClickAsync();
        await Assertions.Expect(Node(page, "Warehousing")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Warehousing")).ToHaveCountAsync(1);

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task TheExpandControlIsTheSameControlOnEveryCardAndCarriesTheNumberOfChildren()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        // Both divisions have two departments, and both say so before anything is fetched.
        await Assertions.Expect(Toggle(page, "Sales")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("2"));
        await Assertions.Expect(Toggle(page, "Operations")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("2"));

        await Toggle(page, "Sales").ClickAsync();
        await Toggle(page, "Retail").ClickAsync();

        // A section has nothing under it, so it shows no control at all rather than a different one.
        await Assertions.Expect(Toggle(page, "In-Store Sales")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task SwitchingViewKeepsTheBranchesAlreadyExpanded()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        await Toggle(page, "Sales").ClickAsync();
        await Toggle(page, "Retail").ClickAsync();
        await Assertions.Expect(Node(page, "Online Sales")).ToBeVisibleAsync();

        await page.ClickAsync("[data-designer-view='List']");

        await Assertions.Expect(page.Locator(".designer-chart-viewport.designer-list")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Online Sales")).ToBeVisibleAsync();

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task TheChartCanBeZoomedAndFittedToTheScreen()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        var canvas = page.Locator(".designer-chart-canvas");

        await Assertions.Expect(page.Locator("#designer-zoom-controls")).ToBeVisibleAsync();

        await page.ClickAsync("[data-designer-zoom='in']");
        var zoomedIn = await ZoomOf(canvas);
        zoomedIn.Should().BeGreaterThan(1);

        await page.ClickAsync("[data-designer-zoom='out']");
        (await ZoomOf(canvas)).Should().BeLessThan(zoomedIn);

        await page.ClickAsync("[data-designer-zoom='fit']");
        (await ZoomOf(canvas)).Should().BeGreaterThan(0);

        // Nothing to zoom on a list, so the controls go away with the chart.
        await page.ClickAsync("[data-designer-view='List']");
        await Assertions.Expect(page.Locator("#designer-zoom-controls")).ToBeHiddenAsync();

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchingForASectionExpandsTheBranchThatHoldsIt()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        await page.FillAsync("#designer-search", "Helpdesk");
        await page.ClickAsync("#designer-search-results li");

        // Two levels that were never clicked open, because the match is three down.
        await Assertions.Expect(Node(page, "Support")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Helpdesk")).ToBeVisibleAsync();

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// The chart under Arabic: the page reads right to left and the cards carry Arabic names.
    /// </summary>
    [Fact]
    public async Task UnderArabicTheChartMirrorsAndTheCardsCarryTheArabicNames()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");
        await page.EvaluateAsync("document.cookie = '.AspNetCore.Culture=c%3Dar%7Cuic%3Dar;path=/'");
        await page.ReloadAsync();

        var direction = await page.GetAttributeAsync("html", "dir");
        direction.Should().Be("rtl");

        await Assertions.Expect(page.Locator(".designer-node .designer-card-name-ar", new()
        {
            HasTextString = "المبيعات",
        }).First).ToBeVisibleAsync();
    }

    /// <summary>One unit's card, found by the English name on it.</summary>
    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($".designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

    private static ILocator Toggle(IPage page, string nameEn) =>
        Node(page, nameEn).Locator(".designer-toggle").First;

    private static async Task<double> ZoomOf(ILocator canvas) =>
        await canvas.EvaluateAsync<double>(
            "element => parseFloat(getComputedStyle(element).getPropertyValue('--designer-zoom')) || 1");

    /// <summary>
    /// Collects anything the browser complained about. A script that throws on load leaves every
    /// control on the page inert while the markup still looks right, which is exactly the failure
    /// these tests were added for — so every test that drives the script asserts this is empty.
    /// </summary>
    private static List<string> Watch(IPage page)
    {
        var problems = new List<string>();

        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                problems.Add("console: " + message.Text);
            }
        };

        page.PageError += (_, error) => problems.Add("page error: " + error);

        page.RequestFailed += (_, request) => problems.Add($"request failed: {request.Url} ({request.Failure})");

        return problems;
    }
}
