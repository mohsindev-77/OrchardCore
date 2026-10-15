using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Positions and grades, by clicking: the menu entries, the establishment columns, the reporting
/// chart, and the Add form's two extra controls. Decisions D26 C–G, ADR-0028.
/// </summary>
/// <remarks>
/// <b>Why these need a browser at all.</b> Three of the four things this slice adds to a screen are
/// only true once a page has been rendered and a reader has clicked something: a menu entry that
/// resolves to no route renders as an anchor with no href and looks exactly like one that works; a
/// reporting chart is the designer's own CSS applied to markup this module writes, so nothing but a
/// layout engine can say whether the boxes joined up; and the grade control's "disabled until a
/// position is chosen" is a script reacting to a change event.
///
/// <b>Both languages where the assertion is a caption.</b> The two new dimensions carry authored
/// Arabic plurals — المناصب and الدرجات — and the failure mode ADR-0024 was written against is a
/// menu that quietly reads English to an Arabic reader.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class PositionsClickBrowserTests
{
    private readonly DemoCompanyTenantFixture _tenant;

    public PositionsClickBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Organisation → Positions → All Positions opens the list, with its establishment columns.
    /// </summary>
    /// <remarks>
    /// Decisions D26 E and D25 together: the four columns appear here and nowhere else, and the
    /// click is what proves the menu entry the base recipe ships actually reaches the screen.
    /// </remarks>
    /// <param name="grades">
    /// What the Allowed grades cell reads in this language. <b>The grade's name, not its code</b>,
    /// so it is translated — the demo's HRM-1 is <em>موارد بشرية-١</em> in Arabic. The column shows
    /// names because the demo codes are prefixed <c>zenith-grade-hrm-1</c>, which is three times as
    /// wide and tells a reader less.
    /// </param>
    [Theory]
    [InlineData("en", "Organisation", "Positions", "All Positions", "HRM-1, HRM-2, HRM-3")]
    [InlineData("ar", "الهيكل التنظيمي", "المناصب", "كل المناصب", "موارد بشرية-١, موارد بشرية-٢, موارد بشرية-٣")]
    public async Task ThePositionsEntryOpensAListWithApprovedFilledAndVacant(
        string culture,
        string organisation,
        string caption,
        string all,
        string grades)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, culture);

        await AdminMenu.ClickAsync(page, all, organisation, caption);

        await Assertions.Expect(page.Locator("#record-list-heading")).ToContainTextAsync(caption);

        // The demo's HR Manager: one post, approved 3, nobody in it yet, three grades.
        var row = page.Locator("tr.record-row[data-record-code='zenith-pos-hr-manager']");

        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.Locator(".record-approved")).ToHaveTextAsync("3");
        await Assertions.Expect(row.Locator(".record-filled")).ToHaveTextAsync("0");
        await Assertions.Expect(row.Locator(".record-vacant")).ToContainTextAsync("3");
        await Assertions.Expect(row.Locator(".record-allowed-grades")).ToContainTextAsync(grades);
    }

    /// <summary>Organisation → Grades opens the grade ladder, in both languages.</summary>
    [Theory]
    [InlineData("en", "Organisation", "Grades", "All Grades")]
    [InlineData("ar", "الهيكل التنظيمي", "الدرجات", "كل الدرجات")]
    public async Task TheGradesEntryOpensTheLadder(
        string culture,
        string organisation,
        string caption,
        string all)
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, culture);

        await AdminMenu.ClickAsync(page, all, organisation, caption);

        await Assertions.Expect(page.Locator("#record-list-heading")).ToContainTextAsync(caption);

        await Assertions.Expect(
            page.Locator("tr.record-row[data-record-code='zenith-grade-se-1']")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Grades ship flat: SE-1 sits at the top of the ladder, not under an SE heading. Decision D26 C.
    /// </summary>
    /// <remarks>
    /// <b>English only, and on purpose.</b> The words it asserts — <em>Top level</em> — are
    /// localised, so running it in both languages would mean pinning the Arabic for a string this
    /// test is not about. What the Arabic theory above proves is that the menu and the screen are
    /// reachable and captioned in Arabic; what this proves is the shape of the shipped data, which
    /// is the same in both.
    ///
    /// <b>Scoped to Zenith's ladder</b>, because this tenant has two Grade dimensions and the
    /// grouped list reports a row's placement on each — SE-1 is at the top of Zenith's and on base's
    /// not at all.
    /// </remarks>
    [Fact]
    public async Task GradesShipFlatRatherThanGrouped()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "All Grades", "Organisation", "Grades");

        var parent = page.Locator("tr.record-row[data-record-code='zenith-grade-se-1'] .record-parent");

        // Top level, and under nothing: the shipped ladder has no grouping above it, only the
        // licence to add one (the grade → grade diagonal on the grades axis).
        await Assertions.Expect(parent).ToContainTextAsync("Top level");
    }

    /// <summary>
    /// A grade's page names the positions that allow it, and each name opens that position.
    /// </summary>
    [Fact]
    public async Task AGradePageNamesThePositionsThatUseItAndTheyAreClickable()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "All Grades", "Organisation", "Grades");

        await page.Locator("tr.record-row[data-record-code='zenith-grade-se-2'] a.record-details")
            .ClickAsync();

        var usedBy = page.Locator("#value-used-by-positions");

        await Assertions.Expect(usedBy).ToContainTextAsync("Software Engineer");

        // And it is a way through, not a label: clicking lands on that position's own page.
        await usedBy.GetByRole(AriaRole.Link, new() { NameString = "Software Engineer" }).ClickAsync();

        await Assertions.Expect(page.Locator("#value-code")).ToContainTextAsync("zenith-pos-se");
    }

    /// <summary>
    /// A position's page opens on its reporting line and draws it as a chart with establishment
    /// badges. Decisions D25 D and D26 E.
    /// </summary>
    /// <remarks>
    /// <b>The chart is the designer's own layout</b> — `li.designer-node > .designer-card +
    /// ul.designer-children` inside a `.designer-surface` — so the connector lines, the row
    /// levelling and the RTL mirroring all come from `organisation-designer.css` with nothing added
    /// to it. Nothing but a browser can say whether that is still true.
    /// </remarks>
    [Fact]
    public async Task APositionPageDrawsItsReportingLineWithFilledOverApproved()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "All Positions", "Organisation", "Positions");

        await page.Locator("tr.record-row[data-record-code='zenith-pos-ceo'] a.record-details")
            .ClickAsync();

        await page.Locator("#value-units-view-hierarchy").ClickAsync();

        // The reporting line, not the organisation chart: the CEO's chart is the Director under it,
        // and the Director's unit is a department the CEO does not sit in.
        // The CARD rather than the node: `:has()` matches an ancestor too, and the Director's box is
        // inside the CEO's, so a node selector resolves to both and Playwright refuses in strict
        // mode. A card contains exactly one name.
        var director = page.Locator(".designer-card:has(.value-hierarchy-name:text-is('Director Technology'))");

        await Assertions.Expect(director).ToBeVisibleAsync();

        var badge = director.Locator(".value-hierarchy-establishment").First;

        await Assertions.Expect(badge).ToContainTextAsync("0/1");
        await Assertions.Expect(badge).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("text-bg-warning"));

        // An employee count on a reporting chart counts nobody, so the badge replaces it.
        await Assertions.Expect(page.Locator(".value-hierarchy-employees")).ToHaveCountAsync(0);

        // And each box names the unit the post sits in, which is the only thing telling two posts
        // of the same name apart once every box's dimension reads "Position".
        await Assertions.Expect(director.Locator(".value-hierarchy-unit").First)
            .ToContainTextAsync("Electrical");
    }

    /// <summary>
    /// The Add form offers a reporting line and a grade set, and the grade box is a multi-select.
    /// </summary>
    /// <remarks>
    /// Decisions D26 C and ADR-0028: the two controls that are a position's and no other
    /// dimension's. Asserted on a tenant where Position <em>can</em> be placed, which is what makes
    /// this the opposite case from the "can't be placed yet" message the integration suite covers.
    /// </remarks>
    [Fact]
    public async Task ThePositionAddFormOffersAReportingLineAndASetOfGrades()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        // Through Dimension types, which is the route to ONE type's list. This tenant holds two
        // Position types — base's and Zenith's — and D23 B shows them as one menu entry, so the
        // grouped list is about both and "Add" would not know which of them it was adding. The
        // *View units* link on a Dimension types row is the existing way to say which, and the
        // route <c>DimensionTypeUnitsBrowserTests</c> was written to pin.
        await AdminMenu.ClickAsync(page, "Dimension types", "Organisation", "Settings");

        await page.Locator(".dimension-type-row[data-type-code='zenith-position'] .dimension-type-units")
            .ClickAsync();

        await page.Locator("#record-new").ClickAsync();

        // Where it sits in the business, and who it answers to: two controls, two axes.
        await Assertions.Expect(page.Locator("[data-unit-picker]").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-position-picker]")).ToBeVisibleAsync();

        // A set of grades, as a list of checkboxes. It was a <select multiple> and manual testing
        // was right to send it back: a plain click in a listbox drops every other selection, so
        // somebody ticking four grades in a row saved one, and nothing on screen said so.
        var grades = page.Locator("[data-grade-checklist]");

        await Assertions.Expect(grades).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-grade-picker]")).ToHaveCountAsync(0);

        await Assertions.Expect(
            grades.Locator("[data-grade-option]", new() { HasTextString = "SE-1" }).First)
            .ToBeVisibleAsync();

        // And the approved headcount, which arrives as the Position type's own attribute.
        (await page.ContentAsync()).Should().Contain("Approved headcount");
    }

    /// <summary>
    /// Several grades can be ticked one after another, with no modifier key and nothing lost.
    /// </summary>
    /// <remarks>
    /// <b>This is the regression, and it needs a browser because the old failure was a click.</b>
    /// In the listbox, clicking SE-1 and then SE-2 left SE-2 selected and SE-1 not — correct listbox
    /// behaviour, invisible as a data-loss bug, and unreachable by any test that only reads markup.
    /// Here four plain clicks leave four boxes ticked.
    ///
    /// The search box and the count are the script's half: filtering narrows the list without a
    /// round trip, a ticked grade is never hidden by it — hiding a chosen value while leaving it in
    /// the post would make the form save what the screen denies — and the count tracks the ticks.
    /// </remarks>
    [Fact]
    public async Task GradesAreTickedOneAfterAnotherWithNoModifierKeyAndNoneAreLost()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "Dimension types", "Organisation", "Settings");

        await page.Locator(".dimension-type-row[data-type-code='zenith-position'] .dimension-type-units")
            .ClickAsync();

        await page.Locator("#record-new").ClickAsync();

        var list = page.Locator("[data-grade-checklist]");

        await Assertions.Expect(list).ToBeVisibleAsync();

        // By code, not by visible text: `has-text` is a substring match, so "SE-1" also selects the
        // PSE-1 row and two of the four clicks would land on one checkbox.
        foreach (var code in new[]
        {
            "zenith-grade-se-1",
            "zenith-grade-se-2",
            "zenith-grade-se-3",
            "zenith-grade-pse-1",
        })
        {
            await list.Locator($"[data-grade-option][data-code='{code}'] input[type=checkbox]")
                .CheckAsync();
        }

        // Four plain clicks, four grades — no control-click, and nothing silently dropped.
        await Assertions.Expect(list.Locator("input[type=checkbox]:checked")).ToHaveCountAsync(4);

        // The live count, which is how a reader of a scrolling box knows how many are on.
        await Assertions.Expect(page.Locator("[data-grade-checklist-count]"))
            .ToContainTextAsync("4");

        // The search narrows the list in place.
        await page.Locator("[data-grade-checklist-search]").FillAsync("HRM");

        await Assertions.Expect(
            list.Locator("[data-grade-option][data-code='zenith-grade-hrm-1']")).ToBeVisibleAsync();

        // Something that matches neither the filter nor a tick is hidden, which is the filter
        // actually filtering rather than a no-op that happens to leave everything visible.
        await Assertions.Expect(
            list.Locator("[data-grade-option][data-code='zenith-grade-lab-1']")).ToBeHiddenAsync();

        // And what is already ticked stays visible through the filter, because a chosen value
        // hidden from the reader is one the form would save behind their back.
        await Assertions.Expect(
            list.Locator("[data-grade-option][data-code='zenith-grade-se-1']")).ToBeVisibleAsync();

        // Filtering is not unticking.
        await Assertions.Expect(list.Locator("input[type=checkbox]:checked")).ToHaveCountAsync(4);
    }

    /// <summary>
    /// Narrowing <em>Within</em> past the chosen unit clears it and says so.
    /// </summary>
    /// <remarks>
    /// <b>The contradiction the control used to leave standing.</b> Pick Civil, then narrow the
    /// scope to Corporate: the form held a unit the filter beside it said was out of bounds, and
    /// saving it saved the contradiction. Now the choice is dropped and the drop is announced —
    /// silently emptying the box would send the reader to Save believing a unit was still named,
    /// which is the same class of failure as the listbox losing selections.
    ///
    /// A browser, because the whole of it is a change event, a fetch and a cleared control.
    /// </remarks>
    [Fact]
    public async Task NarrowingTheScopePastTheChosenUnitClearsItAndSaysSo()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenAdminInAsync(page, "en");

        await AdminMenu.ClickAsync(page, "Dimension types", "Organisation", "Settings");

        await page.Locator(".dimension-type-row[data-type-code='zenith-position'] .dimension-type-units")
            .ClickAsync();

        await page.Locator("#record-new").ClickAsync();

        var picker = page.Locator(".workmate-unit-picker").First;
        var unit = picker.Locator("select[data-unit-picker]");

        // Through the combobox, never by setting the hidden select: the select is the form control
        // and the combobox drives it, and a test that set it directly would skip the one thing
        // worth asserting — that what the reader sees and what the form posts stay one answer.
        await Pickers.ChooseAsync(page, ".workmate-unit-picker", "Civil");

        await Assertions.Expect(unit).Not.ToHaveValueAsync(string.Empty);

        // Then narrow the scope to a division Civil is not under. The Within control is an ordinary
        // select the script unhides, so it is driven as one.
        await picker.Locator("[data-unit-picker-within]")
            .SelectOptionAsync(new SelectOptionValue { Label = "Corporate (zenith-div-corporate)" });

        await Assertions.Expect(picker.Locator("[data-unit-picker-cleared]")).ToBeVisibleAsync();

        await Assertions.Expect(picker.Locator("[data-unit-picker-cleared]"))
            .ToContainTextAsync("not inside that scope");

        // The contradiction is resolved rather than posted.
        await Assertions.Expect(unit).ToHaveValueAsync(string.Empty);
    }

    /// <summary>
    /// Opens the admin in one language, the way <c>OrganisationMenuBrowserTests</c> does.
    /// </summary>
    /// <remarks>
    /// The cookie is written from the page rather than added to the context, because it has to be
    /// scoped to the origin the server is actually on and only the page knows that. The reload is
    /// what makes the request carry it; the wait that follows is what stops a test asking a question
    /// of a menu that has not rendered.
    /// </remarks>
    private static async Task OpenAdminInAsync(IPage page, string culture)
    {
        await page.GotoAsync("/Admin");

        await page.EvaluateAsync(
            $"document.cookie = '.AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture};path=/'");

        await page.ReloadAsync();

        await Assertions.Expect(
            page.Locator("a[href*='/Dimensions/Records/Index']").First).ToBeAttachedAsync();
    }
}
