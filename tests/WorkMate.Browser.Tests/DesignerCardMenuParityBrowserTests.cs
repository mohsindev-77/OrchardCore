using FluentAssertions;
using Microsoft.Playwright;
using WorkMate.Platform;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// A card fetched when a branch is opened offers exactly what a card the server rendered offers.
/// </summary>
/// <remarks>
/// <b>The defect this exists for.</b> A designer card's menu was described in three places that had
/// to agree by hand — the markup in <c>_DesignerNode</c>, a <c>data-&lt;action&gt;-url</c> attribute
/// per action on the surface, and an object literal in <c>organisation-designer.js</c> — and the
/// script <em>deletes</em> any action it has no URL for. Session A2 added "Appoint a head…" to the
/// markup and to neither of the others, so the roots had it and every card below them silently did
/// not. No error, no console warning.
///
/// <b>Why the A2 browser tests missed it.</b> They asserted on cards that were in the page when it
/// loaded — the structure card and the roots — which are precisely the ones the server renders. The
/// fetched cards, built by cloning a template, were never compared against them. A test that only
/// ever looks at one of two code paths cannot find a difference between them.
///
/// So this compares the two directly, at depth 2 and depth 3, for each permission set. It is
/// written against whatever the menu happens to contain rather than against a list of action names,
/// because a list would have to be kept in step by hand — which is the class of mistake being
/// tested for.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class DesignerCardMenuParityBrowserTests
{
    private readonly DemoCompanyTenantFixture _tenant;

    public DesignerCardMenuParityBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Zenith, which is three levels deep: Division → Department, and Division → Project → Team.
    /// </summary>
    private const string Zenith = "zenith-org";

    [Fact]
    public async Task AFetchedCardOffersExactlyWhatAServerRenderedCardOffers()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        // A root: in the page before anything is expanded, so the server rendered it.
        var atLoad = await MenuOf(page, "Engineering Division");

        atLoad.Should().NotBeEmpty("a root card carries a menu for an administrator");
        atLoad.Should().Contain("sethead", "which is the action that went missing");

        await DesignerTree.ExpandEverythingAsync(page);

        // Depth 2: a department under the division, fetched when the division was opened.
        var depthTwo = await MenuOf(page, "Civil");

        depthTwo.Should().Equal(
            atLoad,
            "a fetched card is built by cloning the same template the server renders, so its menu "
            + "must be the same menu in the same order");

        // Depth 3: a team under a project, fetched when the project was opened — two levels of
        // fetching, so a card cloned from a card that was itself cloned.
        var depthThree = await MenuOf(page, "Site Team A");

        depthThree.Should().Equal(atLoad);
    }

    /// <summary>
    /// The same parity for a reader who may see the chart and change nothing.
    /// </summary>
    /// <remarks>
    /// The auditor holds <c>ViewDimensionHistory</c> alone, so there is no menu at all — on either
    /// kind of card. Worth asserting in both directions: a template rendered with the wrong
    /// permissions would give fetched cards a menu their viewer may not use, which is the same
    /// defect wearing the opposite sign.
    /// </remarks>
    [Fact]
    public async Task AReaderWhoMayChangeNothingGetsNoMenuOnEitherKindOfCard()
    {
        await using var context = await _tenant.SignedInContextAsync(PlatformRoles.Auditor);
        var page = await context.NewPageAsync();

        await OpenZenithAsync(page);

        (await MenuOf(page, "Engineering Division")).Should().BeEmpty(
            "a reader gets the chart with no action menu on it, rather than one that is hidden");

        await DesignerTree.ExpandEverythingAsync(page);

        (await MenuOf(page, "Civil")).Should().BeEmpty();
        (await MenuOf(page, "Site Team A")).Should().BeEmpty();
    }

    /// <summary>
    /// A head appointed from a depth-2 card's own menu, and the name on that card afterwards.
    /// </summary>
    /// <remarks>
    /// The defect's consequence, end to end: before the fix there was no control on this card to
    /// click at all. Everything here is a click — the branch is opened by pressing its toggle, the
    /// menu by pressing the card's own ⋯.
    /// </remarks>
    [Fact]
    public async Task AHeadCanBeAppointedFromADepthTwoCardAndTheCardShowsTheName()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // Somebody to appoint, added through the real form.
        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");

        await page.FillAsync("#employee-create input[name='Code']", "zenith-head-civil");
        await page.FillAsync("#employee-create input[name='NameEn']", "Adeel Mahmood");
        await page.FillAsync("#employee-create input[name='NameAr']", "عديل محمود");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2024-01-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        await Assertions.Expect(page.Locator("#employee-code")).ToContainTextAsync("zenith-head-civil");

        await OpenZenithAsync(page);

        // Open the division by clicking its toggle, so the Civil card is one the browser fetched.
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        var civil = DesignerTree.Node(page, "Civil");

        await Assertions.Expect(civil).ToBeVisibleAsync();
        await Assertions.Expect(civil.Locator(".designer-card-head")).ToContainTextAsync("Vacant");

        await civil.Locator(".designer-actions summary").First.ClickAsync();
        await civil.Locator("[data-designer-action='sethead']").First.ClickAsync();

        await Assertions.Expect(page.Locator("#unit-head-set")).ToBeVisibleAsync();

        await page.SelectOptionAsync(
            "#unit-head-set select[name='EmployeeId']",
            new SelectOptionValue { Label = "Adeel Mahmood (zenith-head-civil)" });

        await page.ClickAsync("#unit-head-set button[type='submit']");

        // Back on the designer, which reopens the branch the appointment was made in: the redirect
        // carries the unit as the expand target, so the card is on screen without being clicked for
        // again. Clicking the toggle here would close it.
        await Assertions.Expect(DesignerTree.Node(page, "Civil").Locator(".designer-card-head"))
            .ToContainTextAsync("Adeel Mahmood");

        // And again on a card built from scratch: back to the chart with nothing expanded, then
        // open the division by pressing its toggle, which fetches the row and builds each card
        // from the template. This is the half that failed — the script filled every line of a
        // fetched card except this one, so the card kept the template's words and reported every
        // post in the organisation vacant however many were filled. Reopening the branch already
        // on screen would not have shown it: the children are built once and then only hidden.
        await OpenZenithAsync(page);
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        await Assertions.Expect(DesignerTree.Node(page, "Civil").Locator(".designer-card-head"))
            .ToContainTextAsync("Adeel Mahmood");

        // The posts next to it are still empty, which is a different statement from the one the
        // broken card made: "Vacant" has to be read off the appointment, not off the template.
        await Assertions.Expect(DesignerTree.Node(page, "Electrical").Locator(".designer-card-head"))
            .ToContainTextAsync("Vacant");
    }

    /// <summary>
    /// The headcount on a card reads as a sentence, singular when there is one person.
    /// </summary>
    /// <remarks>
    /// It said "1 employees" until the label moved to the server, where the localiser can choose
    /// between the forms a language actually has — two in English, six in Arabic.
    /// </remarks>
    [Fact]
    public async Task OnePersonAtAUnitReadsAsOneEmployee()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");

        await page.FillAsync("#employee-create input[name='Code']", "zenith-one-person");
        await page.FillAsync("#employee-create input[name='NameEn']", "Sana Iqbal");
        await page.FillAsync("#employee-create input[name='NameAr']", "سناء إقبال");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2024-01-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        // Placed on Mechanical, which the demo recipe leaves empty, by clicking through from the
        // editor rather than by going to the placement screen directly.
        await page.ClickAsync("#employee-placement-link");
        await page.ClickAsync("a[href*='Placement/Place']");

        await page.SelectOptionAsync(
            "#employee-place select[name='StructureId']",
            new SelectOptionValue { Label = "Zenith Engineering & Construction (zenith-org)" });

        await page.SelectOptionAsync(
            "#place-unit",
            new SelectOptionValue { Label = "Mechanical (zenith-dept-mechanical)" });

        await page.ClickAsync("#employee-place button[type='submit']");

        await OpenZenithAsync(page);
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        await Assertions.Expect(DesignerTree.Node(page, "Mechanical").Locator(".designer-card-employees"))
            .ToHaveTextAsync("1 employee");
    }

    private static async Task OpenZenithAsync(IPage page)
    {
        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        // By clicking the structure picker rather than by putting the id in the query string: the
        // picker is how a person chooses, and a test that bypassed it would not notice it breaking.
        await page.SelectOptionAsync("#structureId", new SelectOptionValue { Value = await ZenithIdAsync(page) });
        await Assertions.Expect(page.Locator("#designer-tree")).ToBeVisibleAsync();
    }

    /// <summary>Zenith's structure id, read off the picker's own options.</summary>
    private static async Task<string> ZenithIdAsync(IPage page) =>
        await page.EvaluateAsync<string>(
            """
            code => {
                const option = [...document.querySelectorAll('#structureId option')]
                    .find(option => option.textContent.includes(code));

                return option ? option.value : '';
            }
            """,
            Zenith);

    /// <summary>
    /// The actions on one card's menu, in order — or nothing when the card has no menu.
    /// </summary>
    /// <remarks>
    /// Read from the DOM rather than compared against a list of expected names, so that adding an
    /// action needs no change here and a dropped one still fails: the comparison is between the two
    /// rendering paths, which is the thing that can disagree.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> MenuOf(IPage page, string unitName)
    {
        var card = DesignerTree.Node(page, unitName);

        await Assertions.Expect(card).ToBeVisibleAsync();

        return await card.EvaluateAsync<string[]>(
            """
            node => [...node.querySelectorAll(':scope > .designer-card .designer-actions-menu [data-designer-action]')]
                .map(link => link.getAttribute('data-designer-action'))
            """);
    }
}
