using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Placing and re-placing people, entirely by clicking.
/// </summary>
/// <remarks>
/// <b>No typed URLs past the way in.</b> Every screen under test is reached the way a person
/// reaches it — a card's ⋯ menu, a row's button, the form's own controls — because the defect this
/// session was opened for was not that placement could not be saved but that most of it could not
/// be <em>reached</em>. A test that navigated straight to <c>/Placement/Change</c> would have passed
/// against a build in which nothing linked to it.
///
/// <b>And on fetched cards where it matters.</b> Civil is at depth two, so its card is one the
/// browser built from a cloned template rather than one the server rendered. Those two have now
/// disagreed twice — once over a menu entry the script dropped, once over a line of text it never
/// filled — and both times the server-rendered roots were correct and everything below them was
/// not.
/// </remarks>
[Collection(UsesTheZenithPeopleTenant.Name)]
public sealed class PlacementClickBrowserTests
{
    private readonly ZenithPeopleTenantFixture _tenant;

    public PlacementClickBrowserTests(ZenithPeopleTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// A unit's card offers "Place an employee here…", and the screen it opens knows the unit.
    /// </summary>
    [Fact]
    public async Task PlacingSomebodyFromAFetchedCardsMenuPrefillsTheUnit()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        // Opened by pressing the toggle, so Finance's card is one the browser built rather than one
        // the server rendered.
        //
        // Under Corporate on purpose, not under Engineering Division: another test asserts that
        // division's exact headcount with sub-units included, and placing somebody beneath it here
        // would make its arithmetic depend on which of the two ran first. A test that changes
        // shared data owes the others a unit of its own.
        await DesignerTree.Node(page, "Corporate").Locator(".designer-toggle").First.ClickAsync();

        var finance = DesignerTree.Node(page, "Finance");

        await Assertions.Expect(finance).ToBeVisibleAsync();

        await finance.Locator(".designer-actions summary").First.ClickAsync();
        await finance.Locator("[data-designer-action='placeemployee']").First.ClickAsync();

        // The unit came with the card, so the screen asks who rather than where.
        await Assertions.Expect(page.Locator("#place-heading")).ToContainTextAsync("Finance");
        await Assertions.Expect(page.Locator("#place-unit-prefilled")).ToBeVisibleAsync();

        await Assertions.Expect(page.Locator(".workmate-unit-picker .workmate-picker-input"))
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("Finance"));

        // Somebody who is not in Finance, chosen through the picker's search rather than from a
        // list of the first twenty.
        await Pickers.ChooseAsync(page, ".workmate-employee-picker", "zenith-hr-04");

        await page.FillAsync("#place-effective-from", "2026-06-01");
        await page.ClickAsync("#place-submit");

