using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Stage C driven the way a person drives it: dragging a card onto another, picking from the
/// keyboard list, and confirming a preview before anything changes.
/// </summary>
/// <remarks>
/// Drag is the reason this file exists. The service tests prove what a move does to the dated
/// tree; nothing but a browser can say whether a drag of a card is told apart from a drag of the
/// canvas, whether the drop target is visible, or whether Escape gets you out — and all three are
/// the difference between a usable chart and one that reorganises the company by accident.
///
/// Every test builds its own divisions and works only inside them. The tenant is shared by the
/// whole collection and these tests add, move, merge and retire; hanging them all off the demo
/// organisation's own Retail made each one depend on which of the others had run first, and the
/// failures that produced were about test order rather than about the product.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class DesignerMoveMergeBrowserTests
{
    private const string Structure = "WorkMate Demo Organisation";
    private const string Division = "Sales";
    private const string Department = "Retail";

    private readonly BrowserTenantFixture _tenant;

    public DesignerMoveMergeBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    // ---- move ---------------------------------------------------------------------------

    /// <summary>
    /// The keyboard route, in both views: the menu's "Move to…", a preview, then a confirmation.
    /// </summary>
    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task AUnitCanBeMovedFromTheActionMenuAfterSeeingWhatItWillDo(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        var world = await GivenMyOwnBranchAsync(page, $"mvmenu{view}", view);

        await RevealAsync(page, world.Department);
        await OpenMenuAsync(page, world.Department);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='move']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Move a unit");

        await page.SelectOptionAsync("#NewParentRecordId",
            new SelectOptionValue { Label = $"{world.OtherDivision} ({world.Prefix}-div2)" });
        await page.ClickAsync("form[action*='Move'] button[type=submit]");

        // The preview comes first, every time, and names both ends of the journey.
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync(world.Division);

        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Move'] button[type=submit]");

        await RevealAsync(page, world.Department);

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// Dragging one card onto another offers the move. It does not perform it: the drop lands on
    /// the same preview the menu does, because a drag is the easiest of these to do by accident.
    /// </summary>
    [Fact]
    public async Task DraggingACardOntoAnotherOpensTheMovePreviewForThatParent()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        var world = await GivenMyOwnBranchAsync(page, "dragpv");

        await FitToScreenAsync(page);
        await DragOntoAsync(page, Node(page, world.Department), Node(page, world.OtherDivision));

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Move a unit");
        await Assertions.Expect(page.Locator("#NewParentRecordId")).ToBeVisibleAsync();

        // Nothing was written by the drag itself: it is still where it was.
        await page.GoBackAsync();
        await RevealAsync(page, world.Department);

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// The gesture that must not fire a move: dragging the canvas. It pans, and leaves the page
    /// where it was.
    /// </summary>
    [Fact]
    public async Task DraggingEmptyCanvasPansTheChartAndStartsNoMove()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, "Chart");

        var before = page.Url;
        var scrolledBefore = await ScrollLeftAsync(page);

        var viewport = await page.Locator(".designer-chart-viewport").BoundingBoxAsync();

        // Along the very top of the viewport, which is connector space rather than a card.
        await page.Mouse.MoveAsync((float)(viewport!.X + viewport.Width - 12), (float)(viewport.Y + 6));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(
            (float)(viewport.X + 40), (float)(viewport.Y + 6), new MouseMoveOptions { Steps = 12 });
        await page.Mouse.UpAsync();

        page.Url.Should().Be(before, "panning the canvas never navigates");
        (await ScrollLeftAsync(page)).Should().NotBe(scrolledBefore, "it scrolled instead");
    }

    /// <summary>A twitch is not a drag, and Escape abandons one that has started.</summary>
    [Fact]
    public async Task ASmallTwitchIsNotADragAndEscapeAbandonsOneInProgress()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        var world = await GivenMyOwnBranchAsync(page, "twitch");

        // Fitted to the screen so both ends of the drag are visible at once: measuring one and
        // then scrolling to the other moves the first out from under the coordinates just taken.
        await FitToScreenAsync(page);

        var start = await CentreOfAsync(Node(page, world.Department));

        // Three pixels: under the threshold, so no drag begins.
        await page.Mouse.MoveAsync(start.X, start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(start.X + 3, start.Y, new MouseMoveOptions { Steps = 3 });

        await Assertions.Expect(page.Locator(".designer-dragging")).ToHaveCountAsync(0);

        await page.Mouse.UpAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Organisation designer");

        // Now a real drag, abandoned with Escape before letting go.
        var target = await CentreOfAsync(Node(page, world.OtherDivision));

        await page.Mouse.MoveAsync(start.X, start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(target.X, target.Y, new MouseMoveOptions { Steps = 12 });

        await Assertions.Expect(page.Locator(".designer-dragging")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".designer-drop-target")).ToHaveCountAsync(1);

        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();

        await Assertions.Expect(page.Locator(".designer-drop-target")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Organisation designer");
    }

    // ---- merge --------------------------------------------------------------------------

    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task AUnitCanBeMergedIntoAnotherAfterSeeingWhatItWillDo(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        var world = await GivenMyOwnBranchAsync(page, $"mrg{view}", view);

        // A second department of the same kind, to merge the first into.
        var survivor = $"{world.Prefix} Survivor";

        await AddUnitUnderAsync(page, world.OtherDivision, $"{world.Prefix}-dept2", survivor);

        await RevealAsync(page, world.Department);
        await OpenMenuAsync(page, world.Department);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='merge']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Merge a unit into another");

        await page.SelectOptionAsync("#TargetRecordId",
            new SelectOptionValue { Label = $"{survivor} ({world.Prefix}-dept2)" });
        await page.ClickAsync("form[action*='Merge'] button[type=submit]");

        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("will be retired");

        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Merge'] button[type=submit]");

        await RevealAsync(page, survivor);
        await Assertions.Expect(Node(page, world.Department)).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    // ---- cancel move --------------------------------------------------------------------

    [Fact]
    public async Task AMoveCanBeCancelledWithAReasonAfterSeeingWhatItWillDo()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        var world = await GivenMyOwnBranchAsync(page, "cancel");

        // Move it, so there is a move on record to take back.
        await OpenMenuAsync(page, world.Department);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='move']");
        await page.SelectOptionAsync("#NewParentRecordId",
            new SelectOptionValue { Label = $"{world.OtherDivision} ({world.Prefix}-div2)" });
        await page.ClickAsync("form[action*='Move'] button[type=submit]");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Move'] button[type=submit]");

        await RevealAsync(page, world.Department);

        await RevealAsync(page, world.Department);
        await OpenMenuAsync(page, world.Department);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='cancelmove']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Cancel a move");

        // The newest move is the one just made.
        await page.SelectOptionAsync("#EffectiveFrom", new SelectOptionValue { Index = 1 });
        await page.ClickAsync("form[action*='CancelMove'] button[type=submit]");

        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");

        // Added and moved on the same day, so the move replaced the original placement rather
        // than displacing it. Cancelling therefore leaves the unit with no parent, and the
        // preview says so rather than promising a restoration it cannot perform.
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("Nothing was displaced by it");

        // Confirming without a reason gets nowhere: a cancellation is never anonymous. The
        // browser stops it at the required field; that the service refuses it independently is
        // covered in DesignerMoveMergeTenantTests, which no browser can reach past this.
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='CancelMove'] button[type=submit]");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Cancel a move");
        (await page.EvaluateAsync<bool>("() => document.querySelector('#Reason').validity.valueMissing"))
            .Should().BeTrue("the reason is required and empty, so nothing was posted");

        await page.FillAsync("#Reason", "entered against the wrong unit");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='CancelMove'] button[type=submit]");

        // Off the tree entirely: the move is gone from the record and nothing took its place.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(Unplaced(page, world.Department)).ToBeVisibleAsync();

        problems.Should().BeEmpty();
    }

    // ---- getting back out of the unplaced panel -------------------------------------------

    /// <summary>
    /// An unplaced unit is put back on the tree from its own action menu, in both views and for
    /// both of the ways a unit ends up with no parent.
    /// </summary>
    /// <remarks>
    /// This is the case the suite used to be blind to. The unplaced panel was bespoke list markup
    /// rather than the card partial, so it had no action menu and could not be dragged — and
    /// because every locator in these files asks for a <c>.designer-node</c>, which those rows
    /// were not, no test ever put the question to it. "Moving into and out of the unplaced panel
    /// works" was true of the service and false of the screen, and nothing said so.
    /// </remarks>
    [Theory]
    [InlineData("Chart", false)]
    [InlineData("List", false)]
    [InlineData("Chart", true)]
    [InlineData("List", true)]
    public async Task AnUnplacedUnitCanBePlacedBackOnTheTreeFromItsActionMenu(string view, bool orphaned)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        var stranded = await GivenAUnitWithNoParentAsync(page, $"place{view}{orphaned}", orphaned, view);

        await Assertions.Expect(Unplaced(page, stranded)).ToBeVisibleAsync();

        // The whole point: the card in the panel offers the same menu as one on the tree, and the
        // label on it says "Place under…" because "move" is the wrong word for something that is
        // not anywhere yet.
        await OpenUnplacedMenuAsync(page, stranded);
        await Assertions.Expect(
            page.Locator(".designer-actions[open] a[data-designer-action='move']"))
            .ToHaveTextAsync("Place under…");

        await page.ClickAsync(".designer-actions[open] a[data-designer-action='move']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Move a unit");

        await page.SelectOptionAsync("#NewParentRecordId",
            new SelectOptionValue { Label = HomeFor(orphaned) });
        await page.ClickAsync("form[action*='Move'] button[type=submit]");

        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");

        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Move'] button[type=submit]");

        // On the tree, out of the panel, and with the parent-retired mark gone: placing it is what
        // answers the question the badge was asking. Scoped to this card, because the panel holds
        // other tests' strandings too and their badges are none of this one's business.
        await RevealAsync(page, stranded);
        await Assertions.Expect(Unplaced(page, stranded)).ToHaveCountAsync(0);
        await Assertions.Expect(Node(page, stranded).Locator("[data-designer-orphaned]")).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    /// <summary>The drag route out of the panel: onto a tree card, and on to the same preview.</summary>
    [Theory]
    [InlineData("Chart", false)]
    [InlineData("List", false)]
    [InlineData("Chart", true)]
    [InlineData("List", true)]
    public async Task AnUnplacedCardCanBeDraggedOntoATreeCardToOpenThePlacementPreview(string view, bool orphaned)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        // A window big enough to hold both ends of the drag at once. A drag is one gesture across
        // two columns, and a pointer sent to a card that is scrolled out of the window lands on
        // nothing: scrolling one end into view is what pushes the other end out.
        await page.SetViewportSizeAsync(1600, 1400);

        var stranded = await GivenAUnitWithNoParentAsync(page, $"dragplace{view}{orphaned}", orphaned, view);
        var home = orphaned ? Department : Division;

        await RevealAsync(page, home);

        // Both ends brought into view rather than the whole chart zoomed to fit: the panel is a
        // column beside the chart, so a drag between them crosses the gap at whatever size the
        // cards already are, and shrinking a tenant-wide tree to fit makes them too small to aim at.
        await Node(page, home).ScrollIntoViewIfNeededAsync();
        await Unplaced(page, stranded).ScrollIntoViewIfNeededAsync();

        var from = await CentreOfAsync(Unplaced(page, stranded));
        var onto = await CentreOfAsync(Node(page, home));

        await page.Mouse.MoveAsync(from.X, from.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(onto.X, onto.Y, new MouseMoveOptions { Steps = 15 });

        // Asserted before letting go, so that a drag which never took hold of the card or never
        // found the card under it fails saying which, rather than as a page that did not navigate.
        await Assertions.Expect(Unplaced(page, stranded)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("designer-dragging"));
        await Assertions.Expect(Node(page, home)).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("designer-drop-target"));

        await page.Mouse.UpAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Move a unit");
        await Assertions.Expect(page.Locator("#NewParentRecordId")).ToBeVisibleAsync();

        // The drag opened the preview and wrote nothing: it is still in the panel.
        await page.GoBackAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(Unplaced(page, stranded)).ToBeVisibleAsync();

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// The check that would have caught all of this: every card a user with edit permissions can
    /// act on carries an action menu, wherever on the screen it is drawn.
    /// </summary>
    /// <remarks>
    /// Deliberately not a list of the cards that ought to have one. It asks the page for every
    /// <c>.designer-node</c> there is — tree, unplaced panel, both — and requires a menu on each,
    /// so a card drawn somewhere nobody thought of is covered by the same assertion rather than by
    /// a test somebody remembers to write.
    /// </remarks>
    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task EveryCardOnTheScreenHasAnActionMenuForAUserWhoMayEdit(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page, view);
        await ExpandAsync(page, Division);

        var menuless = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('.designer-node'))
                .filter(node => !node.closest('template'))
                .filter(node => !node.querySelector(':scope > .designer-card .designer-actions'))
                .map(node => node.querySelector(':scope > .designer-card .designer-card-name')?.textContent?.trim() ?? node.dataset.recordId)
            """);

        menuless.Should().BeEmpty(
            "a card a user may act on has an action menu, in the tree and in the unplaced panel alike");

        // And the panel's cards really are in it, so the assertion above had something to find.
        (await page.Locator("#designer-unplaced .designer-node").CountAsync())
            .Should().BeGreaterThan(0);
    }

    // ---- scaffolding --------------------------------------------------------------------

    private sealed record Branch(string Prefix, string Division, string OtherDivision, string Department);

    /// <summary>
    /// Puts one unit of this test's own into the unplaced panel, by whichever of the two routes
    /// the caller asked for, and answers with its name.
    /// </summary>
    /// <remarks>
    /// The two are not the same card. A unit retired out from under its children carries the
    /// "Parent retired" badge and a sentence naming the parent; a unit moved off the tree on
    /// purpose carries neither. Both have to be placeable, which is why both are tested.
    ///
    /// Deliberately small. It adds one department — and for the orphan case one section under it
    /// — and hangs them off the demo's own Sales, rather than building fresh divisions. The chart
    /// tests in OrganisationDesignerBrowserTests open every branch of this same shared tenant and
    /// measure the result, so every division a test invents is a row they have to expand and a
    /// column they have to fit on screen. Adding to an existing branch costs them far less.
    /// </remarks>
    private static async Task<string> GivenAUnitWithNoParentAsync(
        IPage page, string prefix, bool orphaned, string view = "Chart")
    {
        await OpenDesignerAsync(page, view);

        var department = $"{prefix} Dept";

        await AddUnitUnderAsync(page, Division, $"{prefix}-dept", department);

        if (!orphaned)
        {
            await RevealAsync(page, department);
            await OpenMenuAsync(page, department);
            await page.ClickAsync(".designer-actions[open] a[data-designer-action='move']");

            // The empty option: no parent at all, which is the panel rather than the top row.
            await page.SelectOptionAsync("#NewParentRecordId", new SelectOptionValue { Value = "" });
            await page.ClickAsync("form[action*='Move'] button[type=submit]");
            await page.CheckAsync("#Confirmed");
            await page.ClickAsync("form[action*='Move'] button[type=submit]");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var moved = Unplaced(page, department);

            await Assertions.Expect(moved).ToBeVisibleAsync();

            // No "Parent retired" badge on this one: nothing retired, somebody moved it. Which of
            // the two sentences the panel then shows is covered in DesignerActionsBrowserTests.
            await Assertions.Expect(moved.Locator("[data-designer-orphaned]")).ToHaveCountAsync(0);

            return department;
        }

        var section = $"{prefix} Section";

        await AddUnitUnderAsync(page, department, $"{prefix}-sec", section);

        await RevealAsync(page, department);
        await OpenMenuAsync(page, department);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='retire']");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");

        await page.CheckAsync("#Disposition_Unplaced_0");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var stranded = Unplaced(page, section);

        await Assertions.Expect(stranded.Locator("[data-designer-orphaned]")).ToBeVisibleAsync();
        await Assertions.Expect(stranded).ToContainTextAsync(department);

        return section;
    }

    /// <summary>
    /// Where a stranded unit of each kind is allowed to go: a section under a department, a
    /// department under a division. The picker lists only what this structure's levels permit, so
    /// offering a section a division to sit under would simply not be there to choose.
    /// </summary>
    private static string HomeFor(bool orphaned) =>
        orphaned ? $"{Department} (demo-dept-retail)" : $"{Division} (demo-div-sales)";

    /// <summary>Opens the action menu on a card in the unplaced panel.</summary>
    private static async Task OpenUnplacedMenuAsync(IPage page, string nameEn)
    {
        var menu = Unplaced(page, nameEn).Locator(".designer-actions").First;

        await menu.Locator("summary").ClickAsync();
        await Assertions.Expect(menu).ToHaveAttributeAsync(
            "open", new System.Text.RegularExpressions.Regex(".*"));
    }

    /// <summary>
    /// Two divisions of this test's own and a department under the first, built through the screen
    /// so that nothing it does afterwards can disturb another test — or be disturbed by one.
    /// </summary>
    private static async Task<Branch> GivenMyOwnBranchAsync(IPage page, string prefix, string view = "Chart")
    {
        await OpenDesignerAsync(page, view);

        var division = $"{prefix} First";
        var other = $"{prefix} Second";
        var department = $"{prefix} Dept";

        await AddUnitUnderAsync(page, Structure, $"{prefix}-div1", division);
        await AddUnitUnderAsync(page, Structure, $"{prefix}-div2", other);
        await AddUnitUnderAsync(page, division, $"{prefix}-dept", department);

        return new Branch(prefix, division, other, department);
    }

    private static async Task OpenDesignerAsync(IPage page, string view)
    {
        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Brings a card on screen by searching for it, whatever shape the tree is in.
    /// </summary>
    /// <remarks>
    /// Walking down from the roots clicking toggles was the obvious way and the wrong one: the
    /// page these tests land on after an action is already opening a branch of its own, so a test
    /// that reads "collapsed" and clicks can arrive a moment after the script opened it and close
    /// it again. Search is the product's own answer to "show me this unit" — it expands exactly
    /// the path needed and nothing else — so using it here is both steadier and one more thing
    /// these tests cover rather than assume.
    /// </remarks>
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

    /// <summary>Reveals a card and makes sure its own branch is open.</summary>
    private static async Task ExpandAsync(IPage page, string nameEn)
    {
        await RevealAsync(page, nameEn);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await Node(page, nameEn).First.GetAttributeAsync("data-expanded") == "true" ||
                await Node(page, nameEn).First.GetAttributeAsync("data-child-count") == "0")
            {
                return;
            }

            await Toggle(page, nameEn).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }

    private static async Task OpenMenuAsync(IPage page, string nameEn)
    {
        var menu = Node(page, nameEn).Locator(".designer-actions").First;

        if (await menu.GetAttributeAsync("open") is null)
        {
            await menu.Locator("summary").ClickAsync();
        }

        await Assertions.Expect(menu).ToHaveAttributeAsync("open", new System.Text.RegularExpressions.Regex(".*"));
    }

    private static async Task AddUnitUnderAsync(IPage page, string parentNameEn, string code, string nameEn)
    {
        await RevealAsync(page, parentNameEn);
        await OpenMenuAsync(page, parentNameEn);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='add']");

        await page.FillAsync("#Code", code);
        await page.FillAsync("#NameEn", nameEn);
        await page.FillAsync("#NameAr", "وحدة");
        await page.ClickAsync("form[action*='AddUnit'] button[type=submit]");

        await page.WaitForURLAsync(url => url.Contains("Designer/Index", StringComparison.Ordinal));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Zooms the chart until the whole of it fits, which is what makes two cards measurable in one
    /// go: scrolling to the second otherwise moves the first out from under the coordinates just
    /// taken for it.
    /// </summary>
    private static async Task FitToScreenAsync(IPage page)
    {
        await page.ClickAsync("[data-designer-zoom='fit']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static Task<int> ScrollLeftAsync(IPage page) =>
        page.EvaluateAsync<int>("() => document.querySelector('.designer-chart-viewport').scrollLeft");

    /// <summary>
    /// A deliberate drag: past the threshold, in steps, so the handler sees the travel it is
    /// looking for rather than one instantaneous jump.
    /// </summary>
    private static async Task DragOntoAsync(IPage page, ILocator source, ILocator target)
    {
        var from = await CentreOfAsync(source);
        var onto = await CentreOfAsync(target);

        await page.Mouse.MoveAsync(from.X, from.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(onto.X, onto.Y, new MouseMoveOptions { Steps = 15 });
        await page.Mouse.UpAsync();

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Where to put the pointer to take hold of a card: on its name.
    /// </summary>
    /// <remarks>
    /// Not the geometric centre of the card. The action menu is absolutely positioned over the
    /// card's corner, and on a narrow card — the unplaced panel's are a third the width of the
    /// chart's — it reaches the middle. A pointerdown that lands on the menu is a pointerdown on a
    /// <c>&lt;summary&gt;</c>, which the handler deliberately ignores, so the drag never starts and
    /// the test fails for a reason that has nothing to do with dragging.
    /// </remarks>
    private static async Task<(float X, float Y)> CentreOfAsync(ILocator node)
    {
        var grip = node.Locator(".designer-card .designer-card-name").First;
        var box = await grip.BoundingBoxAsync();

        return ((float)(box!.X + (box.Width / 2)), (float)(box.Y + (box.Height / 2)));
    }

    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

    private static ILocator Toggle(IPage page, string nameEn) =>
        Node(page, nameEn).Locator(".designer-toggle").First;

    private static ILocator Unplaced(IPage page, string nameEn) =>
        page.Locator($"#designer-unplaced .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

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

        return problems;
    }
}
