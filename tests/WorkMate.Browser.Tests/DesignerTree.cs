using Microsoft.Playwright;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Driving the organisation designer's tree, shared by every test class that measures it.
/// </summary>
/// <remarks>
/// <b>One copy, deliberately.</b> There were two — one in <c>OrganisationDesignerBrowserTests</c>
/// and one in <c>DesignerCardBrowserTests</c> — and they had already drifted: only the second had
/// learned to retry a detached click, and only the first had a bound high enough for a shared
/// tenant. Each was then flaky in a way the other was not, which is exactly what two copies of one
/// helper buys.
/// </remarks>
internal static class DesignerTree
{
    private const string Collapsed =
        "#designer-tree .designer-node[data-expanded='false']:not([data-child-count='0'])";

    /// <summary>The card for one unit, by its English name.</summary>
    /// <remarks>
    /// The <c>&gt;</c> is load-bearing: it matches the node whose <em>own</em> card carries the
    /// name, not every ancestor that happens to contain it. Without it, "the first node containing
    /// Sales" is the structure card at the top of the chart, and anything scoped to it then finds
    /// the real card's controls as hidden descendants — which fails as a thirty-second timeout on
    /// a click, rather than as anything that points at the selector.
    /// </remarks>
    public static ILocator Node(IPage page, string nameEn)
    {
        ArgumentNullException.ThrowIfNull(page);

        return page.Locator(
            $"#designer-tree .designer-node:has(> .designer-card .designer-card-name:text-is('{nameEn}'))");
    }

    /// <summary>Opens every branch, so the assertions see the whole tree rather than its top.</summary>
    /// <remarks>
    /// <b>Why this clicks an element handle rather than a locator.</b> A locator is a query, not an
    /// element: Playwright re-runs it at every use. Counting the collapsed nodes and then clicking
    /// "the first collapsed node" is therefore two queries against a tree that is still settling —
    /// expanding one branch replaces part of the DOM, so the node that was first when it was
    /// counted can be gone by the time the click resolves. The click then waits out its whole
    /// timeout for an element that no longer matches, and the test fails on a timeout that says
    /// nothing about the chart.
    ///
    /// That was the suite's one intermittent failure: roughly one full run in two, and never the
    /// same test twice — whichever card test happened to run first. Resolving a handle pins the
    /// element; if it has since detached, Playwright says so at once and the loop looks again,
    /// which is the right response to "the tree moved underneath me" rather than an error.
    ///
    /// The click timeout is short on purpose. Nothing here waits on a server — the branch is in the
    /// DOM or it is not — so a click that cannot land in two seconds is a click whose element has
    /// gone, and waiting ten more seconds to discover that is ten seconds per retry.
    ///
    /// The round bound is a backstop against a toggle that reopens what it just closed, which would
    /// otherwise hang the suite rather than fail it. It is deliberately far above the tree it
    /// expects: the tenant is shared and every other test in the collection adds units to it, so a
    /// cap set to the demo recipe's own size would turn somebody else's new test into a failure
    /// here.
    /// </remarks>
    public static async Task ExpandEverythingAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        for (var round = 0; round < 500; round++)
        {
            var toggle = await page.QuerySelectorAsync($"{Collapsed} .designer-toggle");

            if (toggle is null)
            {
                return;
            }

            try
            {
                await toggle.ClickAsync(new ElementHandleClickOptions { Timeout = 2_000 });
            }
            catch (TimeoutException)
            {
                // Re-rendered between being resolved and being clicked. Look again: either it is
                // open now, or another handle will be found for it.
                continue;
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        throw new InvalidOperationException(
            "The tree would not stay open: a branch is being re-collapsed as fast as it is opened.");
    }
}
