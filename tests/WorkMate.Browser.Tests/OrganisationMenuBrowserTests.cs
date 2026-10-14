using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The Organisation menu, in a real browser, in both languages, on a tenant with both demo
/// companies loaded — FRS v2.5 §1.2, decisions D3 and D23, ADR-0024.
/// </summary>
/// <remarks>
/// <b>This file has asserted three different things.</b> A menu with one entry per dimension
/// <em>type</em> shipped in prompt 4 session D and the product owner withdrew it on sight: on this
/// very tenant it read <em>Branchs, Cost centres, Departments, Departments, Departments, Divisions,
/// Divisions, Divisions, Projects, Regions, Sections, Teams</em>. The tests were then written to
/// assert three fixed entries, and then rewritten again when FRS v2.5 asked for the per-dimension
/// menu back.
///
/// Two things had gone wrong and neither was "one entry per dimension is a bad idea". The plural was
/// <b>guessed</b>, and the tenant really did hold three Department <b>types</b>, one per demo
/// organisation. Decision D17 proposed making the demos share one type; that was built and reverted,
/// because a dimension type is a level of every chart that declares it and a shared Department put
/// each company's units on the others' charts. <b>Decision D23 groups them in the menu instead</b>,
/// which is where the duplication was visible and where it costs the graph nothing.
///
/// <b>Why the Arabic half is here and not only in the integration suite.</b> Orchard resolves a
/// request's culture through its localisation middleware, from the cookie its own culture picker
/// writes. A browser sets that cookie the way a person does; a hand-built <c>Cookie</c> header on a
/// client that already has a cookie container does not reliably survive, and a test that silently
/// kept reading English would be worse than no test.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class OrganisationMenuBrowserTests
{
    private readonly DemoCompanyTenantFixture _tenant;

    public OrganisationMenuBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>Every link into a dimension's own list, as the rendered page carries them.</summary>
    private const string DimensionLinks = "a[href*='/Dimensions/Records/Index']";

    /// <summary>Every link into one value's own page.</summary>
    private const string ValueLinks = "a[href*='/Dimensions/Records/Details']";

    /// <summary>
    /// One entry per dimension <em>name</em>, each opening its own list — in both languages.
    /// </summary>
    /// <remarks>
    /// <b>By href, and by a <em>distinct</em> count.</b> The hrefs carry the group key, so counting
    /// distinct ones is what "each dimension appears exactly once" means without reading a single
    /// caption — which is the only way to ask the same question of an Arabic page and an English one.
    ///
    /// Seven, on a tenant with nine dimension types: Zenith and Crescent each declare a Division and
    /// a Department, and each pair is one entry.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task EveryDimensionNameGetsOneEntryOfItsOwn(string culture)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, culture);

        var hrefs = await DimensionHrefsAsync(page);

        hrefs.Should().HaveCount(
            7,
            "nine types, but Zenith's and Crescent's Divisions are one entry and their Departments another");

        hrefs.Should().OnlyHaveUniqueItems("one entry per dimension name, and one name per entry");
    }

    /// <summary>
    /// The entries read as authored plurals, and nothing is pluralised by rule.
    /// </summary>
    /// <remarks>
    /// <b><em>Branchs</em> is the assertion.</b> It is the exact word the withdrawn implementation
    /// produced, from the exact dimension that produced it, on the exact tenant it produced it on.
    ///
    /// <b><em>Zenith Departments</em> is the other one.</b> An earlier attempt at this slice
    /// disambiguated the two Department types by making each recipe author a company-qualified
    /// plural, so the menu read <em>Zenith Departments</em> and <em>Crescent Departments</em>. That
    /// is a tenant reading its own company name on every entry to work around a menu defect, which
    /// decision D23 B replaced with grouping. Asserting it is gone is what stops it coming back.
    /// </remarks>
    [Fact]
    public async Task TheEntriesReadAsAuthoredPluralsInEnglish()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        var captions = await DimensionCaptionsAsync(page);

        captions.Should().Contain(caption => caption.Contains("Branches", StringComparison.Ordinal));

        captions.Should().NotContain(
            caption => caption.Contains("Branchs", StringComparison.Ordinal),
            "no rule pluralises an English noun correctly");

        captions.Should().ContainSingle(
            caption => caption.Contains("Departments", StringComparison.Ordinal),
            "two Department types, one entry (D23 B)");

        captions.Should().NotContain(
            caption => caption.Contains("Zenith", StringComparison.Ordinal),
            "the company-qualified captions are gone");
    }

    /// <summary>
    /// An Arabic reader gets Arabic plurals, not the singular reused and not the English.
    /// </summary>
    /// <remarks>
    /// The half the old implementation could not do at all: Arabic pluralises by changing the word
    /// — فرع becomes الفروع — so there was never a suffix to append and the menu simply repeated the
    /// singular.
    /// </remarks>
    [Fact]
    public async Task AnArabicReaderGetsArabicPlurals()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "ar");

        var captions = await DimensionCaptionsAsync(page);
        var all = string.Join(" | ", captions);

        all.Should().Contain("الفروع", "the authored Arabic plural of فرع");
        all.Should().Contain("الإدارات", "the authored Arabic plural of إدارة, once for both types");

        captions.Should().NotContain(
            caption => caption.Contains("Department", StringComparison.Ordinal),
            "an untranslated caption would read as the English");
    }

    /// <summary>
    /// A dimension expands to its own values, and clicking one opens that value's page.
    /// </summary>
    /// <remarks>
    /// <b>Decision D23 C, by clicking, which is the only way it counts.</b> The menu is built from a
    /// cached per-tenant model and rendered by Orchard's navigation shapes; a value entry that
    /// resolved to no URL would render as an anchor with no href and look exactly like one that
    /// worked. So this expands the group in a real browser, clicks a real department and asserts the
    /// page that opens is that department's.
    /// </remarks>
    [Fact]
    public async Task AValueInTheMenuOpensItsOwnPage()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "Civil (Zenith Engineering & Construction)", "Organisation", "Departments");

        await Assertions.Expect(page.Locator("#value-name")).ToContainTextAsync("Civil");
        await Assertions.Expect(page.Locator("#value-code")).ToContainTextAsync("zenith-dept-civil");

        // The facts the brief asks for, on the page rather than one click further on.
        await Assertions.Expect(page.Locator("#value-parent")).ToContainTextAsync("Engineering Division");
        await Assertions.Expect(page.Locator("#value-status")).ToContainTextAsync("Active");
        await Assertions.Expect(page.Locator("#value-head")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#value-employees")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#value-edit")).ToBeVisibleAsync();
    }

    /// <summary>
    /// <em>All Departments</em> shows both companies' departments, with an Organisation column.
    /// </summary>
    /// <remarks>
    /// The other half of D23 C. The grouping is a menu idea, so the screen behind it has to be able
    /// to show several dimension types at once — and when it does, a reader needs to know which
    /// company each row belongs to, because Crescent and Zenith both have a department called HR.
    /// </remarks>
    [Fact]
    public async Task AllOfADimensionShowsEveryCompanysValuesAndSaysWhich()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "All Departments", "Organisation", "Departments");

        await Assertions.Expect(page.Locator("#record-list-heading")).ToContainTextAsync("Departments");

        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='zenith-dept-civil']")).ToBeVisibleAsync();

        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='crescent-dept-treasury']")).ToBeVisibleAsync();

        // The column, and the filter beside it. Both appear only because this dimension's name is
        // shared by two types; a tenant with one Department sees neither.
        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='zenith-dept-civil'] .record-organisation"))
            .ToContainTextAsync("Zenith");

        await Assertions.Expect(page.Locator("#record-organisation")).ToBeAttachedAsync();
    }

    /// <summary>
    /// The order is the same in both languages.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> The order used to come from
    /// <c>LocalizedString.PrefixPosition()</c>, which sorts by the caption — so it was alphabetical
    /// in whichever language the reader was in, and the two menus disagreed. Compared by href,
    /// because the href is the one thing on the row that is the same for both readers.
    ///
    /// <b>The values under an entry are the deliberate exception</b> and are not compared here: D23
    /// asks for them alphabetically in the reader's own language, so the two languages list them
    /// differently on purpose.
    /// </remarks>
    [Fact]
    public async Task TheOrderIsIdenticalInBothLanguages()
    {
        await using var context = await _tenant.SignedInContextAsync();

        var englishPage = await context.NewPageAsync();
        await OpenAdminInAsync(englishPage, "en");
        var english = await DimensionHrefsAsync(englishPage);

        var arabicPage = await context.NewPageAsync();
        await OpenAdminInAsync(arabicPage, "ar");
        var arabic = await DimensionHrefsAsync(arabicPage);

        arabic.Should().Equal(english, "the order is a number a customer sets, not an alphabetisation");
    }

    /// <summary>
    /// The organisation designer is first, and the configuration screens are last.
    /// </summary>
    /// <remarks>
    /// <b>Decision D23 A, and this test exists because slice F2 got it wrong.</b> F2 moved the
    /// designer under Settings — a click path that had already shipped, changed for no reason the
    /// brief asked for. It is back where it was, above every dimension, and the shape around it is
    /// D3's: the thing you look at, the dimensions, then the things you set up.
    /// </remarks>
    [Fact]
    public async Task TheDesignerIsFirstAndSettingsIsLast()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        var paths = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll(\"a[href*='/Dimensions/']\")]"
            + ".map(link => new URL(link.href, location.origin).pathname)");

        paths.Should().Contain("/Admin/Dimensions/Designer/Index");
        paths.Should().Contain("/Admin/Dimensions/Types/Index");
        paths.Should().Contain("/Admin/Dimensions/Structures/Index");

        var designer = Array.IndexOf(paths, "/Admin/Dimensions/Designer/Index");

        var firstDimension = Array.FindIndex(
            paths, path => path.Contains("/Records/Index", StringComparison.Ordinal));

        var lastDimension = Array.FindLastIndex(
            paths, path => path.Contains("/Records/", StringComparison.Ordinal));

        var firstSetting = Array.FindIndex(
            paths, path => path.Contains("/Types/Index", StringComparison.Ordinal));

        designer.Should().BeGreaterThanOrEqualTo(0, "the designer is in the menu");
        designer.Should().BeLessThan(firstDimension, "and it is above every dimension");
        lastDimension.Should().BeLessThan(firstSetting, "Settings is last");
    }

    /// <summary>
    /// The designer opens on the screen it always opened on, with its card actions intact.
    /// </summary>
    /// <remarks>
    /// The rest of D23 A — <em>same click path, same screen, every card action unchanged</em>. The
    /// integration suite asserts the action list by name; this asserts that clicking the entry a
    /// person clicks still lands on the chart, which is the half a list of strings cannot cover.
    /// </remarks>
    [Fact]
    public async Task TheDesignerEntryStillOpensTheDesigner()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await AdminMenu.ClickAsync(page, "Organisation designer", "Organisation");

        await Assertions.Expect(page.Locator(".designer-surface").First).ToBeVisibleAsync();
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>
    /// The per-dimension captions, as rendered, in menu order.
    /// </summary>
    /// <remarks>
    /// Read off the links into a dimension's own list, so nothing else in the group — the Settings
    /// entries, the designer, the values, the group's own caption — can be mistaken for one. Since
    /// decision D23 those links read <em>All Departments</em> rather than <em>Departments</em>,
    /// which is still the authored plural and still the thing worth asserting about.
    ///
    /// The caption is matched as a substring rather than compared exactly, because the admin theme
    /// renders a menu item's words twice: once visibly and once as the collapsed-sidebar title.
    /// </remarks>
    private static Task<string[]> DimensionCaptionsAsync(IPage page) =>
        page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll(\"" + DimensionLinks + "\")]"
            + ".map(link => link.textContent.trim())");

    /// <summary>The query string of each per-dimension link, in menu order.</summary>
    private static Task<string[]> DimensionHrefsAsync(IPage page) =>
        page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll(\"" + DimensionLinks + "\")]"
            + ".map(link => new URL(link.href, location.origin).search)");

    /// <summary>
    /// Opens the admin in one culture.
    /// </summary>
    /// <remarks>
    /// The culture cookie is set the way the admin's own picker sets it, then the page is loaded —
    /// the order matters, because the middleware reads it on the request rather than afterwards.
    ///
    /// <b>The group is not expanded, and that is a fix rather than an omission.</b> It used to try,
    /// with Playwright's non-retrying <c>CountAsync</c> and <c>IsVisibleAsync</c> against a sidebar
    /// that may not have rendered yet — see <see cref="AdminMenu"/> for the race that was.
    /// Expanding was never needed for the tests that read the DOM: a collapsed group's links are
    /// already in it. Worse, expanding means naming the group, and the group's caption is in the
    /// reader's language — so the helper would have been asserting in English about the very thing
    /// the Arabic pass exists to measure. The tests that do click through a group name it
    /// themselves, in the language they opened.
    ///
    /// What replaces it is the wait that was actually missing: the menu has to be <em>attached</em>
    /// before anything reads it. Attached rather than visible, because visible is a claim about the
    /// group being open, which is not this test's question.
    /// </remarks>
    private static async Task OpenAdminInAsync(IPage page, string culture)
    {
        await page.GotoAsync("/Admin");

        await page.EvaluateAsync(
            $"document.cookie = '.AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture};path=/'");

        await page.ReloadAsync();

        await Assertions.Expect(page.Locator(DimensionLinks).First).ToBeAttachedAsync();
        await Assertions.Expect(page.Locator(ValueLinks).First).ToBeAttachedAsync();
    }
}
