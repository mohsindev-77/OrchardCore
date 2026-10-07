using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The action menu closes when you click away from it, and when you press Escape.
/// </summary>
/// <remarks>
/// The menu is a <c>&lt;details&gt;</c>, which is what makes it work with no script at all and
/// what makes it keyboard-reachable for free. What <c>&lt;details&gt;</c> does not do is close
/// when you click somewhere else — left alone, every menu you open stays open, and a chart ends up
/// wearing three at once with no way to tell which one your next click belongs to.
///
/// The interesting assertions here are the ones about what must <em>not</em> happen. Clicking away
/// is one gesture, not two: it closes the menu and it does not also expand the card it landed on
/// or pick that card up for a drag. Those are the failures that would make dismissing a menu
/// dangerous rather than merely untidy.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class DesignerMenuDismissalBrowserTests
{
    private const string Division = "Sales";
    private const string Other = "Operations";

    private readonly BrowserTenantFixture _tenant;

    public DesignerMenuDismissalBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task ClickingTheBlankCanvasClosesAnOpenMenu(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);
        await OpenMenuAsync(page, Division);

        // The viewport's top edge: connector space in the chart, padding in the list, and a card
        // in neither.
        var viewport = await page.Locator(".designer-chart-viewport").BoundingBoxAsync();

        await page.Mouse.ClickAsync((float)(viewport!.X + viewport.Width - 12), (float)(viewport.Y + 4));

        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Organisation designer");
    }

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task ClickingAnotherCardClosesTheMenuWithoutExpandingThatCard(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);
        await OpenMenuAsync(page, Division);

        var other = Node(page, Other);
        var expandedBefore = await other.First.GetAttributeAsync("data-expanded");

        await other.Locator(".designer-card-name").First.ClickAsync();

        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);

        // The click belonged to the dismissal. A card that also toggled is the difference between
        // closing a menu and rearranging the screen underneath it.
        (await other.First.GetAttributeAsync("data-expanded"))
            .Should().Be(expandedBefore, "a click that dismissed a menu does not also toggle a card");
    }

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task ClickingTheSidePanelClosesTheMenu(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);
        await OpenMenuAsync(page, Division);

        await page.Locator("#designer-unplaced").ScrollIntoViewIfNeededAsync();
        await page.ClickAsync("h2:has-text('Unplaced records')");

        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task EscapeClosesAnOpenMenu(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);
        await OpenMenuAsync(page, Division);

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);
    }

    /// <summary>Opening a second menu closes the first: never two open at once.</summary>
    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task OnlyOneMenuIsEverOpen(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);

        await OpenMenuAsync(page, Division);
        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(1);

        await OpenMenuAsync(page, Other);

        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(1);
        await Assertions.Expect(Node(page, Other).Locator(".designer-actions[open]")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// The panel's cards behave like the tree's, because they are the tree's: same partial, same
    /// menu, same dismissal.
    /// </summary>
    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task AnUnplacedCardsMenuAlsoClosesOnAnOutsideClickAndOnEscape(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);

        var unplaced = page.Locator("#designer-unplaced .designer-node").First;

        await Assertions.Expect(unplaced).ToBeVisibleAsync();
        await unplaced.Locator(".designer-actions summary").First.ClickAsync();
        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(1);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);

        await unplaced.Locator(".designer-actions summary").First.ClickAsync();
        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(1);

        await page.ClickAsync("h1");
        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Dismissing a menu by clicking a card does not begin a drag of that card.
    /// </summary>
    /// <remarks>
    /// Chart only: a drag is a chart gesture, and this is the one that would turn "click away to
    /// close" into "click away to close and start reorganising the company".
    /// </remarks>
    [Fact]
    public async Task DismissingAMenuDoesNotPickUpTheCardTheClickLandedOn()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, "Chart");
        await OpenMenuAsync(page, Division);

        var target = await CentreOfAsync(Node(page, Other));

        // Press on the other card and travel well past the drag threshold without letting go.
        await page.Mouse.MoveAsync(target.X, target.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(target.X + 60, target.Y + 40, new MouseMoveOptions { Steps = 12 });

        await Assertions.Expect(page.Locator(".designer-actions[open]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".designer-dragging")).ToHaveCountAsync(0);

        await page.Mouse.UpAsync();

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Organisation designer");
    }

    // ---- scaffolding --------------------------------------------------------------------

    private static async Task OpenDesignerAsync(IPage page, string view)
    {
        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task OpenMenuAsync(IPage page, string nameEn)
    {
        await RevealAsync(page, nameEn);

        var menu = Node(page, nameEn).Locator(".designer-actions").First;

        await menu.Locator("summary").ClickAsync();
        await Assertions.Expect(menu).ToHaveAttributeAsync(
            "open", new System.Text.RegularExpressions.Regex(".*"));
    }

    private static async Task RevealAsync(IPage page, string nameEn)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (await Node(page, nameEn).CountAsync() > 0 && await Node(page, nameEn).First.IsVisibleAsync())
        {
            return;
        }

        await page.FillAsync("#designer-search", nameEn);

        var hit = page.Locator("#designer-search-results li", new() { HasTextString = nameEn }).First;

        await Assertions.Expect(hit).ToBeVisibleAsync();
        await hit.ClickAsync();
        await Assertions.Expect(Node(page, nameEn)).ToBeVisibleAsync();
    }


    private static async Task<(float X, float Y)> CentreOfAsync(ILocator node)
    {
        var box = await node.Locator(".designer-card .designer-card-name").First.BoundingBoxAsync();

        return ((float)(box!.X + (box.Width / 2)), (float)(box.Y + (box.Height / 2)));
    }

    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");
}
