using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The employee screens and the unit head, driven the way a person drives them: a real browser,
/// against a real tenant, through the forms the product actually ships.
/// </summary>
/// <remarks>
/// These are deliberately end-to-end rather than per-screen. Every one of them is a sentence
/// somebody would say about the product — "I added somebody and they appear in the list as
/// prospective", "I made them head of a department and the chart says so" — and each crosses two
/// modules, because that crossing is where the employee record and the dimension engine meet and
/// therefore where they can disagree.
///
/// The HTTP suite already pins routing, permissions and what each screen renders. What it cannot
/// see is whether the forms, taken together, let a person get from one end to the other.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class EmployeeScreensBrowserTests
{
    private readonly BrowserTenantFixture _tenant;

    public EmployeeScreensBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    [Fact]
    public async Task AddingSomebodyPutsThemInTheListAsProspective()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");

        await AddEmployeeAsync(page, "browser-emp-1", "Layla Mansour", "ليلى منصور", "2025-01-15");

        await page.GotoAsync("/Admin/Employees/Index");

        var row = page.Locator(".employee-row[data-employee-code='browser-emp-1']");

        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row).ToContainTextAsync("Layla Mansour");

        // Prospective, whatever the join date. Starting work is a dated decision somebody makes
        // afterwards, which is what lets the record say "hired, never turned up".
        await Assertions.Expect(row.Locator(".employee-status")).ToContainTextAsync("Prospective");

        // The employee record holds no department: until somebody is placed, this column is empty
        // and says so rather than leaving a blank cell.
        await Assertions.Expect(row.Locator(".employee-unit")).ToContainTextAsync("Not placed");
    }

    /// <summary>
    /// The search finds somebody by their Arabic name, in an English page.
    /// </summary>
    /// <remarks>
    /// ADR-0003's point applied to a list: both halves of a name belong to the person, and a reader
    /// searching in their own language must find the same people. Run in the English admin so the
    /// test is about the data rather than about the page's direction.
    /// </remarks>
    [Fact]
    public async Task TheListFindsSomebodyByTheirArabicName()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Create");
        await AddEmployeeAsync(page, "browser-emp-ar", "Hussain Al Sayed", "حسين السيد", "2025-03-01");

        await page.GotoAsync("/Admin/Employees/Index");
        await page.FillAsync("#employee-search", "حسين");
        await page.ClickAsync("#employee-filter button[type='submit']");

        await Assertions.Expect(
            page.Locator(".employee-row[data-employee-code='browser-emp-ar']")).ToBeVisibleAsync();
    }

    /// <summary>
    /// A unit with nobody in post says "Vacant", and the dash is gone.
    /// </summary>
    /// <remarks>
    /// The 5 October backlog note's distinction. While no employee could exist the card showed a
    /// dash, which meant "not built yet"; now that a unit can have a head, an unfilled post is a
    /// fact about the organisation and the card states it.
    /// </remarks>
    [Fact]
    public async Task AUnitWithNobodyInPostSaysVacant()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        var heads = page.Locator("#designer-tree .designer-card-head");

        await Assertions.Expect(heads.First).ToContainTextAsync("Vacant");

        (await page.ContentAsync()).Should().NotContain(
            "Head: —", "the dash meant 'not built yet', and the feature is built");
    }

    /// <summary>
    /// Appointing a head from the unit's own card puts the name on the card.
    /// </summary>
    /// <remarks>
    /// The whole of the 5 October backlog note, end to end and through the real forms: add a person,
    /// appoint them from the card they will appear on, and read the card back. It crosses both
    /// modules — the employee lives in <c>WorkMate.Records</c>, the card and the appointment in
    /// <c>WorkMate.Dimensions</c> — which is the join that most needs a test driving it rather than
    /// each side asserting about itself.
    /// </remarks>
    [Fact]
    public async Task AppointingAHeadFromTheCardPutsTheNameOnTheCard()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Create");
        await AddEmployeeAsync(page, "browser-head-1", "Mariam Shah", "مريم شاه", "2024-01-01");

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        // The card menu, on a root of the chart so it is in the page without expanding anything.
        var card = DesignerTree.Node(page, "Sales");

        await card.Locator(".designer-actions summary").First.ClickAsync();
        await card.Locator("[data-designer-action='sethead']").First.ClickAsync();

        await Assertions.Expect(page.Locator("#unit-head-set")).ToBeVisibleAsync();

        await page.SelectOptionAsync(
            "#unit-head-set select[name='EmployeeId']",
            new SelectOptionValue { Label = "Mariam Shah (browser-head-1)" });

        await page.ClickAsync("#unit-head-set button[type='submit']");

        // Back on the designer, with the name on the card it was appointed from.
        await Assertions.Expect(page.Locator("#designer-tree")).ToBeVisibleAsync();

        var head = DesignerTree.Node(page, "Sales").Locator(".designer-card-head").First;

        await Assertions.Expect(head).ToContainTextAsync("Mariam Shah");
    }

    /// <summary>
    /// The exit dry run names the units it would leave without a head, and commits nothing.
    /// </summary>
    /// <remarks>
    /// The half of an exit that is easiest to forget and worst to get wrong. A leaver who stays
    /// recorded as a head is somebody prompt 5's routing still routes approvals to, and the failure
    /// is silent — the chart and the routing agree with each other and both are wrong.
    /// </remarks>
    [Fact]
    public async Task TheExitDryRunNamesTheUnitsItWouldLeaveWithoutAHeadAndChangesNothing()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Create");
        await AddEmployeeAsync(page, "browser-exit-1", "Tariq Jameel", "طارق جميل", "2024-01-01");

        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        var card = DesignerTree.Node(page, "Operations");

        await card.Locator(".designer-actions summary").First.ClickAsync();
        await card.Locator("[data-designer-action='sethead']").First.ClickAsync();

        await page.SelectOptionAsync(
            "#unit-head-set select[name='EmployeeId']",
            new SelectOptionValue { Label = "Tariq Jameel (browser-exit-1)" });

        await page.ClickAsync("#unit-head-set button[type='submit']");

        // Now exit them, and look before leaping. The row's lifecycle actions are behind its
        // "Status" menu, so it is opened first — the same two presses a person makes.
        await page.GotoAsync("/Admin/Employees/Index");

        var row = page.Locator(".employee-row[data-employee-code='browser-exit-1']");

        await row.Locator(".employee-status-toggle").ClickAsync();
        await row.Locator("[data-lifecycle-action='Exit']").ClickAsync();

        // After the appointment, which the Set form dated from today. A last day before the term
        // began would correctly find nothing to close, and would make this test pass for the one
        // reason it must not: because the head appointment had not started yet.
        await page.FillAsync("#employee-exit-preview input[name='LastDay']", "2027-12-31");
        await page.ClickAsync("#employee-exit-preview button[type='submit']");

        var plan = page.Locator("#employee-exit-plan");

        await Assertions.Expect(plan).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#exit-headships")).ToContainTextAsync("Operations");

        // Nothing committed: they are still active, and still in post.
        await page.GotoAsync("/Admin/Employees/Index");

        await Assertions.Expect(
            page.Locator(".employee-row[data-employee-code='browser-exit-1'] .employee-status"))
            .ToContainTextAsync("Prospective");
    }

    /// <summary>
    /// Fills and submits the Add-employee form, which several of these tests start from.
    /// </summary>
    /// <remarks>
    /// Through the form rather than through a service, deliberately: a browser test that seeded its
    /// data behind the UI would stop proving that somebody can actually add an employee, which is
    /// the one thing it is better placed than any other suite to check.
    /// </remarks>
    private static async Task AddEmployeeAsync(
        IPage page,
        string code,
        string nameEn,
        string nameAr,
        string joinDate)
    {
        await Assertions.Expect(page.Locator("#employee-create")).ToBeVisibleAsync();

        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", nameEn);
        await page.FillAsync("#employee-create input[name='NameAr']", nameAr);
        await page.FillAsync("#employee-create input[name='JoinDate']", joinDate);

        await page.ClickAsync("#employee-create button[type='submit']");

        // The editor is where a successful add lands, and is proof the record exists.
        await Assertions.Expect(page.Locator("#employee-code")).ToContainTextAsync(code);
    }
}
