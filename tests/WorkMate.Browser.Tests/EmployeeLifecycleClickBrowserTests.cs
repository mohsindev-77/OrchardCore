using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The whole employment lifecycle, reached only by clicking.
/// </summary>
/// <remarks>
/// <b>Why this test exists, and why the A2 browser tests did not catch what it catches.</b> Session
/// A2 shipped all five lifecycle transitions with a control for exactly one of them: the list row
/// offered Exit, and the editor's "Change status" link went to Exit as well. Activate — the move
/// every newly added employee needs — was reachable only by typing
/// <c>/Admin/Employees/Lifecycle/Activate/{id}</c>.
///
/// The A2 browser tests missed it because they <em>navigated by URL</em>. They called
/// <c>page.GotoAsync("/Admin/Employees/Create")</c> and posted the form, which proves the screen
/// works and says nothing about whether anybody can get to it. A screen with no link to it passes
/// every test that starts by going there.
///
/// So this one never types a path after the first. Everything else is a click on something a person
/// can see, which is the only way to test that a control exists at all.
/// </remarks>
[Collection(UsesTheBrowserTenant.Name)]
public sealed class EmployeeLifecycleClickBrowserTests
{
    private readonly BrowserTenantFixture _tenant;

    public EmployeeLifecycleClickBrowserTests(BrowserTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Create, activate, put on leave, activate again, exit, reinstate — every step by clicking.
    /// </summary>
    [Fact]
    public async Task TheWholeLifecycleIsReachableByClicking()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // The one navigation. Everything after this is a control somebody can see and press.
        await page.GotoAsync("/Admin/Employees/Index");

        await page.ClickAsync("a[href*='Employees/Create']");
        await AddAsync(page, "click-life-1", "Noura Al Habsi", "نورة الحبسي", "2024-01-01");

        // Straight from the editor, which is where adding somebody lands: the newly created record
        // is prospective and the one thing to do with it has a button.
        await Assertions.Expect(page.Locator("#employee-activate-hint")).ToBeVisibleAsync();
        await TransitionFromEditorAsync(page, "Activate", "2024-02-01");
        await ExpectStatusAsync(page, "click-life-1", "Active");

        // And from the list's Status menu, which is the other place the same set is offered.
        await TransitionFromListAsync(page, "click-life-1", "PutOnLeave", "2025-05-01");
        await ExpectStatusAsync(page, "click-life-1", "On leave");

        await TransitionFromListAsync(page, "click-life-1", "Activate", "2025-09-01");
        await ExpectStatusAsync(page, "click-life-1", "Active");

        await TransitionFromListAsync(page, "click-life-1", "Suspend", "2026-01-15");
        await ExpectStatusAsync(page, "click-life-1", "Suspended");

        await TransitionFromListAsync(page, "click-life-1", "Activate", "2026-02-01");
        await ExpectStatusAsync(page, "click-life-1", "Active");

        // The exit is the one with a dry run, so it takes two presses rather than one.
        await OpenStatusMenuAsync(page, "click-life-1");
        await page.ClickAsync(StatusMenuItem("click-life-1", "Exit"));

        await page.FillAsync("#employee-exit-preview input[name='LastDay']", "2026-06-30");
        await page.ClickAsync("#employee-exit-preview button[type='submit']");
        await Assertions.Expect(page.Locator("#employee-exit-plan")).ToBeVisibleAsync();
        await page.ClickAsync("#employee-exit-commit button[type='submit']");

        await ExpectStatusAsync(page, "click-life-1", "Exited");

        // A leaver is offered a rehire and nothing else — not Activate, which would describe it as
        // something it is not, and not Suspend or Put on leave, which describe an employment that
        // is running.
        await OpenStatusMenuAsync(page, "click-life-1");

        await Assertions.Expect(page.Locator(StatusMenuItem("click-life-1", "Reinstate"))).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(StatusMenuItem("click-life-1", "Activate"))).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(StatusMenuItem("click-life-1", "Suspend"))).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(StatusMenuItem("click-life-1", "PutOnLeave"))).ToHaveCountAsync(0);

        await page.ClickAsync(StatusMenuItem("click-life-1", "Reinstate"));
        await page.FillAsync("#employee-transition input[name='EffectiveFrom']", "2026-11-01");
        await page.ClickAsync("#employee-transition button[type='submit']");

        await ExpectStatusAsync(page, "click-life-1", "Active");
    }

    /// <summary>
    /// A prospective employee is offered the two moves that are valid, and no others.
    /// </summary>
    /// <remarks>
    /// The menu is built from the same table the service validates against, so an offer that the
    /// service would refuse is a bug in one of them. Asserting the absences is the half that would
    /// have caught A2's defect from the other direction.
    /// </remarks>
    [Fact]
    public async Task AProspectiveEmployeeIsOfferedOnlyTheValidMoves()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");
        await AddAsync(page, "click-valid-1", "Bilal Qasim", "بلال قاسم", "2024-01-01");

        await page.ClickAsync("a[href*='Employees/Index']");
        await OpenStatusMenuAsync(page, "click-valid-1");

        await Assertions.Expect(page.Locator(StatusMenuItem("click-valid-1", "Activate"))).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(StatusMenuItem("click-valid-1", "Exit"))).ToBeVisibleAsync();

        // Neither of these is a move a prospective employee can make: both describe an employment
        // that has already started.
        await Assertions.Expect(page.Locator(StatusMenuItem("click-valid-1", "PutOnLeave"))).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(StatusMenuItem("click-valid-1", "Suspend"))).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(StatusMenuItem("click-valid-1", "Reinstate"))).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A reader without the permission is offered no lifecycle control at all.
    /// </summary>
    /// <remarks>
    /// Not rendered and disabled — not rendered. A disabled button for an action somebody may not
    /// take tells them only that the product has a feature they cannot use.
    /// </remarks>
    [Fact]
    public async Task AReaderWithoutThePermissionSeesNoStatusControls()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");
        await AddAsync(page, "click-perm-1", "Rashid Noor", "راشد نور", "2024-01-01");

        await using var auditor = await _tenant.SignedInContextAsync(WorkMate.Platform.PlatformRoles.Auditor);
        var readerPage = await auditor.NewPageAsync();

        await readerPage.GotoAsync("/Admin/Employees/Index");

        await Assertions.Expect(
            readerPage.Locator(".employee-row[data-employee-code='click-perm-1']")).ToBeVisibleAsync();

        await Assertions.Expect(readerPage.Locator(".employee-status-menu")).ToHaveCountAsync(0);
    }

    // ---- clicking, rather than navigating ---------------------------------------------

    private static string Row(string code) => $".employee-row[data-employee-code='{code}']";

    private static string StatusMenuItem(string code, string action) =>
        $"{Row(code)} .employee-status-menu [data-lifecycle-action='{action}']";

    private static async Task OpenStatusMenuAsync(IPage page, string code)
    {
        // The list, reached by clicking rather than by going there.
        if (await page.Locator(Row(code)).CountAsync() == 0)
        {
            await page.ClickAsync("a[href*='Employees/Index']");
        }

        var menu = page.Locator($"{Row(code)} .employee-status-menu");

        await Assertions.Expect(menu).ToBeVisibleAsync();

        if (await menu.GetAttributeAsync("open") is null)
        {
            await menu.Locator(".employee-status-toggle").ClickAsync();
        }
    }

    private static async Task TransitionFromListAsync(IPage page, string code, string action, string date)
    {
        await OpenStatusMenuAsync(page, code);
        await page.ClickAsync(StatusMenuItem(code, action));

        await page.FillAsync("#employee-transition input[name='EffectiveFrom']", date);
        await page.ClickAsync("#employee-transition button[type='submit']");
    }

    private static async Task TransitionFromEditorAsync(IPage page, string action, string date)
    {
        await page.ClickAsync($".employee-status-actions [data-lifecycle-action='{action}']");

        await page.FillAsync("#employee-transition input[name='EffectiveFrom']", date);
        await page.ClickAsync("#employee-transition button[type='submit']");
    }

    private static async Task ExpectStatusAsync(IPage page, string code, string status)
    {
        if (await page.Locator(Row(code)).CountAsync() == 0)
        {
            await page.ClickAsync("a[href*='Employees/Index']");
        }

        await Assertions.Expect(page.Locator($"{Row(code)} .employee-status")).ToContainTextAsync(status);
    }

    private static async Task AddAsync(IPage page, string code, string nameEn, string nameAr, string joinDate)
    {
        await Assertions.Expect(page.Locator("#employee-create")).ToBeVisibleAsync();

        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", nameEn);
        await page.FillAsync("#employee-create input[name='NameAr']", nameAr);
        await page.FillAsync("#employee-create input[name='JoinDate']", joinDate);

        await page.ClickAsync("#employee-create button[type='submit']");

        await Assertions.Expect(page.Locator("#employee-code")).ToContainTextAsync(code);
    }
}
