using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The organisation designer on a copy of the developer's own tenant, in whichever real browser
/// this machine has installed.
/// </summary>
/// <remarks>
/// Everything here is also covered on a fresh tenant by
/// <see cref="OrganisationDesignerBrowserTests"/>, and that is the suite to extend when adding a
/// feature. This one exists because a tenant with history is a different thing: older settings,
/// structures made by hand as well as by recipe, schema that arrived through upgrades rather than
/// through <c>CreateAsync</c>. A defect reported from a real browser on a real tenant gets a test
/// against a real tenant.
/// </remarks>
[Collection(UsesTheRealDataTenant.Name)]
public sealed class RealDataDesignerBrowserTests
{
    private const string StructureName = "WorkMate Demo Organisation";
    private const string Division = "Sales";
    private const string Department = "Retail";
    private static readonly string[] Sections = ["In-Store Sales", "Online Sales"];

    private readonly RealDataTenantFixture _tenant;

    public RealDataDesignerBrowserTests(RealDataTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Division to department to section, in both views, in both cultures, driven both ways a
    /// person reaches for: the little control, and the card itself. The card is how most people
    /// try first, and until this test existed it did nothing at all.
    /// </summary>
    [RealDataTheory]
    [InlineData("Chart", "en", "control")]
    [InlineData("Chart", "en", "card")]
    [InlineData("Chart", "ar", "control")]
    [InlineData("Chart", "ar", "card")]
    [InlineData("List", "en", "control")]
    [InlineData("List", "en", "card")]
    [InlineData("List", "ar", "control")]
    [InlineData("List", "ar", "card")]
    public async Task ADivisionExpandsToItsDepartmentsAndADepartmentToItsSections(
        string view, string culture, string clickTarget)
    {
        if (_tenant.SkipReason is not null)
        {
            // The attribute already skipped this for a missing environment variable; this covers
            // the case it cannot see at discovery, which is data that holds no demo organisation.
            return;
        }

        await using var context = await _tenant.SignedInContextAsync();
        await SetCultureAsync(context, culture);

        var page = await context.NewPageAsync();
        var problems = Watch(page);

        await OpenTheDemoStructureAsync(page, view);

        await Assertions.Expect(Node(page, StructureName)).ToBeVisibleAsync();
        await Assertions.Expect(Node(page, Department)).ToHaveCountAsync(0);

        await Expand(page, Division, clickTarget);
        await Assertions.Expect(Node(page, Department)).ToBeVisibleAsync();

        await Expand(page, Department, clickTarget);

        foreach (var section in Sections)
        {
            await Assertions.Expect(Node(page, section)).ToBeVisibleAsync();
        }

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// The defect that made all of the above invisible for a month at a time: the designer's script
    /// and stylesheet were served with <c>cache-control: max-age=2592000</c> at a URL that never
    /// changed, so a browser that had fetched them once kept running them after every rebuild,
    /// without even asking the server. Rendering new HTML against a month-old script is the failure
    /// that looks exactly like "the server code is fine and nothing works".
    /// </summary>
    [RealDataFact]
    public async Task TheDesignersScriptAndStylesheetCarryAVersionTokenSoARebuildReachesTheBrowser()
    {
        if (_tenant.SkipReason is not null)
        {
            // The attribute already skipped this for a missing environment variable; this covers
            // the case it cannot see at discovery, which is data that holds no demo organisation.
            return;
        }

        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Dimensions/Designer/Index");

        var assets = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('script[src], link[rel=stylesheet]')]"
            + ".map(n => n.getAttribute('src') || n.getAttribute('href'))"
            + ".filter(u => u && u.indexOf('WorkMate.') >= 0)");

        assets.Should().NotBeEmpty("the designer loads a stylesheet and a script of its own");

        assets.Should().AllSatisfy(url => url.Should().Contain(
            "?v=",
            "every WorkMate asset must be versioned, or a browser keeps the old copy for 30 days"));
    }

    /// <summary>
    /// The date on the wire is the date on the page, whatever culture the admin is in. An
    /// <c>&lt;input type="date"&gt;</c> always submits ISO-8601, but it <em>displays</em> in the
    /// browser's locale — 05/10/2026 and 10/05/2026 are the same day shown two ways — so a
    /// culture-sensitive parse anywhere in the round trip would silently resolve the tree as at a
    /// different day for an Arabic user than for an English one.
    /// </summary>
    [RealDataTheory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task TheDateTheBrowserSendsIsTheDateThePageShowsInEitherCulture(string culture)
    {
        if (_tenant.SkipReason is not null)
        {
            // The attribute already skipped this for a missing environment variable; this covers
            // the case it cannot see at discovery, which is data that holds no demo organisation.
            return;
        }

        await using var context = await _tenant.SignedInContextAsync();
        await SetCultureAsync(context, culture);

        var page = await context.NewPageAsync();
        var childrenRequests = new List<string>();

        page.Request += (_, request) =>
        {
            if (request.Url.Contains("Designer/Children", StringComparison.OrdinalIgnoreCase))
            {
                childrenRequests.Add(request.Url);
            }
        };

        await OpenTheDemoStructureAsync(page, "Chart");

        var pageDate = await page.Locator(".designer-surface").First.GetAttributeAsync("data-as-at");

        pageDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$", "the page carries an ISO-8601 date");

        await Expand(page, Division, "control");
        await Assertions.Expect(Node(page, Department)).ToBeVisibleAsync();

        childrenRequests.Should().NotBeEmpty();
        childrenRequests.Should().AllSatisfy(url =>
            url.Should().Contain($"asAt={pageDate}", "the browser sends back exactly the date the page is showing"));
    }

    private static async Task OpenTheDemoStructureAsync(IPage page, string view)
    {
        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");

        // Named explicitly rather than trusting whichever structure happens to sort first: this
        // tenant has structures made by hand as well as the demo's. Read through textContent, not
        // innerText — an <option> is not laid out, so it has no rendered text to read.
        var options = await page.Locator("#structureId option").EvaluateAllAsync<string[][]>(
            "nodes => nodes.map(n => [n.value, n.textContent.trim()])");

        var match = options.FirstOrDefault(option => option[1].Contains(StructureName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The tenant has no '{StructureName}' structure. Structures offered: "
                + string.Join(" / ", options.Select(option => option[1])));

        // Changing the structure submits the toolbar form, so the page this returns on is a new one.
        await page.SelectOptionAsync("#structureId", match[0]);
        await page.WaitForURLAsync(url => url.Contains($"structureId={match[0]}", StringComparison.Ordinal));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task Expand(IPage page, string nameEn, string clickTarget)
    {
        var target = clickTarget == "card"
            ? Node(page, nameEn).Locator(".designer-card-body").First
            : Node(page, nameEn).Locator(".designer-toggle").First;

        await target.ClickAsync();
    }

    private static async Task SetCultureAsync(IBrowserContext context, string culture) =>
        await context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = ".AspNetCore.Culture",
                Value = $"c={culture}|uic={culture}",
                Domain = "127.0.0.1",
                Path = "/",
            },
        ]);

    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");

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
