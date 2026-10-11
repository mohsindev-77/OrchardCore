using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Following a designer card into the list of the people at its unit.
/// </summary>
/// <remarks>
/// <b>On a fetched card, deliberately.</b> Civil is at depth two, so its card is one the browser
/// built from the template rather than one the server rendered — and the two have now disagreed
/// twice, once over a menu entry the script dropped and once over a line of text the script never
/// filled. Both times the server-rendered roots were correct and everything below them was not, and
/// both times a test that looked only at the roots passed. So this one looks below them.
///
/// Both ways in are exercised, because they are two controls and either can break alone: the count
/// itself, which is what somebody reads and then wants the names behind, and the ⋯ menu's "View
/// employees", which is where somebody who thinks in menus will look.
/// </remarks>
[Collection(UsesTheZenithPeopleTenant.Name)]
public sealed class UnitEmployeeListBrowserTests
{
    private readonly ZenithPeopleTenantFixture _tenant;

    public UnitEmployeeListBrowserTests(ZenithPeopleTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Clicking the employee count on a depth-two card opens that unit's list.
    /// </summary>
    [Fact]
    public async Task ClickingTheCountOnAFetchedCardOpensThatUnitsEmployees()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        // Opened by pressing the toggle, so Civil's card is built in the browser.
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        var civil = DesignerTree.Node(page, "Civil");

        await Assertions.Expect(civil).ToBeVisibleAsync();

        var count = civil.Locator(".designer-card-employees-link");

        // The count says fourteen before it is pressed, which is the number the next screen has to
        // agree with.
        await Assertions.Expect(count).ToContainTextAsync("14");

        await count.ClickAsync();

        await Assertions.Expect(page.Locator("#employee-unit-heading"))
            .ToContainTextAsync("Employees in Civil");

        await Assertions.Expect(page.Locator("#employee-unit-total")).ToContainTextAsync("14");

        // Fourteen *members*, so the header and the list agree as well as the header and the card.
        // Counted by excluding the head-who-is-not-a-member row, because that is exactly the row
        // the header's figure excludes — and because another test on this tenant appoints somebody
        // from Finance to head Civil, which is a legitimate thing for it to do and would otherwise
        // make this test's arithmetic depend on the order the two ran in.
        await Assertions.Expect(
            page.Locator("#employee-list .employee-row:not([data-unit-role='HeadOnly'])"))
            .ToHaveCountAsync(14);
    }

    /// <summary>
    /// The same unit through the card's ⋯ menu, and sub-units from the list itself.
    /// </summary>
    [Fact]
    public async Task TheMenuOpensTheSameListAndSubUnitsWidenIt()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        // The Division itself this time, which is a server-rendered root: nobody is placed on a
        // Division, so this is also the case where the count is absent and the menu is the only way
        // in. A card with no count must still offer it.
        var division = DesignerTree.Node(page, "Engineering Division");

        await division.Locator(".designer-actions summary").First.ClickAsync();
        await division.Locator("[data-designer-action='viewemployees']").First.ClickAsync();

        await Assertions.Expect(page.Locator("#employee-unit-heading"))
            .ToContainTextAsync("Employees in Engineering Division");

        // Empty, because employees attach to leaves and a Division is a container.
        await Assertions.Expect(page.Locator("#employee-unit-total")).ToContainTextAsync("0");

        // Now widen it, by the checkbox on the screen rather than by editing the URL.
        await page.CheckAsync("#employee-include-sub-units");
        await page.ClickAsync("#employee-filter button[type='submit']");

        await Assertions.Expect(page.Locator("#employee-unit-total")).ToContainTextAsync("31");

        await Assertions.Expect(
            page.Locator("#employee-list .employee-row:not([data-unit-role='HeadOnly'])"))
            .ToHaveCountAsync(31);
    }

    /// <summary>
    /// Clicking the count does not also expand the card.
    /// </summary>
    /// <remarks>
    /// The card is a toggle and the count now sits inside it, so the two gestures overlap. Asserted
    /// from the other direction too — pressing the card still toggles — because "the link stopped
    /// the click" and "the card stopped toggling" look identical from one assertion.
    /// </remarks>
    [Fact]
    public async Task TheCountIsALinkAndTheCardIsStillAToggle()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        var division = DesignerTree.Node(page, "Engineering Division");

        // Pressing the card opens it: the gesture the link must not steal.
        await division.Locator(".designer-card").First.ClickAsync();
        await Assertions.Expect(DesignerTree.Node(page, "Civil")).ToBeVisibleAsync();

        // Pressing the card again closes it, so the card is a toggle rather than a one-way opener.
        await division.Locator(".designer-card").First.ClickAsync();
        await Assertions.Expect(DesignerTree.Node(page, "Civil")).ToBeHiddenAsync();
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