        // The overview, which is where a save lands, now says Finance.
        await Assertions.Expect(page.Locator(".placement-axis[data-structure-code='zenith-org']"))
            .ToContainTextAsync("Finance");
    }

    /// <summary>
    /// Splitting somebody 60/40 across two units, and then moving which one is the main placement.
    /// </summary>
    /// <remarks>
    /// Two saves rather than one, because they are two different claims. The first is that a split
    /// can be built on this screen at all — add a row, choose a unit through a searching combobox,
    /// set the shares. The second is that the main row can be moved afterwards without rebuilding
    /// the split, which is the ordinary correction and the one a form with a checkbox per row gets
    /// wrong.
    /// </remarks>
    [Fact]
    public async Task SplittingSomebodySixtyFortyAndThenMovingTheMainPlacement()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenEmployeeAsync(page, "zenith-civil-05");

        await page.ClickAsync(".employee-row .employee-change-placement");

        await Assertions.Expect(page.Locator("#change-placement-heading")).ToContainTextAsync("Usman Khan");
        await Assertions.Expect(page.Locator(".change-row")).ToHaveCountAsync(1);

        // A second row, added by the form's own button rather than by script.
        await page.ClickAsync("#change-add");
        await Assertions.Expect(page.Locator(".change-row")).ToHaveCountAsync(2);

        await Pickers.ChooseAsync(page, ".change-row[data-row-index='1'] .workmate-unit-picker", "Site Team B");

        await page.FillAsync("input[name='Rows[0].AllocationPercent']", "60");
        await page.FillAsync("input[name='Rows[1].AllocationPercent']", "40");
        await page.FillAsync("#change-effective-from", "2026-06-01");

        await page.ClickAsync("#change-save");

        var organisation = page.Locator(".placement-axis[data-structure-code='zenith-org']");

        await Assertions.Expect(organisation).ToContainTextAsync("60%");
        await Assertions.Expect(organisation).ToContainTextAsync("40%");
        await Assertions.Expect(organisation.Locator(".placement-row")).ToHaveCountAsync(2);

        // Civil is the main placement; the site team is not.
        await Assertions.Expect(MainCellOf(page, "zenith-dept-civil")).ToHaveTextAsync("Yes");
        await Assertions.Expect(MainCellOf(page, "zenith-team-site-b")).ToHaveTextAsync("No");

        // Now move it, from the overview's own button.
        await organisation.GetByRole(AriaRole.Link, new() { NameString = "Change placement" }).ClickAsync();

        // By which row's picker holds the unit, not by the row's text: every row's hidden <select>
        // carries the whole first page of units as options, so "the row containing Site Team B" is
        // every row on the form.
        var siteRow = await RowHoldingAsync(page, "Site Team B");

        await page.CheckAsync($".change-row[data-row-index='{siteRow}'] .change-main");
        await page.FillAsync("#change-effective-from", "2026-07-01");
        await page.ClickAsync("#change-save");

        await Assertions.Expect(MainCellOf(page, "zenith-team-site-b")).ToHaveTextAsync("Yes");
        await Assertions.Expect(MainCellOf(page, "zenith-dept-civil")).ToHaveTextAsync("No");

        // And the 60/40 survived the move: changing which row is main is not a new split.
        await Assertions.Expect(organisation).ToContainTextAsync("60%");
        await Assertions.Expect(organisation).ToContainTextAsync("40%");
    }

    /// <summary>
    /// A split that does not total 100 is refused on the form, with the form still filled in.
    /// </summary>
    /// <remarks>
    /// The second half is the part worth clicking for: a refusal that redrew an empty form would
    /// make the reader rebuild a split they had already typed, which is how somebody decides to
    /// stop using the screen.
    /// </remarks>
    [Fact]
    public async Task ASplitThatDoesNotTotalAHundredIsRefusedWithTheFormStillFilledIn()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenEmployeeAsync(page, "zenith-civil-06");

        await page.ClickAsync(".employee-row .employee-change-placement");
        await page.ClickAsync("#change-add");

        await Pickers.ChooseAsync(page, ".change-row[data-row-index='1'] .workmate-unit-picker", "Site Team A");

        await page.FillAsync("input[name='Rows[0].AllocationPercent']", "60");
        await page.FillAsync("input[name='Rows[1].AllocationPercent']", "30");
        await page.ClickAsync("#change-save");

        await Assertions.Expect(page.Locator("#change-placement")).ToContainTextAsync("must total 100");
        await Assertions.Expect(page.Locator("#change-placement")).ToContainTextAsync("90");

        // Still two rows, still the figures that were typed.
        await Assertions.Expect(page.Locator(".change-row")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("input[name='Rows[0].AllocationPercent']")).ToHaveValueAsync("60");
        await Assertions.Expect(page.Locator("input[name='Rows[1].AllocationPercent']")).ToHaveValueAsync("30");
    }

    /// <summary>
    /// The list shows the main placement with the rest behind "+N more", and never repeats itself.
    /// </summary>
    /// <remarks>
    /// Ahmed Khan is the row the defect was reported on. It read "Civil · Secondary · 40% · home:
    /// Civil" — the home department named twice, once by the placement column and once by a caption
    /// under the role that could only ever have repeated it. The disclosure is what replaced it:
    /// the main placement in the cell, the others one click away with their full paths and shares.
    /// </remarks>
    [Fact]
    public async Task TheListShowsTheMainPlacementWithTheOthersBehindADisclosure()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        // Site Team A is at depth three — Projects Division → Motorway Interchange → the team — so
        // its card is one the browser built from a card that was itself built in the browser.
        await DesignerTree.ExpandEverythingAsync(page);

        var siteA = DesignerTree.Node(page, "Site Team A");

        await Assertions.Expect(siteA).ToBeVisibleAsync();
        // The card, not the node — see DesignerTree.Card. Site Team A is a leaf today and this was
        // green for as long as it was; the distinction is what stops that being luck.
        await DesignerTree.Card(page, "Site Team A").Locator(".designer-card-employees-link").ClickAsync();

        await Assertions.Expect(page.Locator("#employee-unit-heading"))
            .ToContainTextAsync("Employees in Site Team A");

        var ahmed = page.Locator(".employee-row[data-employee-code='zenith-civil-01']");

        // What he is here, in the renamed column with the renamed values.
        await Assertions.Expect(ahmed.Locator(".employee-role-here")).ToHaveTextAsync("Additional");
        await Assertions.Expect(ahmed.Locator(".employee-allocation")).ToHaveTextAsync("40%");

        // Where he mainly sits, once.
        await Assertions.Expect(ahmed.Locator(".employee-unit-main")).ToHaveTextAsync("Civil");
        await Assertions.Expect(ahmed).Not.ToContainTextAsync("home:");

        // And the rest, behind the disclosure, with the path and the share.
        var more = ahmed.Locator(".employee-unit-more");

        await Assertions.Expect(more.Locator(".employee-unit-more-toggle")).ToHaveTextAsync("+1 more");

        await more.Locator(".employee-unit-more-toggle").ClickAsync();

        await Assertions.Expect(more.Locator(".employee-unit-other")).ToContainTextAsync("Site Team A");
        await Assertions.Expect(more.Locator(".employee-unit-other")).ToContainTextAsync("40%");

        // Somebody wholly here has nothing to disclose, so there is no control at all.
        await Assertions.Expect(
            page.Locator(".employee-row[data-employee-code='zenith-labour-a-01'] .employee-unit-more"))
            .ToHaveCountAsync(0);

        // The star's tooltip is asserted in UnitEmployeeListTenantTests and not here. Who heads
        // Site Team A is not stable on this tenant — EmployeePickerSelectionBrowserTests appoints
        // a head on it, and on Civil, Mechanical and Electrical — so an assertion about the star
        // would pass or fail by test order rather than by anything about the screen.
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>The "Main" cell of one unit's row on the placement overview.</summary>
    private static ILocator MainCellOf(IPage page, string unitCode) =>
        page.Locator($".placement-row[data-unit-code='{unitCode}'] td").Nth(2);

    /// <summary>Which editable row's picker currently holds a named unit.</summary>
    private static Task<int> RowHoldingAsync(IPage page, string unitName) =>
        page.EvaluateAsync<int>(
            """
            name => {
                const rows = [...document.querySelectorAll('.change-row')];

                return rows.findIndex(row => {
                    const select = row.querySelector('select[data-unit-picker]');
                    const chosen = select && select.selectedOptions[0];

                    return !!chosen && chosen.textContent.includes(name);
                });
            }
            """,
            unitName);

    /// <summary>The employee list, narrowed to one person by its own search box.</summary>
    private static async Task OpenEmployeeAsync(IPage page, string code)
    {
        await page.GotoAsync("/Admin/Employees/Index");

        await page.FillAsync("#employee-search", code);
        await page.ClickAsync("#employee-filter button[type='submit']");

        await Assertions.Expect(page.Locator($".employee-row[data-employee-code='{code}']"))
            .ToBeVisibleAsync();
    }

    private static async Task OpenZenithAsync(IPage page)
    {
        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        await page.SelectOptionAsync("#structureId", new SelectOptionValue { Value = await ZenithIdAsync(page) });
        await Assertions.Expect(page.Locator("#designer-tree")).ToBeVisibleAsync();
    }

    private static async Task<string> ZenithIdAsync(IPage page) =>
        await page.EvaluateAsync<string>(
            """
            code => {
                const option = [...document.querySelectorAll('#structureId option')]
                    .find(option => option.textContent.includes(code));

                return option ? option.value : '';
            }
            """,
            "zenith-org");
}
