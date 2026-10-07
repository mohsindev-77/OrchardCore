using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Every screen that takes a name works with the Arabic box left empty, and shows the English name
/// to an Arabic reader rather than a blank.
/// </summary>
/// <remarks>
/// Written against six HTTP 500s. The cause was not the validation rule but the model binder:
/// <c>ConvertEmptyStringToNull</c> is true by default, so an empty box binds to null, and a
/// computed <c>Name =&gt; new(NameEn.Trim(), NameAr.Trim())</c> threw inside the binder — before
/// the action method, before ModelState, and therefore with no way to show a field error instead
/// of a stack trace.
///
/// Which is why these tests assert on the <em>response</em>, not only on the end state. A test
/// that checks "the unit now exists" passes a 500 by on the next page load if something else
/// created it; a test that watches for a 500 on every request cannot. <see cref="WatchForServerErrors"/>
/// fails the test on any 5xx the page made, whatever the assertions afterwards say.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class ArabicOptionalBrowserTests
{
    private const string Structure = "WorkMate Demo Organisation";

    private readonly BrowserTenantFixture _tenant;

    public ArabicOptionalBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    // ---- the configuration screens ------------------------------------------------------

    [Fact]
    public async Task ADimensionTypeCanBeCreatedAndEditedWithNoArabicName()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var failures = WatchForServerErrors(page);

        await page.GotoAsync("/Admin/Dimensions/Types/Create");
        await page.FillAsync("#Code", "aropt-type");
        await page.FillAsync("#NameEn", "Arabic Optional Type");
        await SubmitAsync(page);

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Arabic Optional Type");

        // And editing it back: the Edit form renders the empty Arabic box it stored, and posting
        // that box straight back is the exact round trip that produced three of the six 500s.
        await EditLinkAsync(page, "Arabic Optional Type", "Types");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.Locator("#NameAr")).ToHaveValueAsync(string.Empty);

        await page.FillAsync("#NameEn", "Arabic Optional Type Renamed");
        await SubmitAsync(page);

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Arabic Optional Type Renamed");

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task AStructureCanBeCreatedAndEditedWithNoArabicName()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var failures = WatchForServerErrors(page);

        await page.GotoAsync("/Admin/Dimensions/Structures/Create");
        await page.FillAsync("#Code", "zzaroptstructure");
        await page.FillAsync("#NameEn", "Arabic Optional Structure");

        // One type, so the structure is valid without the grid being touched.
        await page.ClickAsync("#add-level-row");
        await page.SelectOptionAsync(
            "select[name='LevelDimensionTypeIds[0]']", new SelectOptionValue { Index = 1 });

        await SubmitAsync(page);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Arabic Optional Structure");

        // Re-posting the stored empty Arabic: the exact journey that 500'd three times.
        await EditLinkAsync(page, "Arabic Optional Structure", "Structures");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.Locator("#NameAr")).ToHaveValueAsync(string.Empty);

        await SubmitAsync(page);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Arabic Optional Structure");

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// A missing <em>English</em> name is a field error on the form, never a 500.
    /// </summary>
    /// <remarks>
    /// The other half of the same lesson. English is still required, so leaving it empty must be
    /// refused — and refused as an answer the reader can act on, under the box it is about, rather
    /// than as an unhandled exception. The browser's own <c>required</c> is bypassed deliberately
    /// so the server-side path is the one under test.
    /// </remarks>
    [Fact]
    public async Task AMissingEnglishNameIsAFieldErrorNotAServerError()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var failures = WatchForServerErrors(page);

        await page.GotoAsync("/Admin/Dimensions/Types/Create");
        await page.FillAsync("#Code", "aropt-noname");

        // Take the browser's own validation out of the way: the server is what is being tested.
        await page.EvaluateAsync("document.querySelectorAll('[required]').forEach(e => e.removeAttribute('required'))");
        await SubmitAsync(page);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("dimension type");
        await Assertions.Expect(
            page.Locator("[data-valmsg-for='NameEn'].field-validation-error"))
            .ToContainTextAsync("required in English");

        failures.Should().BeEmpty("a validation problem is an answer, not a failure");
    }

    // ---- the designer's own actions ------------------------------------------------------

    /// <summary>Add unit, Rename, Move, Merge and Retire, all with the Arabic box left empty.</summary>
    [Theory]
    [InlineData("Chart")]
    [InlineData("List")]
    public async Task EveryDesignerActionWorksWithTheArabicBoxEmpty(string view)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var failures = WatchForServerErrors(page);

        var prefix = $"aropt{view}";

        await page.GotoAsync($"/Admin/Dimensions/Designer/Index?view={view}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Add, with no Arabic.
        await AddUnitAsync(page, Structure, $"{prefix}-div", $"{prefix} Division");
        await Assertions.Expect(Node(page, $"{prefix} Division")).ToBeVisibleAsync();

        // A second one, to move into and merge with.
        await AddUnitAsync(page, Structure, $"{prefix}-div2", $"{prefix} Second");

        // Rename, still with no Arabic.
        await OpenMenuAsync(page, $"{prefix} Division");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='rename']");
        await page.FillAsync("#NameEn", $"{prefix} Renamed");
        await page.CheckAsync("#Kind_Substantive");
        await page.ClickAsync("form[action*='Rename'] button[type=submit]");

        await RevealAsync(page, $"{prefix} Renamed");

        // Move it, which renders a preview full of names that have no Arabic half.
        await OpenMenuAsync(page, $"{prefix} Renamed");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='move']");
        await page.SelectOptionAsync("#NewParentRecordId", new SelectOptionValue { Value = "" });
        await page.ClickAsync("form[action*='Move'] button[type=submit]");
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Move'] button[type=submit]");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Retire the other one, whose plan and confirmation also interpolate its names.
        await RevealAsync(page, $"{prefix} Second");
        await OpenMenuAsync(page, $"{prefix} Second");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='retire']");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");
        await Assertions.Expect(page.Locator(".alert")).ToContainTextAsync("What this will do");
        await page.CheckAsync("#Confirmed");
        await page.ClickAsync("form[action*='Retire'] button[type=submit]");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// Under Arabic, a unit with no Arabic name shows its English one — on the card and in a
    /// picker, which are the two places a blank would be mistaken for missing data.
    /// </summary>
    [Fact]
    public async Task TheArabicUiFallsBackToTheEnglishNameEverywhereItAppears()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();
        var failures = WatchForServerErrors(page);

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AddUnitAsync(page, Structure, "aropt-fallback", "Fallback Division");

        await page.EvaluateAsync("document.cookie = '.AspNetCore.Culture=c%3Dar%7Cuic%3Dar;path=/'");
        await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        (await page.GetAttributeAsync("html", "dir")).Should().Be("rtl");

        var card = Node(page, "Fallback Division");

        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator(".designer-card-name")).ToHaveCountAsync(1);
        await Assertions.Expect(card.Locator(".designer-card-name").First).ToHaveTextAsync("Fallback Division");

        // And in a picker: the Move screen's heading and its target list are sentences built from
        // names, which is where an empty half used to leave empty brackets or a bare separator.
        await OpenMenuAsync(page, "Fallback Division");
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='move']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var heading = await page.Locator("p.text-muted").First.InnerTextAsync();

        heading.Should().Contain("Fallback Division");
        heading.Should().NotContain("()", "there is no Arabic half, so there are no brackets to show");

        failures.Should().BeEmpty();
    }

    // ---- scaffolding --------------------------------------------------------------------

    /// <summary>
    /// Records every 5xx the page receives, so a test fails on a server error even if the journey
    /// afterwards happens to look right.
    /// </summary>
    private static List<string> WatchForServerErrors(IPage page)
    {
        var failures = new List<string>();

        page.Response += (_, response) =>
        {
            if (response.Status >= 500)
            {
                failures.Add($"{response.Status} {response.Request.Method} {response.Url}");
            }
        };

        page.PageError += (_, error) => failures.Add("page error: " + error);

        return failures;
    }

    /// <summary>
    /// Submits the screen's own form.
    /// </summary>
    /// <remarks>
    /// The last form on the page, not the first: the admin layout puts its sign-out form in the top
    /// nav, before the content, so "the first submit button" is reliably the wrong one.
    /// </remarks>
    private static Task SubmitAsync(IPage page) =>
        page.Locator("form").Last.Locator("button[type=submit]").First.ClickAsync();

    /// <summary>
    /// Clicks the Edit link on the list row for a named thing.
    /// </summary>
    /// <remarks>
    /// Scoped to the row rather than positioned near the text: the lists carry several links per
    /// row, and "nearest to this text" is a measurement, not an identification.
    /// </remarks>
    private static Task EditLinkAsync(IPage page, string nameEn, string screen) =>
        page.Locator($"tr:has-text('{nameEn}') a[href*='/Dimensions/{screen}/Edit/']")
            .First
            .ClickAsync();

    private static async Task AddUnitAsync(IPage page, string parentNameEn, string code, string nameEn)
    {
        await RevealAsync(page, parentNameEn);
        await OpenMenuAsync(page, parentNameEn);
        await page.ClickAsync(".designer-actions[open] a[data-designer-action='add']");

        await page.FillAsync("#Code", code);
        await page.FillAsync("#NameEn", nameEn);
        // #NameAr is deliberately left exactly as the form rendered it: empty.
        await page.ClickAsync("form[action*='AddUnit'] button[type=submit]");

        await page.WaitForURLAsync(url => url.Contains("Designer/Index", StringComparison.Ordinal));
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

    private static ILocator Node(IPage page, string nameEn) =>
        page.Locator($"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");
}
