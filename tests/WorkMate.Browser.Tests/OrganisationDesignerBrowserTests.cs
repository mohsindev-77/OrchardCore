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

        // The count is on the control before anything has been fetched, and it is the number of
        // cards that then appear. Asserted against what opening it actually produces rather than
        // against a number from the demo recipe, because other tests in this collection add and
        // retire units of their own — and a count that only matches a fixture is not the promise
        // the control is making.
        foreach (var division in new[] { "Sales", "Operations" })
        {
            // The count only, not the chevron beside it.
            var claimed = int.Parse(
                (await Toggle(page, division).Locator(".designer-toggle-count").InnerTextAsync()).Trim(),
                System.Globalization.CultureInfo.InvariantCulture);

            claimed.Should().BeGreaterThan(0);

            await Toggle(page, division).ClickAsync();

            await Assertions.Expect(
                Node(page, division).Locator("> .designer-children > .designer-node"))
                .ToHaveCountAsync(claimed);
        }

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
    /// What makes a drawing of a hierarchy readable: every card at the same level starts at the
    /// same height, and a line between two cards always has a card at both ends.
    /// </summary>
    /// <remarks>
    /// Both failed on the fully expanded demo tree. Cards sat at different heights because each
    /// branch's children began directly under its own parent's card, and parents are not all the
    /// same height; and a leaf drew the connector stem that a parent draws down to its children,
    /// leaving a line hanging off the bottom of a card with nothing under it.
    ///
    /// Measured rather than eyeballed, in the browser's own layout, because this is geometry: the
    /// markup and the stylesheet were each defensible on their own and the drawing was still wrong.
    /// </remarks>
    [Fact]
    public async Task EveryCardInARowStartsAtTheSameHeightAndNoLeafTrailsAConnector()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");
        await ExpandEverythingAsync(page);

        var cards = await MeasureCardsAsync(page);

        cards.Should().HaveCountGreaterThan(10, "the demo organisation is three levels deep");

        foreach (var row in cards.GroupBy(card => card.Depth))
        {
            var tops = row.Select(card => card.Top).Distinct().ToList();

            tops.Should().ContainSingle(
                "every card at depth {0} must start at the same height, but these did not: {1}",
                row.Key,
                string.Join(", ", row.Select(card => $"{card.Name} at {card.Top}")));
        }

        foreach (var leaf in cards.Where(card => card.ChildCount == 0))
        {
            leaf.ChildrenDisplay.Should().Be(
                "none",
                "{0} has nothing under it, so it must not draw a connector below its card", leaf.Name);
        }

        cards.Select(card => card.TextAlign).Distinct().Should().ContainSingle(
            "every card is laid out the same way, whether or not it has children");

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// Nothing on a card is cut off. A code is an identifier people type, search for and read out
    /// to each other, so half of one is worse than none; a name that does not fit wraps.
    /// </summary>
    /// <remarks>
    /// Measured against each element's own scroll size, which is how the browser reports text it
    /// has had to clip. An earlier version gave every card one fixed height and an ellipsis to
    /// enforce it, which lined the rows up beautifully and turned "WorkMate Demo Organisation"
    /// into "WorkMate De…".
    /// </remarks>
    [Fact]
    public async Task NoCardClipsItsNameOrItsCode()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");
        await ExpandEverythingAsync(page);

        var clipped = await page.EvaluateAsync<string[]>(
            """
            () => {
                const clipped = [];

                for (const element of document.querySelectorAll(
                    '.designer-card-name, .designer-card-name-ar, .designer-card-code')) {
                    // A pixel of slack: sub-pixel text metrics round against us otherwise.
                    if (element.scrollWidth > element.clientWidth + 1 ||
                        element.scrollHeight > element.clientHeight + 1) {
                        clipped.push(element.className + ' "' + element.textContent.trim() + '"');
                    }
                }

                return clipped;
            }
            """);

        clipped.Should().BeEmpty("every name and code on the chart must be readable in full");

        // And the rows are still rows, even though the cards are free to grow.
        foreach (var row in (await MeasureCardsAsync(page)).GroupBy(card => card.Depth))
        {
            row.Select(card => card.Top).Distinct().Should().ContainSingle(
                "cards at depth {0} grow to the tallest in the row rather than drifting apart: {1}",
                row.Key,
                string.Join(", ", row.Select(card => $"{card.Name} at {card.Top}")));
        }
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

    /// <summary>
    /// A unit with no Arabic name still has a name under Arabic: the English one, not a blank.
    /// </summary>
    /// <remarks>
    /// ADR-0003's addendum made Arabic optional, which is a validation change. This is the display
    /// half of it, and the half that fails silently — the page renders, the card is there, and the
    /// name is simply missing. Asserted on a unit created through the screen with the Arabic box
    /// left empty, which is exactly what a customer setting a tenant up does.
    /// </remarks>
    [Fact]
    public async Task AUnitWithNoArabicNameShowsItsEnglishNameUnderArabic()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await OpenMenuAsync(page, "WorkMate Demo Organisation");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='add']");

        await page.FillAsync("#Code", "fallback-div");
        await page.FillAsync("#NameEn", "Untranslated Division");
        // The Arabic box is deliberately left as it came.
        await page.ClickAsync("form[action*='AddUnit'] button[type=submit]");

        await page.WaitForURLAsync(url => url.Contains("Designer/Index", StringComparison.Ordinal));
        await Assertions.Expect(Node(page, "Untranslated Division")).ToBeVisibleAsync();

        await page.EvaluateAsync("document.cookie = '.AspNetCore.Culture=c%3Dar%7Cuic%3Dar;path=/'");
        await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var card = Node(page, "Untranslated Division");

        await Assertions.Expect(card).ToBeVisibleAsync();

        // No Arabic line reserving space where a name should be — present in the markup, because
        // the template this card is cloned from needs it, and hidden because there is nothing in
        // it — and the English name still on the card for an Arabic reader to read.
        await Assertions.Expect(card.Locator(".designer-card-name-ar").First).ToBeHiddenAsync();
        await Assertions.Expect(card.Locator(".designer-card-name").First).ToHaveTextAsync("Untranslated Division");
    }

    private static async Task OpenMenuAsync(IPage page, string nameEn)
    {
        var menu = Node(page, nameEn).Locator(".designer-actions").First;

        await menu.Locator("summary").ClickAsync();
        await Assertions.Expect(menu).ToHaveAttributeAsync(
            "open", new System.Text.RegularExpressions.Regex(".*"));
    }

    /// <summary>Opens every branch, so the assertions see the whole tree rather than its top.</summary>
    private static async Task ExpandEverythingAsync(IPage page)
    {
        const string collapsed = "#designer-tree .designer-node[data-expanded='false']:not([data-child-count='0'])";

        // Bounded, because a bug that reopened what it just closed would otherwise hang the suite
        // rather than fail it. The bound is only a backstop and is deliberately far above the tree
        // it expects: the tenant is shared, every other test in the collection adds units to it,
        // and a cap set to the demo recipe's own size turns somebody else's new test into a
        // failure here that says nothing about the chart.
        for (var round = 0; round < 500; round++)
        {
            var next = page.Locator(collapsed).First;

            if (await page.Locator(collapsed).CountAsync() == 0)
            {
                return;
            }

            await next.Locator(".designer-toggle").First.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        throw new InvalidOperationException("The tree would not stay open.");
    }

    /// <summary>
    /// Where every card actually ended up, measured in the browser. Positions are relative to the
    /// chart canvas, not the window, so panning or scrolling cannot move them.
    /// </summary>
    /// <remarks>
    /// Scoped to the tree. The unplaced panel draws the same card partial — which is what gives an
    /// unplaced unit an action menu and makes it draggable — but it is a stacked list beside the
    /// chart rather than part of it, so its cards are not in any of the chart's rows and have no
    /// business in a measurement about row alignment.
    /// </remarks>
    private static async Task<IReadOnlyList<MeasuredCard>> MeasureCardsAsync(IPage page) =>
        await page.EvaluateAsync<MeasuredCard[]>(
            """
            () => {
                const canvas = document.querySelector('.designer-chart-canvas').getBoundingClientRect();

                return [...document.querySelectorAll('#designer-tree .designer-node')].map(node => {
                    let depth = 0;
                    for (let p = node.parentElement; p; p = p.parentElement) {
                        if (p.classList && p.classList.contains('designer-node')) { depth++; }
                    }

                    const card = node.querySelector(':scope > .designer-card');
                    const children = node.querySelector(':scope > .designer-children');
                    const box = card.getBoundingClientRect();

                    return {
                        name: card.querySelector('.designer-card-name').textContent.trim(),
                        depth: depth,
                        top: Math.round(box.top - canvas.top),
                        childCount: Number(node.getAttribute('data-child-count')),
                        childrenDisplay: children ? getComputedStyle(children).display : 'none',
                        textAlign: getComputedStyle(card).textAlign
                    };
                });
            }
            """);

    /// <summary>
    /// Settable properties and a parameterless constructor, which is what Playwright's deserialiser
    /// needs; a positional record cannot be built from a JSON object here.
    /// </summary>
    private sealed class MeasuredCard
    {
        public string Name { get; set; } = string.Empty;

        public int Depth { get; set; }

        public int Top { get; set; }

        public int ChildCount { get; set; }

        public string ChildrenDisplay { get; set; } = string.Empty;

        public string TextAlign { get; set; } = string.Empty;
    }

    /// <summary>One unit's card, found by the English name on it.</summary>
    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

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
