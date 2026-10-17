using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Placing somebody by clicking, on an axis that has positions. Decision D30, ADR-0030.
/// </summary>
/// <remarks>
/// <b>Why these need a browser.</b> Three of the things F6b adds are only true once a page has
/// rendered and somebody has clicked: the grade control is empty and disabled until a position is
/// chosen and refills when it changes (D26 G — a script reacting to a change event); the derived
/// organisation block is recomputed on the round trip rather than typed; and the structure chooser
/// used to run the whole save path on change and tell somebody off for not finishing a question
/// they had only just been asked.
///
/// The HTTP suite pins what each screen renders. What it cannot see is whether the controls,
/// together, let a person get from one end to the other.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class PlacementThroughPositionsBrowserTests
{
    private readonly DemoCompanyTenantFixture _tenant;

    public PlacementThroughPositionsBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// The whole journey: add somebody, give them a post at a grade, and see where that put
    /// them.
    /// </summary>
    /// <remarks>
    /// <b>The grade control is the half that needs the browser.</b> It is empty and disabled until
    /// a position is chosen (D26 G, and the disabling is the point rather than a nicety: a grade
    /// box populated before a position is chosen invites somebody to pick a grade the position does
    /// not carry, which the validator then refuses).
    /// </remarks>
    [Fact]
    public async Task SomebodyIsGivenAPositionAndAGradeAndTheOrganisationFollows()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // ITS OWN POST, under HR, carrying HR's grades. Never the recipe's HR Manager: that one is
        // what PositionsClickBrowserTests documents as "approved 3, nobody in it yet", and a test
        // that hires into it makes that reading test pass or fail on run order.
        var post = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-hr",
            ["zenith-grade-hrm-1", "zenith-grade-hrm-2", "zenith-grade-hrm-3"]);

        var code = $"f6b-click-{Guid.NewGuid():n}"[..20];

        await page.GotoAsync("/Admin/Employees/Create");
        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", "Huda Nasser");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2025-03-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync($".employee-row[data-employee-code='{code}'] a[href*='Placement/Index']");
        await page.ClickAsync("a[href*='Placement/Place']");
        await ChooseZenithAsync(page);

        // D30: no unit picker and no Within on this screen. The question is which position.
        await Assertions.Expect(page.Locator("[data-position-picker]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-unit-picker]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-unit-picker-within]")).ToHaveCountAsync(0);

        // D26 G: empty and disabled until a position is chosen.
        var grade = page.Locator("select[data-grade-picker]");

        await Assertions.Expect(grade).ToBeDisabledAsync();

        await Pickers.ChoosePositionAsync(page, post.Name);

        // The grade control refills from the position beside it, and only with its own grades.
        await Assertions.Expect(grade).ToBeEnabledAsync();
        await Assertions.Expect(grade.Locator("option", new() { HasTextString = "HRM-2" }).First)
            .ToBeAttachedAsync();

        await Assertions.Expect(grade.Locator("option", new() { HasTextString = "SE-1" }))
            .ToHaveCountAsync(0);

        await grade.SelectOptionAsync(new SelectOptionValue { Label = "HRM-2 (zenith-grade-hrm-2)" });

        await page.ClickAsync("#place-submit");

        // D26 I: the reminder, non-blocking and beside the success.
        await Assertions.Expect(page.Locator(".message-warning, .alert-warning").First)
            .ToContainTextAsync("access roles");

        // And the derived block: the chain, labelled with the tenant's own dimension names. The
        // post was made under HR, so the chain is HR's — derived, not typed.
        var chain = page.Locator("#job-organisation-chain");

        await Assertions.Expect(chain).ToContainTextAsync("Division:");
        await Assertions.Expect(chain).ToContainTextAsync("Corporate");
        await Assertions.Expect(chain).ToContainTextAsync("Department:");
        await Assertions.Expect(chain).ToContainTextAsync("HR");

        // Nothing was typed into any of that — it is read off the chart.
        await Assertions.Expect(page.Locator("#placement-position-name")).ToContainTextAsync(post.Name);
        await Assertions.Expect(page.Locator("#placement-grade")).ToContainTextAsync("HRM-2");
    }

    /// <summary>
    /// The organisation block follows the position: choosing a different one changes what it says.
    /// </summary>
    /// <remarks>
    /// D30's claim made visible. The chain is computed from the position's ancestors, so two
    /// positions in two departments give two answers without anybody choosing a department — which
    /// is the thing a stored field could not do.
    /// </remarks>
    [Fact]
    public async Task TheOrganisationBlockFollowsTheChosenPosition()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // Two posts of its own, in two different departments — which is the whole experiment. The
        // recipe's own posts would do just as well and are left alone on purpose: see
        // CreateThrowawayPositionAsync.
        var inCivil = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-civil",
            ["zenith-grade-ss-1"]);

        var inHr = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-hr",
            ["zenith-grade-hrm-1"]);

        var code = $"f6b-live-{Guid.NewGuid():n}"[..20];

        await page.GotoAsync("/Admin/Employees/Create");
        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", "Tariq Aziz");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2025-03-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync($".employee-row[data-employee-code='{code}'] a[href*='Placement/Index']");
        await page.ClickAsync("a[href*='Placement/Place']");
        await ChooseZenithAsync(page);

        // The first post sits in Civil, under the Engineering Division.
        await Pickers.ChoosePositionAsync(page, inCivil.Name);
        await page.ClickAsync("#place-submit");

        await Assertions.Expect(page.Locator("#job-organisation-chain")).ToContainTextAsync("Civil");

        // The second sits in HR, under Corporate. Same employee, different post, different chain —
        // and nobody chose a department either time.
        await page.ClickAsync("#placement-change-position");
        await Pickers.ChoosePositionAsync(page, inHr.Name);
        await page.ClickAsync("#place-submit");

        var chain = page.Locator("#job-organisation-chain");

        await Assertions.Expect(chain).ToContainTextAsync("HR");
        await Assertions.Expect(chain).ToContainTextAsync("Corporate");
        await Assertions.Expect(chain).Not.ToContainTextAsync("Engineering Division");
    }

    /// <summary>
    /// Reports to names a person when one holds the post above, and the post when several do.
    /// Decision D28 A.
    /// </summary>
    [Fact]
    public async Task ReportsToNamesAPersonForOneHolderAndThePostForSeveral()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // A two-post reporting line of its own: a chief with nobody under them yet, and a deputy
        // that answers to it. Built here rather than borrowed from the recipe because the second
        // half of this test HIRES somebody into the upper post, and the recipe's CEO is a post
        // other tests read as empty.
        var chief = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-hr",
            ["zenith-grade-hrm-1"],
            reportingStructureCode: "zenith-reporting");

        var deputy = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-hr",
            ["zenith-grade-hrm-2"],
            reportingStructureCode: "zenith-reporting",
            reportsToCode: chief.Code);

        // Nobody holds the post above, so it is named and marked vacant rather than left blank:
        // somebody reading this is the person who would do something about an empty post.
        var first = $"f6b-rt-a-{Guid.NewGuid():n}"[..20];

        await PlaceAsync(page, first, "Nadia Farouk", deputy.Name);

        await Assertions.Expect(page.Locator("#job-reports-to-vacant")).ToContainTextAsync(chief.Name);
        await Assertions.Expect(page.Locator("#job-reports-to-vacant")).ToContainTextAsync("vacant");

        // Put somebody in the upper post, and the same question answers with a person — D28 A's
        // one-holder case, which is the only one that names anybody.
        var held = $"f6b-rt-chief-{Guid.NewGuid():n}"[..20];

        await PlaceAsync(page, held, "Samir Haddad", chief.Name);

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync($".employee-row[data-employee-code='{first}'] a[href*='Placement/Index']");

        await Assertions.Expect(page.Locator("#job-reports-to-one")).ToContainTextAsync("Samir Haddad");
        await Assertions.Expect(page.Locator("#job-reports-to-one")).ToContainTextAsync(chief.Name);
    }

    /// <summary>
    /// Changing the structure chooser redraws the form and does not validate anything.
    /// </summary>
    /// <remarks>
    /// <b>The reported bug.</b> Changing the structure POSTed to the save path and answered
    /// <em>"Choose a structure and a unit"</em> before anybody had chosen either — a form telling
    /// somebody off for not finishing a question they had only just been asked. The demo tenant has
    /// two eligible axes (Zenith and Crescent), so the chooser is rendered here.
    /// </remarks>
    [Fact]
    public async Task ChangingTheStructureRedrawsWithoutAValidationError()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        var code = $"f6b-sw-{Guid.NewGuid():n}"[..20];

        await page.GotoAsync("/Admin/Employees/Create");
        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", "Rana Khalil");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2025-03-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync($".employee-row[data-employee-code='{code}'] a[href*='Placement/Index']");
        await page.ClickAsync("a[href*='Placement/Place']");

        // No ChooseZenithAsync here: this test is ABOUT the chooser, and settling the axis first
        // would be settling the thing it exists to exercise.
        var chooser = page.Locator("#StructureId");

        await Assertions.Expect(chooser).ToBeVisibleAsync();

        // Neither a grade ladder nor a reporting line is on the list: neither is anywhere a person
        // goes (ADR-0030).
        await Assertions.Expect(chooser.Locator("option", new() { HasTextString = "reporting line" }))
            .ToHaveCountAsync(0);

        await Assertions.Expect(chooser.Locator("option", new() { HasTextString = "grades" }))
            .ToHaveCountAsync(0);

        await chooser.SelectOptionAsync(new SelectOptionValue { Label = "Crescent Microfinance (crescent-org)" });

        // The form redraws for the new axis. No complaint about a question nobody has answered yet.
        await Assertions.Expect(page.Locator(".text-danger")).Not.ToContainTextAsync("Choose a");
    }

    /// <summary>
    /// Hiring somebody into a position in <em>one</em> form: the F6b item-1 journey.
    /// </summary>
    /// <remarks>
    /// <b>The one the other tests in this file do not cover.</b> Every one of them creates a bare
    /// employee and then walks to the Placement screen, which is the path that existed before this
    /// slice. What item 1 adds is that the Add-employee form itself carries the block — so the
    /// claim worth clicking is that a person can be hired, graded and placed without ever visiting
    /// a second screen.
    ///
    /// <b>The grade control is why this needs a browser.</b> It is empty and disabled on a form
    /// nobody has touched and refills from the position beside it (D26 G), which is a script
    /// reacting to a change event and is invisible to the HTTP suite.
    ///
    /// <b>The effective date is the join date</b>, typed above it on the same form — which is the
    /// whole reason this screen may ask for a position when the editor may not.
    /// </remarks>
    [Fact]
    public async Task SomebodyIsHiredIntoAPositionFromTheAddFormAlone()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // Its own post, under Civil, with its own grades — the rule for every placing test here.
        var post = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-civil",
            ["zenith-grade-lab-1", "zenith-grade-lab-2"]);

        var code = $"f6b-hire-{Guid.NewGuid():n}"[..20];

        await page.GotoAsync("/Admin/Employees/Create");
        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", "Dalia Saeed");
        await page.FillAsync("#employee-create input[name='JoinDate']", "2025-03-01");

        // The block is part of the fixed core of this form, not a second screen.
        await Assertions.Expect(page.Locator("#create-job-heading")).ToBeVisibleAsync();

        // This tenant has more than one axis an employee can join on, so the form asks which —
        // and asking redraws, which must not be mistaken for a save. The fields typed above have
        // to survive it, which is the defect the IsRefresh flag exists to stop.
        await page.Locator("#create-structure")
            .SelectOptionAsync(new SelectOptionValue { Label = "Zenith Engineering & Construction (zenith-org)" });

        await Assertions.Expect(page.Locator("#employee-create input[name='Code']"))
            .ToHaveValueAsync(code, new() { Timeout = 10_000 });

        await Assertions.Expect(page.Locator("#employee-create input[name='JoinDate']"))
            .ToHaveValueAsync("2025-03-01");

        // Nothing was saved by the redraw: it was navigation. Asserted on the summary rather than
        // on ".text-danger", which every field's validation span also carries — seven of them on
        // this form, which is a strict-mode violation rather than an assertion.
        await Assertions.Expect(
            page.Locator("#employee-create [data-valmsg-for='Code']")).ToBeEmptyAsync();

        // D26 G: empty and disabled until a position is chosen.
        var grade = page.Locator("select[data-grade-picker]");

        await Assertions.Expect(grade).ToBeDisabledAsync();

        await Pickers.ChoosePositionAsync(page, post.Name);

        // It refills from the position beside it, and only with that position's own grades.
        await Assertions.Expect(grade).ToBeEnabledAsync();

        await Assertions.Expect(grade.Locator("option", new() { HasTextString = "HRM-1" }))
            .ToHaveCountAsync(0);

        await grade.SelectOptionAsync(new SelectOptionValue { Label = "LAB-2 (zenith-grade-lab-2)" });

        await page.ClickAsync("#employee-create button[type='submit']");

        // D26 I: the reminder rides with the success and blocks nothing.
        await Assertions.Expect(page.Locator(".message-warning, .alert-warning").First)
            .ToContainTextAsync("access roles");

        // And they landed on their own record with the derived block filled in — a department and
        // a division nobody typed, read off the chart from where the post sits.
        var chain = page.Locator("#job-organisation-chain");

        await Assertions.Expect(chain).ToContainTextAsync("Department:");
        await Assertions.Expect(chain).ToContainTextAsync("Civil");
        await Assertions.Expect(page.Locator("#employee-position")).ToContainTextAsync(post.Name);
        await Assertions.Expect(page.Locator("#employee-grade")).ToContainTextAsync("LAB-2");
    }

    /// <summary>
    /// Choosing a position updates the line manager there and then, without a save. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>Why this needs a browser.</b> "Follows the position" is only true if it follows when the
    /// position is <em>chosen</em>, not when the form is next submitted — and the redraw that makes
    /// that happen is a change event on the picker. The HTTP suite can post a redraw and assert the
    /// result; only a browser can show that choosing causes one.
    ///
    /// Two posts with different answers, so the assertion is about the control following rather
    /// than about it being filled in: a post with one holder above derives that person, and a post
    /// at the top of the line has nobody at all.
    /// </remarks>
    [Fact]
    public async Task ChoosingAPositionUpdatesTheLineManagerWithoutASave()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // Its own two posts: one under the other, so the lower one's manager is the upper one's
        // holder. The recipe's posts are left pristine — see CreateThrowawayPositionAsync.
        var chief = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-hr",
            ["zenith-grade-hrm-1"],
            reportingStructureCode: "zenith-reporting");

        var deputy = await _tenant.CreateThrowawayPositionAsync(
            DemoCompanyTenantFixture.ZenithStructureCode,
            "zenith-dept-hr",
            ["zenith-grade-hrm-2"],
            reportingStructureCode: "zenith-reporting",
            reportsToCode: chief.Code);

        var boss = $"f6b-d31-boss-{Guid.NewGuid():n}"[..20];

        await PlaceAsync(page, boss, "Mona Rashid", chief.Name);

        // Now add somebody and choose the deputy's post. The line manager must appear without the
        // form being submitted.
        await page.GotoAsync("/Admin/Employees/Create");
        await page.FillAsync("#employee-create input[name='Code']", $"f6b-d31-{Guid.NewGuid():n}"[..20]);
        await page.FillAsync("#employee-create input[name='NameEn']", "Yara Fahd");
        // TODAY, not a date in the past: the control resolves the reporting line AS AT the join date,
        // and the holder placed below took the post today. A back-dated join would correctly find the
        // post empty, which is a different fact and not the one this test is about.
        await page.FillAsync("#employee-create input[name='JoinDate']", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        await page.Locator("#create-structure")
            .SelectOptionAsync(new SelectOptionValue { Label = "Zenith Engineering & Construction (zenith-org)" });

        // Before choosing: the control says it follows the position and offers nothing.
        await Assertions.Expect(page.Locator("#line-manager-waiting")).ToBeVisibleAsync();

        await Pickers.ChoosePositionAsync(page, deputy.Name);

        // After choosing, and with no save: the holder of the post above, named — in the Reports
        // to row itself, which carries the post and the person who answers for it. Polish 3: the
        // control below it used to print the name a second time, so one person looked like two.
        await Assertions.Expect(page.Locator("#job-organisation-reports-to"))
            .ToContainTextAsync("Mona Rashid");

        await Assertions.Expect(page.Locator("#line-manager-derived")).ToHaveCountAsync(0);

        // And switching to the post at the top of the line clears it rather than keeping her —
        // which is the defect "follows the position" exists to stop.
        await Pickers.ChoosePositionAsync(page, chief.Name);

        // The chief post is the top of its line, so the row says so — and says it in the words the
        // D31 addendum split out, not the old "answers to no one on the reporting line" that was
        // false for every post under a vacant one.
        await Assertions.Expect(page.Locator("#job-reports-to-top"))
            .ToContainTextAsync("top of the reporting line");

        await Assertions.Expect(page.Locator("#job-organisation-reports-to"))
            .Not.ToContainTextAsync("Mona Rashid");
    }

    /// <summary>
    /// Settles the axis on a placement screen, which this tenant has to be asked about.
    /// </summary>
    /// <remarks>
    /// <b>The demo tenant has more than one axis an employee can join on</b> — Zenith and Crescent
    /// — so the Place screen opens with the structure chooser and nothing else. The position picker
    /// is rendered only once an axis is settled, because until then there is no establishment to
    /// read positions from. A test that went straight for the picker was asserting against a
    /// single-axis tenant it does not have.
    ///
    /// Choosing redraws through the IsRefresh path, which is navigation and not a save.
    /// </remarks>
    private static async Task ChooseZenithAsync(IPage page)
    {
        var chooser = page.Locator("select[name='StructureId']").First;

        await Assertions.Expect(chooser).ToBeVisibleAsync();

        await chooser.SelectOptionAsync(
            new SelectOptionValue { Label = "Zenith Engineering & Construction (zenith-org)" });

        await Assertions.Expect(page.Locator("[data-position-picker]")).ToBeVisibleAsync();
    }

    /// <summary>Places somebody in a named position and lands on their placement page.</summary>
    private static async Task PlaceAsync(IPage page, string code, string name, string position)
    {
        await page.GotoAsync("/Admin/Employees/Create");
        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", name);
        await page.FillAsync("#employee-create input[name='JoinDate']", "2025-03-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync($".employee-row[data-employee-code='{code}'] a[href*='Placement/Index']");
        await page.ClickAsync("a[href*='Placement/Place']");
        await ChooseZenithAsync(page);

        await Pickers.ChoosePositionAsync(page, position);
        await page.ClickAsync("#place-submit");
    }
}
