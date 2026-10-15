using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Choosing a person out of a hundred and fifty-eight, by typing part of their name.
/// </summary>
/// <remarks>
/// <b>The defect.</b> The picker was a <c>&lt;select&gt;</c> built from the first hundred employees
/// of any status. On this very tenant — Zenith, 158 people — fifty-eight of them could not be
/// chosen as a head, as a line manager, or as anything else, from any screen, and the control said
/// nothing about it. Leavers were offered to a form that cannot accept one.
///
/// <b>Why it was not caught.</b> Every test that had exercised the picker ran on a tenant with a
/// handful of people in it, where the cap never bit. A control that is correct below its limit and
/// silently wrong above it passes every test written below the limit — so this one is written on a
/// tenant that is over it, which is why this fixture exists.
///
/// <b>The scenario.</b> Appointing somebody who works in Finance as head of Civil, which is also
/// ADR-0012's case: a head need not be a member of the unit they head, so the picker has to offer
/// the whole tenant rather than the unit's own staff. Found by typing, chosen by clicking, and
/// every step of it a control somebody can see.
/// </remarks>
[Collection(UsesTheZenithPeopleTenant.Name)]
public sealed class EmployeePickerBrowserTests
{
    private readonly ZenithPeopleTenantFixture _tenant;

    public EmployeePickerBrowserTests(ZenithPeopleTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// A Finance employee is appointed head of Civil, found by searching rather than by scrolling.
    /// </summary>
    [Fact]
    public async Task AFinanceEmployeeIsAppointedHeadOfCivilBySearchingForThem()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // Who to appoint: somebody in Finance, read off the employee list rather than assumed, so
        // the test names a real person and says where they work.
        var candidate = await FinanceEmployeeAsync(page);

        candidate.Name.Should().NotBeNullOrWhiteSpace();

        await OpenZenithAsync(page);
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        var civil = DesignerTree.Node(page, "Civil");

        await Assertions.Expect(civil).ToBeVisibleAsync();

        await civil.Locator(".designer-actions summary").First.ClickAsync();
        await civil.Locator("[data-designer-action='sethead']").First.ClickAsync();

        await Assertions.Expect(page.Locator("#unit-head-set")).ToBeVisibleAsync();

        // The control is a search box, not a list. Typing part of the surname is how somebody
        // actually finds a colleague, and it is the only way to reach anybody past the first page.
        var picker = page.Locator(".workmate-employee-picker");
        var input = picker.Locator(".workmate-picker-input");

        await Assertions.Expect(input).ToBeVisibleAsync();

        await input.ClickAsync();
        await input.FillAsync(PartOf(candidate.Name));

        var option = picker.Locator(".workmate-picker-list [role='option']", new()
        {
            HasTextString = candidate.Code,
        });

        await Assertions.Expect(option).ToBeVisibleAsync();

        // The option says where they sit, which is what tells two people with the same name apart.
        await Assertions.Expect(option).ToContainTextAsync("Finance");

        await option.ClickAsync();

        // Dated from today, which the form has already filled in, and submitted.
        await page.ClickAsync("#unit-head-set button[type='submit']");

        // Back on the designer: the card names them. They work in Finance and head Civil, which is
        // the arrangement ADR-0012 exists to make expressible.
        await Assertions.Expect(DesignerTree.Card(page, "Civil").Locator(".designer-card-head"))
            .ToContainTextAsync(candidate.Name);
    }

    /// <summary>
    /// Somebody who has left is not offered for a head appointment, and the form says why.
    /// </summary>
    /// <remarks>
    /// The other half of the old defect: the control offered every status, so a leaver could be
    /// picked and the save would then be refused by the service. Offering a choice that cannot be
    /// made is worse than not offering it, and withholding one without saying so is worse again —
    /// it reads as a missing record. Both are asserted.
    /// </remarks>
    [Fact]
    public async Task ALeaverIsNotOfferedAsAHeadAndTheFormSaysWhy()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // Somebody added and exited through the real screens, so the state under test is one the
        // product produced.
        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");

        await page.FillAsync("#employee-create input[name='Code']", "picker-leaver-1");
        await page.FillAsync("#employee-create input[name='NameEn']", "Wajiha Dastgir");
        await page.FillAsync("#employee-create input[name='NameAr']", "وجيهة دستگير");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2024-01-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        // Searched for rather than scrolled to: this tenant has a hundred and fifty-eight people
        // and the list is paged, so "go back to the list" does not put a new starter in front of
        // you. That the list has a working search is EmployeeScreensBrowserTests' business.
        await page.GotoAsync("/Admin/Employees/Index?search=picker-leaver-1");

