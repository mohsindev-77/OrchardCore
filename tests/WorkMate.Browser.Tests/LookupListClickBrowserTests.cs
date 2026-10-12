using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The whole lookup list journey, reached only by clicking, and the People menu in both languages.
/// </summary>
/// <remarks>
/// <b>One typed path, and everything after it is a control somebody can see and press.</b> That is
/// the rule <c>EmployeeLifecycleClickBrowserTests</c> was written to enforce, and the reason is
/// worth repeating because this slice is exactly the shape that trips it: the lookup lists have no
/// admin menu entry of their own by design, so every route to them is a link on another screen. A
/// test that navigated to <c>/Admin/Forms/Lookups/Index</c> would prove the screen works and say
/// nothing at all about whether anybody can get to it — which, for a screen deliberately hidden
/// behind another, is the only question that matters.
///
/// <b>The add and remove controls are round trips, not script</b>, like every repeating form in
/// this module. They are still exercised here rather than only over HTTP, because what this suite
/// can see and the integration suite cannot is whether the button a person would press is on the
/// page, enabled, and attached to the form it submits.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class LookupListClickBrowserTests
{
    private readonly BrowserTenantFixture _tenant;

    public LookupListClickBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Menu → Forms → Lookup lists → create a list with two entries → retire one → back to the list.
    /// </summary>
    [Fact]
    public async Task ALookupListIsCreatedAndRetiredEntirelyByClicking()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // The one navigation. Everything after this is a control somebody can see and press.
        await page.GotoAsync("/Admin");

        await OpenPeopleGroupAsync(page);
        await page.ClickAsync("a[href*='/Admin/Forms/Index']");

        await Assertions.Expect(page.Locator("#forms-lookup-lists")).ToBeVisibleAsync();

        // The Forms screen is the only way in to the lists, which is the product-owner constraint
        // this whole screen exists to honour.
        await page.ClickAsync("#forms-open-lookup-lists");
        await page.ClickAsync("#lookup-list-create");

        await Assertions.Expect(page.Locator("#lookup-list-editor")).ToBeVisibleAsync();

        await page.FillAsync("#lookup-list-code", "click-grade");
        await page.FillAsync("#lookup-list-editor input[name='NameEn']", "Grade");
        await page.FillAsync("#lookup-list-editor input[name='NameAr']", "الدرجة");

        await FillRowAsync(page, 0, "G1", "Grade 1", "الدرجة ١");

        // Adding a row is a round trip: the page comes back with a second row on it.
        await page.ClickAsync("#lookup-item-add");
        await Assertions.Expect(page.Locator("tr.lookup-item-row")).ToHaveCountAsync(2);

        await FillRowAsync(page, 1, "G2", "Grade 2", "الدرجة ٢");

        await page.ClickAsync("#lookup-list-save");

        var row = page.Locator("tr.lookup-list-row[data-lookup-list-code='click-grade']");

        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.Locator(".lookup-list-counts")).ToContainTextAsync("2");

        // Back into the editor by clicking the row's own Edit control.
        await row.Locator(".lookup-list-edit").ClickAsync();

        // A stored entry cannot be removed, so it is offered no Remove button — the affordance half
        // of ADR-0014's rule. Retiring is what is offered instead.
        await Assertions.Expect(page.Locator("tr.lookup-item-row[data-item-code='G2'] .lookup-item-remove"))
            .ToHaveCountAsync(0);

        await page.UncheckAsync("tr.lookup-item-row[data-item-code='G2'] .lookup-item-active");
        await page.ClickAsync("#lookup-list-save");

        await Assertions.Expect(row.Locator(".lookup-list-counts")).ToContainTextAsync("1");
        await Assertions.Expect(row.Locator(".lookup-list-counts")).ToContainTextAsync("retired");
    }

    /// <summary>
    /// A row added by mistake can be taken off the form again before it is saved.
    /// </summary>
    /// <remarks>
    /// The other half of the round-trip pair. It matters more than it looks: Remove is the only
    /// control on this screen that disappears once a row is saved, so a Remove that did nothing
    /// would leave a reader with a blank row they could neither fill in nor get rid of, and a
    /// blank code is refused on Save.
    /// </remarks>
    [Fact]
    public async Task ARowAddedByMistakeIsRemovedBeforeItIsSaved()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin");

        await OpenPeopleGroupAsync(page);
        await page.ClickAsync("a[href*='/Admin/Forms/Index']");
        await page.ClickAsync("#forms-open-lookup-lists");
        await page.ClickAsync("#lookup-list-create");

        await page.FillAsync("#lookup-list-code", "click-removable");
        await page.FillAsync("#lookup-list-editor input[name='NameEn']", "Removable");

        await FillRowAsync(page, 0, "KEEP", "Keep", string.Empty);

        await page.ClickAsync("#lookup-item-add");
        await Assertions.Expect(page.Locator("tr.lookup-item-row")).ToHaveCountAsync(2);

        await page.ClickAsync("tr.lookup-item-row[data-row-index='1'] .lookup-item-remove");
        await Assertions.Expect(page.Locator("tr.lookup-item-row")).ToHaveCountAsync(1);

        await page.ClickAsync("#lookup-list-save");

        var row = page.Locator("tr.lookup-list-row[data-lookup-list-code='click-removable']");

        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.Locator(".lookup-list-counts")).ToContainTextAsync("1");
    }

    /// <summary>
    /// The reconciliation screen is reached from the list editor, and nowhere else.
    /// </summary>
    /// <remarks>
    /// The product owner's constraint is one menu entry for the whole form designer, so every
    /// screen it brings has to be a click from another. This one hangs off the editor because that
    /// is where somebody who has just bound a list already is — and a screen with no link to it
    /// passes every test that starts by going there.
    /// </remarks>
    [Fact]
    public async Task TheStoredValuesScreenIsReachedByClickingFromTheListEditor()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin");

        await OpenPeopleGroupAsync(page);
        await page.ClickAsync("a[href*='/Admin/Forms/Index']");
        await page.ClickAsync("#forms-open-lookup-lists");

        // A list the base recipe ships, so there is something real to open.
        await page.ClickAsync("tr.lookup-list-row[data-lookup-list-code='grade'] .lookup-list-edit");

        await page.ClickAsync("#lookup-list-check-values");

        await Assertions.Expect(page.Locator("#lookup-values-locations")).ToBeVisibleAsync();

        // Where it looked, named. On this tenant the grade list is offered by one field, and a
        // reader who sees nothing needs to be able to tell "no unmatched values" from "nothing is
        // bound".
        await Assertions.Expect(page.Locator(".lookup-values-location")).ToHaveCountAsync(1);

        // And back again, by clicking.
        await page.ClickAsync("#lookup-values-back");
        await Assertions.Expect(page.Locator("#lookup-list-editor")).ToBeVisibleAsync();
    }

    /// <summary>
    /// A list nothing offers says so, rather than reporting that the data is clean.
    /// </summary>
    /// <remarks>
    /// <c>job-title</c> ships as a list and is deliberately bound to no field — a bilingual name
    /// field cannot post a single chosen code without script, so binding it is a separate decision.
    /// A reader checking it must not be told "every stored value matches", which would be true and
    /// useless.
    /// </remarks>
    [Fact]
    public async Task AListNothingOffersSaysSoRatherThanReportingCleanData()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin");

        await OpenPeopleGroupAsync(page);
        await page.ClickAsync("a[href*='/Admin/Forms/Index']");
        await page.ClickAsync("#forms-open-lookup-lists");

        await page.ClickAsync("tr.lookup-list-row[data-lookup-list-code='job-title'] .lookup-list-edit");
        await page.ClickAsync("#lookup-list-check-values");

        await Assertions.Expect(page.Locator(".lookup-values-location")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#lookup-values-locations .alert")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The People menu offers exactly two entries, in that order, whatever language the reader is in.
    /// </summary>
    /// <remarks>
    /// By href, so the same assertion holds in both languages — which is the point. The order used
    /// to come from <c>PrefixPosition()</c>, which sorts by the caption, so it was alphabetical in
    /// whichever language the reader was in and the two did not agree: "Employees" before "Forms"
    /// in English, "النماذج" before "الموظفون" in Arabic. The dimension engine's menu found this
    /// first; adding a second People entry is what made it reachable here.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task ThePeopleMenuOffersExactlyTwoEntriesInOrder(string culture)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, culture);

        var hrefs = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('a[href]')]
                .map(link => new URL(link.href, location.origin).pathname)
                .filter(path => path.startsWith('/Admin/Employees/') || path.startsWith('/Admin/Forms'))
            """);

        hrefs.Should().Equal(["/Admin/Employees/Index", "/Admin/Forms/Index"]);
    }

    /// <summary>
    /// An Arabic reader gets the Arabic captions, not the English strings falling through.
    /// </summary>
    /// <remarks>
    /// Separate from the order, because the two fail for different reasons: a missing resource shows
    /// as English text in the right place, and a positioning mistake shows as Arabic text in the
    /// wrong order. One assertion covering both would report either as the other.
    /// </remarks>
    [Fact]
    public async Task AnArabicReaderGetsTheArabicCaptions()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "ar");

        var captions = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('a[href]')]
                .filter(link => new URL(link.href, location.origin).pathname.startsWith('/Admin/Forms'))
                .map(link => link.textContent.trim())
            """);

        captions.Should().ContainSingle();
        captions[0].Should().Be("النماذج", "the caption is a resource, and an untranslated one would read as 'Forms'");
    }

    // ---- clicking, rather than navigating ---------------------------------------------

    private static async Task FillRowAsync(IPage page, int index, string code, string nameEn, string nameAr)
    {
        await page.FillAsync($"input[name='Items[{index}].Code']", code);
        await page.FillAsync($"input[name='Items[{index}].NameEn']", nameEn);
        await page.FillAsync($"input[name='Items[{index}].NameAr']", nameAr);
    }

    /// <summary>
    /// Expands the People group if the theme has it collapsed, so the links inside can be clicked.
    /// </summary>
    private static async Task OpenPeopleGroupAsync(IPage page)
    {
        var link = page.Locator("a[href*='/Admin/Forms/Index']").First;

        if (await link.IsVisibleAsync())
        {
            return;
        }

        var group = page.GetByRole(AriaRole.Button, new() { NameString = "People" }).First;

        if (await group.CountAsync() > 0 && await group.IsVisibleAsync())
        {
            await group.ClickAsync();
        }
    }

    /// <summary>
    /// Opens the admin in one culture, with the People group expanded.
    /// </summary>
    /// <remarks>
    /// The culture cookie is set the way the admin's own picker sets it, then the page is loaded —
    /// the order matters, because the middleware reads it on the request rather than afterwards.
    /// </remarks>
    private static async Task OpenAdminInAsync(IPage page, string culture)
    {
        await page.GotoAsync("/Admin");

        await page.EvaluateAsync(
            $"document.cookie = '.AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture};path=/'");

        await page.ReloadAsync();

        await OpenPeopleGroupAsync(page);
    }
}
