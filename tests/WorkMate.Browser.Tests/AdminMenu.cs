using Microsoft.Playwright;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Reaching an admin menu entry by clicking, from a cold page load.
/// </summary>
/// <remarks>
/// <b>One copy, for the reason <see cref="DesignerTree"/> gives and one more.</b> Three test classes
/// had their own version of this — <c>LookupListClickBrowserTests</c>, <c>OrganisationMenuBrowserTests</c>
/// and <c>DimensionTypeUnitsBrowserTests</c> — and all three shared a defect, which is what a copied
/// helper buys:
///
/// <code>
/// var entry = page.GetByRole(AriaRole.Link, new() { NameString = text }).First;
///
/// if (!await entry.IsVisibleAsync())           // ← does not wait
/// {
///     await page.GetByRole(AriaRole.Button, new() { NameString = "Organisation" }).First.ClickAsync();
/// }
/// </code>
///
/// <c>IsVisibleAsync</c> and <c>CountAsync</c> are Playwright's <em>immediate</em> checks: they
/// answer about the instant they are called and never retry, unlike every <c>Assertions.Expect</c>
/// in this suite. So the question "is the menu group already open?" was being asked at whatever
/// moment the page happened to be in. If the sidebar had not rendered yet, the entry was not
/// visible <em>and</em> the group button was not there either, so the helper clicked nothing and
/// returned — and the caller's next line, an auto-waiting click on a link inside a group that is
/// still collapsed, then waited thirty seconds for an element that would never appear.
///
/// Whether it fired depended on how loaded the machine was, which is the signature of an
/// intermittent failure: one full-suite run in three had a single browser test fail. The two
/// browser flakes before it — <c>NoCardClipsItsNameOrItsCode</c> in prompt 3, and the double-fetch
/// and picker races in prompt 4 — were both found the same way, by running the suite under load.
///
/// <b>What this does instead.</b> It waits for the menu to have rendered <em>at all</em> before
/// asking anything about it: either the entry is already visible, which means its group is open, or
/// the group's own button is, which means it is closed. Until one of those is true the question has
/// no answer. Then it asserts the group really did open, so a failure says "the Organisation group
/// did not open" rather than "timed out waiting for a link" — which is the difference between a
/// five-minute diagnosis and the one that cost prompt 3 a review cycle.
///
/// <b>Role-based locators, never the admin theme's own ids.</b> The markup of Orchard's sidebar is
/// not this solution's to depend on, and the accessible name is what a reader navigates by. Matched
/// on a substring of that name, because the theme renders a menu item's words twice — once visibly
/// and once as the collapsed-sidebar title — so a link captioned "Dimension types" has the
/// accessible name "Dimension types Dimension types".
/// </remarks>
internal static class AdminMenu
{
    /// <summary>
    /// Loads the admin and clicks <paramref name="entry"/>, expanding each group on the way to it.
    /// </summary>
    /// <param name="groups">
    /// The groups to open, outermost first. The Organisation menu nests one level since ADR-0024 —
    /// <c>ClickAsync(page, entry: "Dimension types", "Organisation", "Settings")</c> — and the
    /// per-dimension entries sit directly under the group, so most callers pass one.
    /// </param>
    public static async Task ClickAsync(IPage page, string entry, params string[] groups)
    {
        ArgumentNullException.ThrowIfNull(page);

        await page.GotoAsync("/Admin");

        await OpenGroupAsync(page, entry, groups);

        await Link(page, entry).ClickAsync();
    }

    /// <summary>
    /// Expands each of <paramref name="groups"/> that is closed, on a page that is already loaded,
    /// and leaves <paramref name="entry"/> visible.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ClickAsync"/> for the caller that loads the page itself — a test
    /// that sets a culture cookie and reloads before it looks at the menu.
    ///
    /// <b>Each group is opened in turn, and the entry is only checked at the end.</b> A nested
    /// entry is not visible until every group above it is open, so asking "is the entry visible
    /// yet?" between the two would answer no for a reason that has nothing to do with the group
    /// being checked.
    /// </remarks>
    public static async Task OpenGroupAsync(IPage page, string entry, params string[] groups)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(groups);

        var link = Link(page, entry);

        foreach (var group in groups)
        {
            var button = page.GetByRole(AriaRole.Button, new() { NameString = group }).First;

            // The wait that was missing. Either state is a legitimate answer; neither being true
            // yet is not an answer at all, and acting on it is the race this helper exists to
            // close.
            await Assertions.Expect(link.Or(button).First).ToBeVisibleAsync();

            // Already open — the entry is reachable, so there is nothing to expand.
            if (await link.IsVisibleAsync())
            {
                break;
            }

            if (await button.IsVisibleAsync())
            {
                await button.ClickAsync();
            }
        }

        // Asserted, not assumed. A group that did not open fails here, naming the entry it was
        // opened for.
        await Assertions.Expect(link).ToBeVisibleAsync();
    }

    private static ILocator Link(IPage page, string entry) =>
        page.GetByRole(AriaRole.Link, new() { NameString = entry }).First;
}