        var row = page.Locator(".employee-row[data-employee-code='picker-leaver-1']");

        await Assertions.Expect(row).ToBeVisibleAsync();

        await row.Locator(".employee-status-toggle").ClickAsync();
        await row.Locator("[data-lifecycle-action='Exit']").ClickAsync();

        await page.FillAsync("#employee-exit-preview input[name='LastDay']", "2025-06-30");
        await page.ClickAsync("#employee-exit-preview button[type='submit']");
        await page.ClickAsync("#employee-exit-commit button[type='submit']");

        // Now on the head form, they are not among the candidates.
        await OpenZenithAsync(page);
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        var mechanical = DesignerTree.Node(page, "Mechanical");

        await mechanical.Locator(".designer-actions summary").First.ClickAsync();
        await mechanical.Locator("[data-designer-action='sethead']").First.ClickAsync();

        var picker = page.Locator(".workmate-employee-picker");
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync("Dastgir");

        // The list settles on "no match" rather than on a stale result.
        await Assertions.Expect(picker.Locator(".workmate-picker-list [role='option']"))
            .ToHaveCountAsync(0);

        // And the form says why, rather than leaving it looking like a missing record.
        await Assertions.Expect(picker).ToContainTextAsync("left");
    }

    /// <summary>
    /// The whole control works from the keyboard: type, arrow down, Enter.
    /// </summary>
    /// <remarks>
    /// A picker that can only be used with a mouse is a picker half the people who need it cannot
    /// use. Asserted as the sequence somebody actually performs rather than as the presence of
    /// attributes, because the attributes being right and the keys doing nothing is a thing that
    /// happens.
    /// </remarks>
    [Fact]
    public async Task TheKeyboardAloneChoosesSomebody()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        var candidate = await FinanceEmployeeAsync(page);

        await OpenZenithAsync(page);
        await DesignerTree.Node(page, "Engineering Division").Locator(".designer-toggle").First.ClickAsync();

        var electrical = DesignerTree.Node(page, "Electrical");

        await electrical.Locator(".designer-actions summary").First.ClickAsync();
        await electrical.Locator("[data-designer-action='sethead']").First.ClickAsync();

        await Assertions.Expect(page.Locator("#unit-head-set")).ToBeVisibleAsync();

        var picker = page.Locator(".workmate-employee-picker");
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync(candidate.Code);

        // The option for the person typed, not whichever option is first. Focusing the box opens
        // the list on the unfiltered first page, so "the first option" is visible from the moment
        // the control is touched and waiting for it waits for nothing — the keys would then land on
        // whoever sorts first alphabetically, which is somebody else entirely.
        //
        // This raced before and could not fail: the focus handler used to search for the text in
        // the box, which after a selection is a person's full label, so it found nobody and left
        // the list empty. Fixing that bug is what made the race reachable.
        await Assertions.Expect(
            picker.Locator(".workmate-picker-list [role='option']", new() { HasTextString = candidate.Code }))
            .ToBeVisibleAsync();

        await input.PressAsync("ArrowDown");

        // aria-activedescendant is what tells a screen reader which row the arrow keys are on. If
        // it is absent the list is navigable by sight only, which is the failure this asserts.
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-activedescendant", new Regex(".+"));

        await input.PressAsync("Enter");

        // Chosen without the mouse ever touching an option.
        await Assertions.Expect(input).ToHaveValueAsync(new Regex(Regex.Escape(candidate.Code)));

        await page.ClickAsync("#unit-head-set button[type='submit']");

        await Assertions.Expect(DesignerTree.Card(page, "Electrical").Locator(".designer-card-head"))
            .ToContainTextAsync(candidate.Name);
    }

    /// <summary>
    /// Somebody who works in Finance, read off the employee list rather than hard-coded.
    /// </summary>
    /// <remarks>
    /// The recipe is generated, so the names in it move when the figures do. Reading one off the
    /// screen keeps this test about the picker rather than about the generator's name pool.
    /// </remarks>
    private static async Task<(string Code, string Name)> FinanceEmployeeAsync(IPage page)
    {
        await page.GotoAsync("/Admin/Employees/Index?search=zenith-fin-01");

        var row = page.Locator(".employee-row[data-employee-code='zenith-fin-01']");

        await Assertions.Expect(row).ToBeVisibleAsync();

        var name = await row.Locator(".employee-name").First.InnerTextAsync();

        return ("zenith-fin-01", name.Trim());
    }

    /// <summary>The distinctive half of a name, which is what somebody types.</summary>
    private static string PartOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 1 ? parts[^1] : name;
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
