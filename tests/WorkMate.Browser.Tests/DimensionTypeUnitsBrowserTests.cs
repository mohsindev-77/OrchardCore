using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Reaching a dimension type's units the way the Dimension types list offers them.
/// </summary>
/// <remarks>
/// <b>No typed URLs past the way in.</b> CLAUDE.md requires every screen to be reachable by
/// clicking, and this screen set briefly had a menu entry per dimension type to do it. That was
/// withdrawn — on a real tenant the menu read <em>Branchs, Cost centres, Departments, Departments,
/// Departments, Divisions…</em> — so the route in is the <b>View units</b> link on each row of the
/// Dimension types list, and this is the test that it is a route at all rather than a screen only a
/// URL can open.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class DimensionTypeUnitsBrowserTests
{
    private readonly BrowserTenantFixture _tenant;

    public DimensionTypeUnitsBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Organisation → Dimension types → View units on Department → the list → + New.
    /// </summary>
    /// <remarks>
    /// <b>No exact row counts.</b> The tenant is shared and other tests in this collection add
    /// units to it, so the assertions are about which rows are there rather than how many.
    /// </remarks>
    [Fact]
    public async Task ViewUnitsOnDepartmentOpensItsListAndItsNewButton()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenOrganisationMenuEntryAsync(page, "Dimension types");

        var department = page.Locator(".dimension-type-row[data-type-code='demo-department']");

        await Assertions.Expect(department).ToBeVisibleAsync();

        await department.Locator(".dimension-type-units").ClickAsync();

        // The list for that type, named for the type rather than for a stored plural — the editor
        // that maintained one went with the menu it existed for.
        await Assertions.Expect(page.Locator("#record-list-heading")).ToContainTextAsync("Department");

        await Assertions.Expect(
            page.Locator("#record-list .record-row[data-record-code='demo-dept-retail']"))
            .ToBeVisibleAsync();

        // And the New button, which is the other half of what the menu entry used to offer.
        await page.ClickAsync("#record-new");

        await Assertions.Expect(page.Locator("#record-create-heading")).ToContainTextAsync("Department");
        await Assertions.Expect(page.Locator(".workmate-unit-picker")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Every type on the list offers the link, including the one no structure uses.
    /// </summary>
    /// <remarks>
    /// <c>cost-centre</c> comes from <c>base.recipe.json</c> and is on no structure, so its list is
    /// legitimately empty — the case where a missing link would be easiest to overlook, because
    /// there is nothing behind it to notice the absence of.
    /// </remarks>
    [Fact]
    public async Task EveryTypeOffersTheLinkIncludingOneOnNoStructure()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenOrganisationMenuEntryAsync(page, "Dimension types");

        var rows = page.Locator("#dimension-type-list .dimension-type-row");
        var links = page.Locator("#dimension-type-list .dimension-type-units");

        await Assertions.Expect(links).ToHaveCountAsync(await rows.CountAsync());

        await page.Locator(".dimension-type-row[data-type-code='cost-centre'] .dimension-type-units")
            .ClickAsync();

        await Assertions.Expect(page.Locator("#record-list-empty")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#record-new")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Opens the admin and clicks an Organisation menu entry, expanding the group if it is closed.
    /// </summary>
    /// <remarks>
    /// <b>One line, because the helper it used to hold is now shared.</b> It had its own copy, and
    /// so did two other classes, and all three asked whether the group was open with Playwright's
    /// non-retrying <c>IsVisibleAsync</c> before the sidebar had necessarily rendered. See
    /// <see cref="AdminMenu"/> for the race that bought and what replaced it.
    /// </remarks>
    internal static Task OpenOrganisationMenuEntryAsync(IPage page, string text) =>
        AdminMenu.ClickAsync(page, "Organisation", text);
}
