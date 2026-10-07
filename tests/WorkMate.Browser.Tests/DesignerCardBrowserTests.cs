using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The chart as a drawing: what fits inside a card, what one card says, where the chart sits in
/// its viewport, and the order siblings come out in.
/// </summary>
/// <remarks>
/// On the two demo companies rather than the demo organisation, because the demo organisation's
/// codes are short enough that none of this shows. Every defect these tests were written for was
/// found by looking at Zenith fully expanded: <c>zenith-dept-mechanical</c> printed out through
/// the right-hand edge of its card, the root card's second name line sat on top of its first, the
/// leftmost branch was cut off with no way to scroll to it, and Corporate — listed third in the
/// recipe — came out first because every sibling sorted alphabetically.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class DesignerCardBrowserTests
{
    private const string Zenith = "zenith-org";
    private const string Crescent = "crescent-org";

    private readonly DemoCompanyTenantFixture _tenant;

    public DesignerCardBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Nothing renders outside a card. Every element on a card, the code included, sits inside the
    /// card's own box.
    /// </summary>
    /// <remarks>
    /// Measured as rectangle containment rather than as each element's scroll size, which is the
    /// mistake the earlier version of this assertion made: text that escapes a card with
    /// <c>overflow: visible</c> never reports itself as clipped, because from the element's own
    /// point of view nothing was cut — it was simply drawn somewhere it had no business being.
    /// That is also why the earlier test was intermittent. It only failed once a card in the
    /// shared tenant happened to carry a code longer than the card, which depended on what other
    /// tests had added, so the same code passed and failed on the same day. This tenant carries
    /// two companies whose codes are deliberately long, every time.
    /// </remarks>
    [Theory]
    [InlineData(Zenith)]
    [InlineData(Crescent)]
    public async Task NothingOnACardRendersOutsideIt(string structureCode)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAsync(page, structureCode, "Chart");
        await ExpandEverythingAsync(page);

        var escaped = await page.EvaluateAsync<string[]>(
            """
            () => {
                const escaped = [];

                for (const card of document.querySelectorAll('.designer-surface .designer-card')) {
                    const box = card.getBoundingClientRect();
                    const name = card.querySelector('.designer-card-name');
                    const label = name ? name.textContent.trim() : '(no name)';

                    for (const element of card.querySelectorAll('*')) {
                        // The action menu is the one thing on a card that is meant to leave it:
                        // it is positioned out of the flow precisely so that opening it cannot
                        // change the card's height. A closed <details> still reports a box for it
                        // in Chromium, so it has to be named rather than filtered out by size.
                        if (element.closest('.designer-actions-menu')) {
                            continue;
                        }

                        const rect = element.getBoundingClientRect();

                        // Nothing to place: a hidden element, or one of the two always-rendered
                        // states the template needs and this card does not use.
                        if (rect.width === 0 && rect.height === 0) {
                            continue;
                        }

                        // A pixel of slack throughout: sub-pixel text metrics round against us.
                        if (rect.left < box.left - 1 || rect.right > box.right + 1 ||
                            rect.top < box.top - 1 || rect.bottom > box.bottom + 1) {
                            escaped.push(
                                label + ' / ' + element.className + ' "' +
                                element.textContent.trim() + '" is outside its card');
                        }
                    }
                }

                return escaped;
            }
            """);

        escaped.Should().BeEmpty();
    }

    /// <summary>
    /// One name on a card, and it is the English one, whichever language the reader is in.
    /// </summary>
    /// <remarks>
    /// Both halves matter and they fail differently. Under English the risk is a second line
    /// nobody asked for; under Arabic it is a card that reads correctly for one reader and is a
    /// different chart from everyone else's. Zenith and Crescent both carry a full set of Arabic
    /// names, so an Arabic line would certainly be populated here if one were still rendered —
    /// this is not passing because there is nothing to show.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task EveryCardCarriesExactlyOneNameAndItIsTheEnglishOne(string culture)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAsync(page, Zenith, "Chart", culture);
        await ExpandEverythingAsync(page);

        var names = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('#designer-tree .designer-node')].map(node => {
                const card = node.querySelector(':scope > .designer-card');
                const lines = card.querySelectorAll('.designer-card-name, .designer-card-name-ar');

                return lines.length + '|' + [...lines].map(line => line.textContent.trim()).join(' / ');
            })
            """);

        names.Should().NotBeEmpty();
        names.Should().OnlyContain(name => name.StartsWith("1|", StringComparison.Ordinal),
            "a card carries one name line, not two, in every language");

        names.Should().Contain("1|Zenith Engineering & Construction");
        names.Should().Contain("1|Engineering Division");
        names.Should().Contain("1|Civil Works");

        // And the Arabic names are still there to be edited, which is the half that must not have
        // been thrown away along with the line that showed them.
        await page.GotoAsync(
            "/Admin/Dimensions/Designer/Rename?structureId=" + await StructureIdAsync(page, Zenith) +
            "&recordId=" + await RecordIdAsync(page, "zenith-div-engineering"));

        await Assertions.Expect(page.Locator("#NameAr")).ToHaveValueAsync("قطاع الهندسة");
    }

    /// <summary>
    /// A chart wider than its viewport can be scrolled to both of its edges, and nothing is cut off
    /// at either.
    /// </summary>
    /// <remarks>
    /// The canvas used to centre itself with an auto margin, which puts half the overflow off the
    /// start edge where there is no scroll range to reach it. Corporate, the leftmost branch of
    /// the expanded Zenith tree, read "…ate" and no amount of panning would bring it back.
    /// </remarks>
    [Fact]
    public async Task AWideChartScrollsToBothOfItsEdges()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(900, 800);
        await OpenAsync(page, Zenith, "Chart");
        await ExpandEverythingAsync(page);

        var edges = await page.EvaluateAsync<Edges>(
            """
            () => {
                const viewport = document.querySelector('.designer-chart-viewport');
                const cards = () => [...document.querySelectorAll('#designer-tree .designer-card')]
                    .map(card => ({ card, rect: card.getBoundingClientRect() }));
                const name = entry => entry.card.querySelector('.designer-card-name').textContent.trim();

                viewport.scrollLeft = 0;
                const box = viewport.getBoundingClientRect();
                const leftmost = cards().reduce((a, b) => a.rect.left <= b.rect.left ? a : b);
                const startOverhang = box.left - leftmost.rect.left;

                viewport.scrollLeft = viewport.scrollWidth;
                const after = viewport.getBoundingClientRect();
                const rightmost = cards().reduce((a, b) => a.rect.right >= b.rect.right ? a : b);
                const endOverhang = rightmost.rect.right - after.right;

                return {
                    wider: viewport.scrollWidth > viewport.clientWidth,
                    leftmost: name(leftmost),
                    startOverhang: Math.round(startOverhang),
                    rightmost: name(rightmost),
                    endOverhang: Math.round(endOverhang)
                };
            }
            """);

        edges.Wider.Should().BeTrue(
            "the expanded Zenith tree in a 900px viewport is what this is about; if it fits, the test proves nothing");

        edges.StartOverhang.Should().BeLessThanOrEqualTo(1,
            "scrolled fully to the start, the leftmost card ({0}) must be whole", edges.Leftmost);
        edges.EndOverhang.Should().BeLessThanOrEqualTo(1,
            "scrolled fully to the end, the rightmost card ({0}) must be whole", edges.Rightmost);
    }

    /// <summary>Fit to screen shows the whole tree, not the middle of it.</summary>
    [Fact]
    public async Task FitToScreenBringsTheWholeTreeOnScreen()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(900, 800);
        await OpenAsync(page, Zenith, "Chart");
        await ExpandEverythingAsync(page);

        await page.ClickAsync("[data-designer-zoom='fit']");

        var fit = await page.EvaluateAsync<Fit>(
            """
            () => {
                const viewport = document.querySelector('.designer-chart-viewport');
                const canvas = document.querySelector('.designer-chart-canvas');
                const box = viewport.getBoundingClientRect();

                return {
                    available: viewport.clientWidth,
                    needed: canvas.scrollWidth,
                    zoom: canvas.style.getPropertyValue('--designer-zoom') || '1',
                    outside: [...document.querySelectorAll('#designer-tree .designer-card')]
                        .filter(card => {
                            const rect = card.getBoundingClientRect();
                            // Two pixels: the fit calculation divides and the result is a zoom
                            // factor, so the last pixel is rounding rather than a hidden card.
                            return rect.left < box.left - 2 || rect.right > box.right + 2;
                        })
                        .map(card => card.querySelector('.designer-card-name').textContent.trim())
                };
            }
            """);

        fit.Outside.Should().BeEmpty(
            "fit to screen means the whole tree is on screen (viewport {0}px, canvas {1}px, zoom {2})",
            fit.Available,
            fit.Needed,
            fit.Zoom);
    }

    /// <summary>
    /// Siblings come out in the order the recipe lists them, in both views.
    /// </summary>
    /// <remarks>
    /// Every assertion here is a set of siblings the recipe deliberately lists out of alphabetical
    /// order, so passing cannot be an accident of the names. Engineering, Projects, Corporate is
    /// the order the company itself puts its divisions in; Motorway Interchange before Grid
    /// Station is the order the projects started in; Lahore before Gujranwala before Sialkot is by
    /// size, which is how a bank lists its branches and is not how an alphabet does.
    /// </remarks>
    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task SiblingsAppearInTheOrderTheRecipeListsThem(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAsync(page, Zenith, view);
        await ExpandEverythingAsync(page);

        (await ChildCodesAsync(page, "zenith-org")).Should().Equal(
            "zenith-div-engineering", "zenith-div-projects", "zenith-div-corporate");

        (await ChildCodesAsync(page, "zenith-div-projects")).Should().Equal(
            "zenith-proj-motorway", "zenith-proj-grid");

        (await ChildCodesAsync(page, "zenith-proj-grid")).Should().Equal(
            "zenith-team-electrical-works", "zenith-team-civil-works");

        await OpenAsync(page, Crescent, view);
        await ExpandEverythingAsync(page);

        (await ChildCodesAsync(page, "crescent-org")).Should().Equal(
            "crescent-div-head-office", "crescent-div-branch-network");

        (await ChildCodesAsync(page, "crescent-region-punjab")).Should().Equal(
            "crescent-branch-lahore-main", "crescent-branch-gujranwala", "crescent-branch-sialkot");
    }

    /// <summary>The codes of one unit's children, in the order they are drawn.</summary>
    private static Task<string[]> ChildCodesAsync(IPage page, string parentCode) =>
        page.EvaluateAsync<string[]>(
            """
            code => {
                const codeOf = node => {
                    const element = node.querySelector(':scope > .designer-card .designer-card-code');
                    return element ? element.textContent.trim() : '';
                };

                const parent = [...document.querySelectorAll('#designer-tree .designer-node')]
                    .find(node => codeOf(node) === code);

                if (!parent) {
                    return ['(no unit with code ' + code + ' is on the page)'];
                }

                return [...parent.querySelectorAll(':scope > .designer-children > .designer-node')].map(codeOf);
            }
            """,
            parentCode);

    /// <summary>
    /// Opens one structure's tree. By id read from the picker, because a structure's id is
    /// generated and a test cannot know it, and the picker is how a person chooses one anyway.
    /// </summary>
    private static async Task OpenAsync(IPage page, string structureCode, string view, string culture = "en")
    {
        await page.GotoAsync("/Admin/Dimensions/Designer/Index");

        if (culture != "en")
        {
            await page.EvaluateAsync(
                $"document.cookie = '.AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture};path=/'");
        }

        var structureId = await StructureIdAsync(page, structureCode);

        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?structureId={structureId}&view={view}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static Task<string> StructureIdAsync(IPage page, string structureCode) =>
        page.EvaluateAsync<string>(
            """
            code => {
                const option = [...document.querySelectorAll('#structureId option')]
                    .find(option => option.textContent.includes('(' + code + ')'));

                return option ? option.value : '';
            }
            """,
            structureCode);

    private static Task<string> RecordIdAsync(IPage page, string unitCode) =>
        page.EvaluateAsync<string>(
            """
            code => {
                const node = [...document.querySelectorAll('#designer-tree .designer-node')]
                    .find(node => {
                        const element = node.querySelector(':scope > .designer-card .designer-card-code');
                        return element && element.textContent.trim() === code;
                    });

                return node ? node.getAttribute('data-record-id') : '';
            }
            """,
            unitCode);

    /// <summary>Opens every branch, so the assertions see the whole tree rather than its top.</summary>
    private static async Task ExpandEverythingAsync(IPage page)
    {
        const string collapsed = "#designer-tree .designer-node[data-expanded='false']:not([data-child-count='0'])";

        // Clicking a toggle loads a branch, which replaces part of the tree, which can detach the
        // very element a click is in the middle of landing on. That is the tree working, not
        // failing, so a timed-out click is re-queried rather than fatal — but only a few times,
        // because a toggle that genuinely never takes is the defect this suite exists to catch.
        var retries = 5;

        for (var round = 0; round < 200; round++)
        {
            if (await page.Locator(collapsed).CountAsync() == 0)
            {
                return;
            }

            try
            {
                await page.Locator(collapsed).First
                    .Locator(".designer-toggle").First
                    .ClickAsync(new LocatorClickOptions { Timeout = 10000 });
            }
            catch (TimeoutException) when (retries-- > 0)
            {
                continue;
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        throw new InvalidOperationException("The tree would not stay open.");
    }

    /// <summary>
    /// Settable properties and a parameterless constructor, which is what Playwright's deserialiser
    /// needs; a positional record cannot be built from a JSON object here.
    /// </summary>
    private sealed class Fit
    {
        public int Available { get; set; }

        public int Needed { get; set; }

        public string Zoom { get; set; } = string.Empty;

        public string[] Outside { get; set; } = [];
    }

    private sealed class Edges
    {
        public bool Wider { get; set; }

        public string Leftmost { get; set; } = string.Empty;

        public int StartOverhang { get; set; }

        public string Rightmost { get; set; } = string.Empty;

        public int EndOverhang { get; set; }
    }
}
