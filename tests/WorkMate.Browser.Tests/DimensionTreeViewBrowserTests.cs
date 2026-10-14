using FluentAssertions;
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

    /// <summary>
    /// Opening a value from the menu shows the units under it, three levels deep, and collapses.
    /// </summary>
    /// <remarks>
    /// <b>Decision D24, by clicking, which is the only way the collapse half counts.</b> The
    /// organisation designer's expand control once shipped doing nothing at all while the whole
    /// suite stayed green, because the markup was right and nothing ever pressed it. This presses
    /// everything: the menu entry, the value under it, a branch toggle, and Collapse all.
    ///
    /// The demo chart is Sales → Retail → In-Store Sales → Counter, which is three levels below
    /// the division and the only seeded tree deep enough to tell "nested" from "a list of children".
    /// </remarks>
    [Fact]
    public async Task AValuesPageShowsTheUnitsUnderItAndCollapsesThem()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSalesAsync(page);

        await Assertions.Expect(page.Locator("#value-name")).ToContainTextAsync("Sales");
        await Assertions.Expect(page.Locator("#value-tree-heading")).ToContainTextAsync("Units under Sales");

        var retail = page.Locator("#value-tree tr.record-row[data-record-code='demo-dept-retail']");
        var inStore = page.Locator("#value-tree tr.record-row[data-record-code='demo-sec-instore']");
        var counter = page.Locator("#value-tree tr.record-row[data-record-code='demo-sec-counter']");

        await Assertions.Expect(retail).ToBeVisibleAsync();
        await Assertions.Expect(inStore).ToBeVisibleAsync();
        await Assertions.Expect(counter).ToBeVisibleAsync();

        // Three levels, indented by depth, and each naming the dimension it is of.
        await Assertions.Expect(retail).ToHaveAttributeAsync("data-depth", "1");
        await Assertions.Expect(inStore).ToHaveAttributeAsync("data-depth", "2");
        await Assertions.Expect(counter).ToHaveAttributeAsync("data-depth", "3");
        await Assertions.Expect(retail).ToContainTextAsync("Department");

        // Collapsing the top branch takes everything under it with it.
        await retail.Locator(".record-toggle").ClickAsync();

        await Assertions.Expect(inStore).ToBeHiddenAsync();
        await Assertions.Expect(counter).ToBeHiddenAsync();
        await Assertions.Expect(retail).ToBeVisibleAsync();

        // Expand all brings the whole subtree back in one press.
        await page.ClickAsync("#value-tree-expand-all");

        await Assertions.Expect(counter).ToBeVisibleAsync();

        await page.ClickAsync("#value-tree-collapse-all");

        // Only the immediate children survive a collapse; everything deeper goes.
        await Assertions.Expect(retail).ToBeVisibleAsync();
        await Assertions.Expect(inStore).ToBeHiddenAsync();
    }

    /// <summary>
    /// A unit's name in the tree opens that unit's own page, and a leaf has no tree at all.
    /// </summary>
    /// <remarks>
    /// The two ends of D24's fifth requirement in one journey: every row is a way further in, and
    /// the bottom of the chart says nothing rather than showing an empty table.
    /// </remarks>
    [Fact]
    public async Task AUnitInTheTreeOpensItsOwnPageAndALeafHasNoTree()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSalesAsync(page);

        await page.Locator("#value-tree tr.record-row[data-record-code='demo-sec-counter'] a").First.ClickAsync();

        await Assertions.Expect(page.Locator("#value-name")).ToContainTextAsync("Counter");
        await Assertions.Expect(page.Locator("#value-code")).ToContainTextAsync("demo-sec-counter");

        // Counter is the bottom of this chart, so there is no section — not an empty one.
        await Assertions.Expect(page.Locator("#value-tree")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#value-tree-heading")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The three views switch by clicking, each draws its own thing, and the URL says which.
    /// </summary>
    /// <remarks>
    /// <b>Decision D25, in a browser because that is where a view switch exists.</b> The server
    /// side is covered far more cheaply in <c>DimensionValuePageTenantTests</c>; what only this can
    /// answer is whether the three links are reachable, whether pressing one lands on the view it
    /// names, and whether the back button returns to the one before — which is the whole reason
    /// the view is in the URL rather than only in storage.
    /// </remarks>
    [Fact]
    public async Task TheThreeViewsSwitchByClickingAndTheUrlSaysWhichIsShowing()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSalesAsync(page);

        // Tree is what you get without asking.
        await Assertions.Expect(page.Locator("#value-tree")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#value-units-view-tree")).ToHaveAttributeAsync("aria-current", "true");

        await page.ClickAsync("#value-units-view-hierarchy");

        await Assertions.Expect(page.Locator("#value-hierarchy")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#value-tree")).ToHaveCountAsync(0);
        page.Url.Should().Contain("view=hierarchy");

        await page.ClickAsync("#value-units-view-list");

        await Assertions.Expect(page.Locator("#value-list")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#value-hierarchy")).ToHaveCountAsync(0);
        page.Url.Should().Contain("view=list");

        // The back button returns to the view before, because each one is a real page.
        await page.GoBackAsync();

        await Assertions.Expect(page.Locator("#value-hierarchy")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The hierarchy expands, collapses, zooms, and every box is a way into that unit's page.
    /// </summary>
    /// <remarks>
    /// The expand control is the half that only a browser can answer: the designer's own expand
    /// control once shipped doing nothing at all while the whole suite stayed green, because the
    /// markup was right and nothing ever pressed it.
    /// </remarks>
    [Fact]
    public async Task TheHierarchyExpandsCollapsesZoomsAndLinksToEachUnit()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSalesAsync(page);
        await page.ClickAsync("#value-units-view-hierarchy");

        // Scoped to the chart, not to the page. Since decision D23 the Organisation menu lists a
        // dimension's values too, so "In-Store Sales" is a link in the sidebar as well and a
        // page-wide locator matches two — which Playwright reports as a strict-mode violation
        // rather than quietly picking one. The question here is about the chart.
        var chart = page.Locator("#value-hierarchy");

        var retail = chart.Locator("li.designer-node[data-record-id]")
            .Filter(new() { Has = page.Locator("a.value-hierarchy-name", new() { HasTextString = "Retail" }) })
            .First;

        var inStore = chart.GetByRole(AriaRole.Link, new() { NameString = "In-Store Sales" });

        await Assertions.Expect(inStore).ToBeVisibleAsync();

        // Collapsing a branch takes everything under it with it.
        await retail.Locator(".value-hierarchy-toggle").First.ClickAsync();

        await Assertions.Expect(inStore).ToBeHiddenAsync();

        await page.ClickAsync("#value-tree-expand-all");

        await Assertions.Expect(inStore).ToBeVisibleAsync();

        // Zoom scales the canvas, which is what keeps a wide chart's scrollbars honest.
        await page.ClickAsync("[data-value-zoom='in']");

        var zoomed = await page.Locator("#value-hierarchy-canvas").EvaluateAsync<string>(
            "node => node.style.getPropertyValue('--designer-zoom')");

        zoomed.Should().NotBeNullOrEmpty("the zoom control set a scale on the canvas");

        await page.ClickAsync("[data-value-zoom='fit']");

        // And a box is a way in: clicking a name opens that unit's own page.
        await chart.GetByRole(AriaRole.Link, new() { NameString = "Counter" }).ClickAsync();

        await Assertions.Expect(page.Locator("#value-name")).ToContainTextAsync("Counter");
        await Assertions.Expect(page.Locator("#value-code")).ToContainTextAsync("demo-sec-counter");
    }

    /// <summary>
    /// The list sorts by clicking a column heading, and its names open the right pages.
    /// </summary>
    [Fact]
    public async Task TheListSortsByClickingAColumnHeading()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSalesAsync(page);
        await page.ClickAsync("#value-units-view-list");

        await page.ClickAsync("#value-list thead a[data-sort='name']");

        page.Url.Should().Contain("sort=name");

        var ascending = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('#value-list tbody tr')].map(r => r.getAttribute('data-record-code'))");

        // Pressing the same heading again turns it round, which is what a reader expects of a
        // column they have just sorted.
        await page.ClickAsync("#value-list thead a[data-sort='name']");

        var descending = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('#value-list tbody tr')].map(r => r.getAttribute('data-record-code'))");

        descending.Should().Equal(ascending.Reverse(), "the same rows, the other way up");

        await page.Locator("#value-list tr[data-record-code='demo-sec-counter'] a").First.ClickAsync();

        await Assertions.Expect(page.Locator("#value-code")).ToContainTextAsync("demo-sec-counter");
    }

    /// <summary>
    /// An Arabic reader gets the three views in Arabic, and the chart mirrors.
    /// </summary>
    /// <remarks>
    /// The chart's layout is the designer's, every rule of which is written in logical properties
    /// — <c>inset-inline-start</c>, <c>border-inline-end</c> — so it mirrors on the document's
    /// direction rather than on a second stylesheet. This asserts the direction actually reaches
    /// it, which is the part a rule cannot promise by itself.
    /// </remarks>
    [Fact]
    public async Task TheViewsAreInArabicAndTheChartMirrors()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSalesAsync(page);

        await page.EvaluateAsync("document.cookie = '.AspNetCore.Culture=c%3Dar%7Cuic%3Dar;path=/'");
        await page.ReloadAsync();

        await Assertions.Expect(page.Locator("#value-units-view-hierarchy")).ToContainTextAsync("الهيكل");
        await Assertions.Expect(page.Locator("#value-units-view-list")).ToContainTextAsync("قائمة");

        await page.ClickAsync("#value-units-view-hierarchy");

        await Assertions.Expect(page.Locator("#value-hierarchy")).ToBeVisibleAsync();

        var direction = await page.Locator("#value-hierarchy").EvaluateAsync<string>(
            "node => getComputedStyle(node).direction");

        direction.Should().Be("rtl", "the chart inherits the document's direction and mirrors on it");
    }

    /// <summary>
    /// Opens the Sales division's own page, entirely by clicking: Organisation → Divisions → All
    /// Divisions → the row for <c>demo-div-sales</c>.
    /// </summary>
    /// <remarks>
    /// <b>Through the list rather than straight off the menu, and that is about this tenant rather
    /// than about the feature.</b> The browser tenant is shared, and the move-and-merge tests add
    /// two divisions apiece to it; past fifteen the menu lists the first fifteen and offers
    /// <em>Show all</em>, so whether <em>Sales</em> has its own entry depends on how many tests ran
    /// first. Finding the row by its <b>code</b> is stable in a way a name in a capped list is not.
    ///
    /// The journey is no weaker for it — every step is still a click, and it also exercises the
    /// list screen's name link, which is the other route D23 C says must reach this page. The menu
    /// listing values is covered on a tenant nothing mutates, in
    /// <c>OrganisationMenuBrowserTests</c> and <c>OrganisationMenuTenantTests</c>.
    /// </remarks>
    private static async Task OpenSalesAsync(IPage page)
    {
        await AdminMenu.ClickAsync(page, "All Divisions", "Organisation", "Divisions");

        await page.Locator("tr.record-row[data-record-code='demo-div-sales'] a.record-details").ClickAsync();
    }
}
