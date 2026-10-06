using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The designer's three mutations driven the way a person drives them: open the card's action
/// menu, fill in the form it leads to, save, and look at the chart.
/// </summary>
/// <remarks>
/// The service tests in WorkMate.Integration.Tests prove what each operation does to the dated
/// tree, and they are the right place for that. What they cannot see is the journey: whether the
/// menu opens, whether the link goes anywhere, whether the form posts what the controller binds,
/// and whether the card a user was looking at actually changes afterwards. Every one of those can
/// break while the service stays perfectly correct.
///
/// Each test makes and uses its own unit rather than touching the demo organisation's, so the
/// suite's other tests keep seeing the tree the demo recipe describes.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class DesignerActionsBrowserTests
{
    private const string Division = "Sales";
    private const string Department = "Retail";

    private readonly BrowserTenantFixture _tenant;

    public DesignerActionsBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// From either view. The brief requires every action to be reachable from both, and they are
    /// the same cards rendered from the same partial — which is exactly the kind of claim that
    /// stays true until a stylesheet scoped to one of them quietly hides something.
    /// </summary>
    [Theory]
    [InlineData("Chart", "browser-sec-added", "Browser Added Section")]
    [InlineData("List", "browser-sec-added-list", "Browser Added In List")]
    public async Task AddingAUnitFromACardsActionMenuPutsItOnTheChart(
        string view, string code, string name)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await OpenDesignerAsync(page, view);
        await ExpandToDepartmentAsync(page);

        await Assertions.Expect(Node(page, name)).ToHaveCountAsync(0);

        await AddUnitUnderAsync(page, Department, code, name, "قسم مضاف");

        // The branch reopens by itself on the way back, so the new card is on screen without
        // anyone having to go and find it.
        await Assertions.Expect(Node(page, name)).ToBeVisibleAsync();

        problems.Should().BeEmpty();
    }

    [Fact]
    public async Task RenamingAUnitFromItsActionMenuChangesTheCard()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await OpenDesignerAsync(page);
        await ExpandToDepartmentAsync(page);
        await AddUnitUnderAsync(page, Department, "browser-sec-rename", "Before Rename", "قبل");

        await RevealAsync(page, "Before Rename");
        await OpenMenuAsync(page, "Before Rename");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='rename']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Rename a unit");

        await page.FillAsync("#NameEn", "After Rename");
        await page.FillAsync("#NameAr", "بعد");
        await page.CheckAsync("#Kind_Substantive");
        await page.ClickAsync("form[action*='Rename'] button[type=submit]");

        await Assertions.Expect(Node(page, "After Rename")).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, "Before Rename")).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// Retiring shows what it will do and waits to be confirmed, then the card goes.
    /// </summary>
    [Fact]
    public async Task RetiringAUnitFromItsActionMenuAsksFirstAndThenTakesItOffTheChart()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await OpenDesignerAsync(page);
        await ExpandToDepartmentAsync(page);
        await AddUnitUnderAsync(page, Department, "browser-sec-retire", "Doomed Section", "محكوم");

        await RevealAsync(page, "Doomed Section");
        await OpenMenuAsync(page, "Doomed Section");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='retire']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Retire a unit");

        // Nothing happens on the first post: it reports what retiring would do and waits.
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");
        await Assertions.Expect(page.Locator("#Confirmed")).ToBeVisibleAsync();

        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");

        await ExpandToDepartmentAsync(page);
        await Assertions.Expect(Node(page, "Doomed Section")).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    // ---- retiring a unit that still has units under it ------------------------------------

    /// <summary>
    /// The screen will not retire a unit over live children until somebody says what happens to
    /// them. Confirming without choosing gets the question back, not a retirement.
    /// </summary>
    [Fact]
    public async Task RetiringAUnitWithChildrenAsksWhatHappensToThemAndWillNotProceedWithoutAnAnswer()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await GivenADepartmentWithASectionAsync(page, "nochoice", "No Choice Dept", "No Choice Section");

        await OpenRetireFormAsync(page, "No Choice Dept");
        await CheckWhatThisWillDoAsync(page);

        await Assertions.Expect(page.Locator("#Disposition_Cascade_0")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#Disposition_Unplaced_0")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("No Choice Section")).ToBeVisibleAsync();

        // Confirm without answering the question.
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");

        // Back on the form with the question unanswered, rather than on the designer with a unit
        // retired and a section stranded.
        await Assertions.Expect(page.Locator(".text-danger").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Retire a unit");

        problems.Should().BeEmpty();
    }

    /// <summary>(a) The children move somewhere else on the day their own parent closes.</summary>
    [Fact]
    public async Task TheChildrenCanBeMovedToAnotherParent()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await GivenADepartmentWithASectionAsync(page, "movekids", "Move Kids Dept", "Move Kids Section");

        await OpenRetireFormAsync(page, "Move Kids Dept");
        await CheckWhatThisWillDoAsync(page);

        await page.CheckAsync("#Disposition_Move_0");
        await page.SelectOptionAsync("#Dispositions_0__NewParentRecordId", new SelectOptionValue { Label = "Retail (demo-dept-retail)" });
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");

        // Under its new parent, still active, and not stranded in the unplaced panel.
        await RevealAsync(page, "Move Kids Section");
        await Assertions.Expect(Node(page, "Move Kids Dept")).ToHaveCountAsync(0);
        await Assertions.Expect(Unplaced(page, "Move Kids Section")).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    /// <summary>(b) The children close with their parent.</summary>
    [Fact]
    public async Task TheChildrenCanBeRetiredWithTheirParent()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await GivenADepartmentWithASectionAsync(page, "cascadekids", "Cascade Dept", "Cascade Section");

        await OpenRetireFormAsync(page, "Cascade Dept");
        await CheckWhatThisWillDoAsync(page);

        await page.CheckAsync("#Disposition_Cascade_0");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(Node(page, "Cascade Dept")).ToHaveCountAsync(0);
        await Assertions.Expect(Node(page, "Cascade Section")).ToHaveCountAsync(0);

        // Retired, not stranded: a cascade leaves nothing on the unplaced panel.
        await Assertions.Expect(Unplaced(page, "Cascade Section")).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    /// <summary>(c) The children stay, with no parent, and the panel says why.</summary>
    [Fact]
    public async Task TheChildrenCanBeLeftUnplacedAndTheyCarryAParentRetiredBadge()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await GivenADepartmentWithASectionAsync(page, "orphankids", "Orphan Dept", "Orphan Section");

        await OpenRetireFormAsync(page, "Orphan Dept");
        await CheckWhatThisWillDoAsync(page);

        await page.CheckAsync("#Disposition_Unplaced_0");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");

        var stranded = Unplaced(page, "Orphan Section");

        await Assertions.Expect(stranded).ToBeVisibleAsync();
        await Assertions.Expect(stranded.Locator("[data-designer-orphaned]")).ToBeVisibleAsync();
        await Assertions.Expect(stranded).ToContainTextAsync("Parent retired");
        await Assertions.Expect(stranded).ToContainTextAsync("Orphan Dept");

        // The demo's own never-placed record is in the same panel and must not say the same thing.
        var neverPlaced = Unplaced(page, "Unassigned Department");

        await Assertions.Expect(neverPlaced).ToContainTextAsync("Never placed");
        await Assertions.Expect(neverPlaced.Locator("[data-designer-orphaned]")).ToHaveCountAsync(0);

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// The action menu is a <c>&lt;details&gt;</c> element, so it opens with no script at all —
    /// and opening it must not also expand the card it sits on.
    /// </summary>
    [Fact]
    public async Task OpeningACardsActionMenuDoesNotExpandTheCard()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenDesignerAsync(page);

        var sales = Node(page, Division);

        await OpenMenuAsync(page, Division);

        await Assertions.Expect(sales.Locator(".designer-actions-menu").First).ToBeVisibleAsync();
        (await sales.First.GetAttributeAsync("data-expanded")).Should().Be(
            "false", "opening the menu is not a request to open the branch");
    }

    private static async Task OpenDesignerAsync(IPage page, string view = "Chart")
    {
        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static Task ExpandToDepartmentAsync(IPage page) =>
        RevealAsync(page, Department);

    /// <summary>Opens one card's action menu, closing any other that is already open.</summary>
    private static async Task OpenMenuAsync(IPage page, string nameEn)
    {
        var menu = Node(page, nameEn).Locator(".designer-actions").First;

        if (await menu.GetAttributeAsync("open") is null)
        {
            await menu.Locator("summary").ClickAsync();
        }

        await Assertions.Expect(menu).ToHaveAttributeAsync("open", new System.Text.RegularExpressions.Regex(".*"));
    }

    private static async Task AddUnitUnderAsync(
        IPage page, string parentNameEn, string code, string nameEn, string nameAr)
    {
        await RevealAsync(page, parentNameEn);
        await OpenMenuAsync(page, parentNameEn);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='add']");

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Add a unit");

        await page.FillAsync("#Code", code);
        await page.FillAsync("#NameEn", nameEn);
        await page.FillAsync("#NameAr", nameAr);
        await page.ClickAsync("form[action*='AddUnit'] button[type=submit]");

        await page.WaitForURLAsync(url => url.Contains("Designer/Index", StringComparison.Ordinal));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// A department of our own under Sales, with a section under it: the demo organisation's own
    /// units are left alone so the rest of the suite keeps seeing the tree the recipe describes.
    /// </summary>
    private static async Task GivenADepartmentWithASectionAsync(
        IPage page, string prefix, string departmentName, string sectionName)
    {
        await OpenDesignerAsync(page);
        await RevealAsync(page, Division);

        await AddUnitUnderAsync(page, Division, $"{prefix}-dept", departmentName, "إدارة");
        await AddUnitUnderAsync(page, departmentName, $"{prefix}-sec", sectionName, "شعبة");
    }

    private static async Task OpenRetireFormAsync(IPage page, string nameEn)
    {
        await RevealAsync(page, nameEn);
        await OpenMenuAsync(page, nameEn);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='retire']");
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Retire a unit");
    }

    /// <summary>The first post, which assesses and comes back without having written anything.</summary>
    private static async Task CheckWhatThisWillDoAsync(IPage page)
    {
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");
    }

    /// <summary>
    /// Brings a card on screen by searching for it, whatever shape the tree is in.
    /// </summary>
    /// <remarks>
    /// Walking down from the roots clicking toggles was the obvious way and the wrong one: the
    /// page these tests land on after an action is already opening a branch of its own, so a test
    /// that reads "collapsed" and clicks can arrive a moment after the script opened it and close
    /// it again. Search is the product's own answer to "show me this unit".
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

    /// <summary>One card of the unplaced panel, found by the English name on it.</summary>
    /// <remarks>
    /// The same <c>.designer-node</c> the tree uses, because the panel renders the same partial.
    /// It was bespoke list markup once, and that is precisely why nothing here noticed that those
    /// cards had no action menu and could not be dragged: every locator in this suite asks for a
    /// <c>.designer-node</c>, and the unplaced rows were not one.
    /// </remarks>
    private static ILocator Unplaced(IPage page, string nameEn) =>
        page.Locator($"#designer-unplaced .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

    private static ILocator Toggle(IPage page, string nameEn) =>
        Node(page, nameEn).Locator(".designer-toggle").First;

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
