using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// A dimension's tree and list views, and the alias beside each name — FRS v2.5 §4.1, slice F1.
/// </summary>
/// <remarks>
/// <b>One typed path and clicks for everything after it.</b> CLAUDE.md requires every screen to be
/// reachable by clicking, and this suite's rule since prompt 4 session A2 is that a browser test
/// proves it rather than assuming it: the tests that preceded that rule started by typing URLs,
/// which proves a screen works and says nothing about whether anybody can get to it. So the only
/// navigation here is <c>/Admin</c>; the menu, the View units link, the view toggle and the
/// collapse control are all pressed.
///
/// <b>What this sees that the integration suite cannot.</b> Everything the server renders is
/// asserted in <c>DimensionAliasAndTreeViewTenantTests</c>, far more cheaply. What only a browser
/// can answer is whether the collapse control actually collapses — the expand control on the
/// organisation designer shipped doing nothing at all while the whole suite stayed green, because
/// the markup was right and nothing ever clicked it.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class DimensionTreeViewBrowserTests
{
    private readonly BrowserTenantFixture _tenant;

    public DimensionTreeViewBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Menu → Dimension types → View units on Department → Tree → collapse a branch → List.
    /// </summary>
    /// <remarks>
    /// The demo organisation's Departments sit under Divisions, so on the Department screen every
    /// one of them is a root — which is Rule 2 working, and is why this test collapses from the
    /// Division screen instead, where the demo tree has depth. The assertions name rows by code
    /// rather than counting them, because the tenant is shared and other tests in this collection
    /// add units to it.
    /// </remarks>
    [Fact]
    public async Task ADimensionsTreeAndListAreReachedAndToggledEntirelyByClicking()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await AdminMenu.ClickAsync(page, "Dimension types", "Organisation", "Settings");

        await page.Locator(".dimension-type-row[data-type-code='demo-department'] .dimension-type-units")
            .ClickAsync();

        await Assertions.Expect(page.Locator("#record-list-heading")).ToContainTextAsync("Department");

        // List view is what the screen opens in, and the Alias column is on it.
        await Assertions.Expect(page.Locator("#record-list")).ToBeVisibleAsync();

        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='demo-dept-retail'] .record-alias"))
            .ToHaveTextAsync("Shops");

        // A unit the demo recipe gives no alias leaves the cell empty rather than printing a dash,
        // which would be a claim about the unit that nothing had checked.
        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='demo-dept-wholesale'] .record-alias"))
            .ToHaveTextAsync("");

        // The toggle, by clicking it.
        await page.ClickAsync("#record-view-tree");

        await Assertions.Expect(page.Locator("#record-list.record-tree")).ToBeVisibleAsync();

        // Rule 2: the Divisions these Departments sit under on the chart are not on this screen.
        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='demo-div-sales']")).ToHaveCountAsync(0);

        await page.ClickAsync("#record-view-list");

        await Assertions.Expect(page.Locator("#record-list")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#record-list.record-tree")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The collapse control hides a branch, and expanding it brings the branch back.
    /// </summary>
    /// <remarks>
    /// <b>The only thing on this screen that needs the browser.</b> Everything else — adding,
    /// editing, filtering, paging, switching view — is a link or a form the server rendered, and
    /// works with no script at all. This does not, so this is where a browser test earns its cost.
    ///
    /// <b>Named rows, not "the first branch".</b> The demo recipe seeds <c>demo-sec-counter</c>
    /// inside <c>demo-sec-instore</c> for exactly this test — the FRS's own Department → HR →
    /// Recruitment shape, one level down — so the branch is always there and is always the same
    /// one. Finding "whichever row has a toggle" would pass on a tree that happened to have one and
    /// assert nothing on a tenant where another test in this shared collection had changed the
    /// shape.
    ///
    /// <b>Section rather than Department, and the recipe says why.</b> Adding a unit under a parent
    /// that permits two kinds makes the designer's Add-unit form ask which kind, and the designer
    /// browser tests add departments under divisions and sections under departments. Section is the
    /// one level nothing in this suite adds beneath, so it is the one that can carry the diagonal
    /// without changing another test's journey.
    /// </remarks>
    [Fact]
    public async Task CollapsingABranchHidesItsDescendantsAndExpandingBringsThemBack()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await AdminMenu.ClickAsync(page, "Dimension types", "Organisation", "Settings");

        await page.Locator(".dimension-type-row[data-type-code='demo-section'] .dimension-type-units")
            .ClickAsync();

        await page.ClickAsync("#record-view-tree");
        await Assertions.Expect(page.Locator("#record-list.record-tree")).ToBeVisibleAsync();

        var branch = page.Locator("tr.record-row[data-record-code='demo-sec-instore']");
        var child = page.Locator("tr.record-row[data-record-code='demo-sec-counter']");

        // The server put the nesting on the rows; the script only hides and shows.
        await Assertions.Expect(child).ToHaveAttributeAsync("data-depth", "1");
        await Assertions.Expect(child).ToBeVisibleAsync();

        await branch.Locator(".record-toggle").ClickAsync();

        await Assertions.Expect(child).ToBeHiddenAsync();
        await Assertions.Expect(branch.Locator(".record-toggle")).ToHaveAttributeAsync("aria-expanded", "false");

        await branch.Locator(".record-toggle").ClickAsync();

        await Assertions.Expect(child).ToBeVisibleAsync();
        await Assertions.Expect(branch.Locator(".record-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    /// <summary>
    /// A dimension's own menu entry opens that dimension's screen — ADR-0024, by clicking.
    /// </summary>
    /// <remarks>
    /// <b>The journey the FRS actually asks for</b>, and the one the "View units" route above is
    /// the second way into. §1.2: <em>"All dimensional master tables … should appear as separate
    /// menu items."</em>
    ///
    /// Two things are asserted and both matter. The entry reads the <b>authored plural</b> —
    /// <em>Sections</em>, which this tenant's recipe states and which no suffix rule produces from
    /// شعبة — and clicking it lands on <b>that</b> dimension's list rather than on the first one,
    /// which is what a menu built from a shared route and a forgotten route value would do.
    ///
    /// <b>Two clicks since decision D23</b>: the dimension's entry expands, and <em>All
    /// Sections</em> under it opens the list. The entry itself is a group now, because the values
    /// hang off it.
    /// </remarks>
    [Fact]
    public async Task ADimensionsOwnMenuEntryOpensItsOwnScreen()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await AdminMenu.ClickAsync(page, "All Sections", "Organisation", "Sections");

        await Assertions.Expect(page.Locator("#record-list-heading")).ToContainTextAsync("Section");

        // That dimension's values, not another's: a section the demo recipe seeds, and no department.
        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='demo-sec-instore']")).ToBeVisibleAsync();

        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='demo-dept-retail']")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The toolbar's Edit acts on the row the reader ticked.
    /// </summary>
    /// <remarks>
    /// The selection is a radio inside the table and the buttons sit outside it, joined by the
    /// <c>form</c> attribute — plain HTML, no script. So this is really asking whether that join
    /// works in a real browser, which is exactly the kind of thing that renders correctly and does
    /// nothing.
    /// </remarks>
    [Fact]
    public async Task TheToolbarsEditOpensTheRowThatWasTicked()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await AdminMenu.ClickAsync(page, "Dimension types", "Organisation", "Settings");

        await page.Locator(".dimension-type-row[data-type-code='demo-department'] .dimension-type-units")
            .ClickAsync();

        await page.Locator("tr.record-row[data-record-code='demo-dept-retail'] .record-select").CheckAsync();
        await page.ClickAsync("#record-edit-selected");

        // The editor for that record, named for it — and carrying the alias box the FRS asks for.
        await Assertions.Expect(page.Locator("#record-edit")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#AliasEn")).ToHaveValueAsync("Shops");
    }
}
