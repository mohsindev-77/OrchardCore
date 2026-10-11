using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Choosing somebody with the mouse, and with the keyboard, and the choice surviving the submit.
/// </summary>
/// <remarks>
/// <b>The defect.</b> After choosing somebody with the mouse the control announced "No match. Try
/// part of a name or an employee code." at a reader who had just chosen successfully. The cause was
/// that the selection was never recorded as a fact: it was inferred from the text in the box, and
/// the box holds a chosen person's <em>full label</em> — name, code and unit. The focus handler then
/// re-read that label as though it were a search term, found nobody, and said so.
///
/// The posted value survived, which made it worse rather than better: the control claimed the
/// selection had failed while quietly holding a correct one. Its mirror image had the same cause and
/// is the dangerous one — typing over a selection left the old id in the hidden
/// <c>&lt;select&gt;</c>, so the form would post somebody the reader had visibly moved away from.
///
/// <b>Why the first picker tests missed it.</b> They selected an option and asserted the page that
/// came back, and the page that came back was right. What they never looked at was the control's own
/// state in between — the hidden <c>&lt;select&gt;</c> that is what actually gets posted, and the
/// status line that tells the reader what the control thinks happened. Nor did they ever do the
/// second thing a person does, which is look back at the box they just filled. Every assertion here
/// is about one of those two.
/// </remarks>
[Collection(UsesTheZenithPeopleTenant.Name)]
public sealed class EmployeePickerSelectionBrowserTests
{
    private readonly ZenithPeopleTenantFixture _tenant;

    public EmployeePickerSelectionBrowserTests(ZenithPeopleTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Appointing a head by clicking the option with the mouse, and the card naming them after.
    /// </summary>
    [Fact]
    public async Task AHeadIsAppointedBySelectingWithTheMouse()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSetHeadAsync(page, "Site Team A");

        var picker = Picker(page);
        var chosen = await SelectWithMouseAsync(page, "zenith-fin-03");

        // The state the old control got wrong, asserted before the submit rather than inferred from
        // it afterwards.
        await AssertChosenAsync(page, chosen);

        // And the thing a person does next: look back at the box. This is where "No match" appeared.
        await picker.Locator(".workmate-picker-input").ClickAsync();
        await AssertNoFalseRefusalAsync(page);

        await page.ClickAsync("#unit-head-set button[type='submit']");

        await Assertions.Expect(DesignerTree.Node(page, "Site Team A").Locator(".designer-card-head"))
            .ToContainTextAsync(NameIn(chosen));
    }

    /// <summary>
    /// The same, from the keyboard: type, arrow down, Enter.
    /// </summary>
    [Fact]
    public async Task AHeadIsAppointedBySelectingWithTheKeyboard()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSetHeadAsync(page, "Site Team B");

        var chosen = await SelectWithKeyboardAsync(page, "zenith-hr-02");

        await AssertChosenAsync(page, chosen);

        await page.ClickAsync("#unit-head-set button[type='submit']");

        await Assertions.Expect(DesignerTree.Node(page, "Site Team B").Locator(".designer-card-head"))
            .ToContainTextAsync(NameIn(chosen));
    }

    /// <summary>
    /// A line manager chosen with the mouse is the line manager the record comes back with.
    /// </summary>
    /// <remarks>
    /// The other form the picker appears on, and the one where the posted value is a field on the
    /// record rather than a dated decision — so a selection that silently posted nothing would show
    /// up as "no line manager" and look like somebody had not filled it in.
    /// </remarks>
    [Fact]
    public async Task ALineManagerIsSetBySelectingWithTheMouse()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        var code = await AddEmployeeAsync(page, "picker-mouse-lm", "Rameez Soomro");
        var chosen = await SelectWithMouseAsync(page, "zenith-fin-04");

        await AssertChosenAsync(page, chosen);
        await page.ClickAsync("#employee-edit button[type='submit']");

        // Back on the editor, the picker shows who it holds — read from the record, not from what
        // was typed a moment ago.
        await page.GotoAsync($"/Admin/Employees/Index?search={code}");
        await page.ClickAsync($".employee-row[data-employee-code='{code}'] a[href*='Employees/Edit']");

        await Assertions.Expect(Picker(page).Locator(".workmate-picker-input"))
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("zenith-fin-04"));
    }

    /// <summary>
    /// A line manager chosen from the keyboard, likewise.
    /// </summary>
    [Fact]
    public async Task ALineManagerIsSetBySelectingWithTheKeyboard()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        var code = await AddEmployeeAsync(page, "picker-keys-lm", "Tahira Lodhi");
        var chosen = await SelectWithKeyboardAsync(page, "zenith-fin-05");

        await AssertChosenAsync(page, chosen);
        await page.ClickAsync("#employee-edit button[type='submit']");

        await page.GotoAsync($"/Admin/Employees/Index?search={code}");
        await page.ClickAsync($".employee-row[data-employee-code='{code}'] a[href*='Employees/Edit']");

        await Assertions.Expect(Picker(page).Locator(".workmate-picker-input"))
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("zenith-fin-05"));
    }

    /// <summary>
    /// Placement: chosen with the mouse, and the person is placed where the list said.
    /// </summary>
    /// <remarks>
    /// Placement has no employee picker on it — it chooses a <em>unit</em>, from a native
    /// <c>&lt;select&gt;</c> — so there is no combobox here to get wrong. It is covered anyway
    /// because it is the third form you named, and because "selected with the mouse, submitted, and
    /// the thing I chose is what was saved" is the property under test on all three regardless of
    /// which control implements it.
    /// </remarks>
    [Fact]
    public async Task APlacementIsMadeBySelectingWithTheMouse()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        var code = await AddEmployeeAsync(page, "picker-mouse-place", "Shazia Mehmood");

        await page.ClickAsync("#employee-placement-link");
        await page.ClickAsync("a[href*='Placement/Place']");

        await page.SelectOptionAsync(
            "#employee-place select[name='StructureId']",
            new SelectOptionValue { Label = "Zenith Engineering & Construction (zenith-org)" });

        // Finance, which sits under Corporate. Deliberately not a unit under Engineering Division:
        // another test asserts that division's exact headcount with sub-units included, and placing
        // somebody beneath it here would make that test's arithmetic depend on which of the two ran
        // first. A test that changes shared data owes the others a unit of its own.
        //
        // Chosen through the unit picker's own search, which is what replaced the flat <select>
        // this used to call SelectOption on: the list could only ever hold the first page of a
        // structure, and on a real one the rest was unreachable.
        await Pickers.ChooseAsync(page, ".workmate-unit-picker", "Finance");

        await page.ClickAsync("#employee-place button[type='submit']");

        await page.GotoAsync($"/Admin/Employees/Index?search={code}");

        await Assertions.Expect(
            page.Locator($".employee-row[data-employee-code='{code}'] .employee-unit"))
            .ToContainTextAsync("Finance");
    }

    /// <summary>
    /// Typing over a selection abandons it, rather than posting the person who was chosen before.
    /// </summary>
    /// <remarks>
    /// The dangerous half of the same defect. The box and the posted value must never disagree: a
    /// reader who types over "Adeel Bhatti" has stopped choosing Adeel Bhatti, and a control that
    /// kept his id while showing somebody else's name would save a person nobody on the screen had
    /// named.
    /// </remarks>
    [Fact]
    public async Task TypingOverASelectionAbandonsIt()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSetHeadAsync(page, "Mechanical");

        var picker = Picker(page);
        var chosen = await SelectWithMouseAsync(page, "zenith-hr-03");

        await AssertChosenAsync(page, chosen);

        await picker.Locator(".workmate-picker-input").FillAsync("somebody else entirely");

        await Assertions.Expect(picker.Locator("select[data-employee-picker]")).ToHaveValueAsync("");
    }

    /// <summary>
    /// Submitting text that names nobody is refused on the screen, not saved and not ignored.
    /// </summary>
    [Fact]
    public async Task SubmittingTypedTextWithNothingChosenIsRefused()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSetHeadAsync(page, "Electrical");

        var picker = Picker(page);
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync("Nobody With This Name");

        await page.ClickAsync("#unit-head-set button[type='submit']");

        // Still on the form — not submitted, not silently cleared — and the control says why.
        await Assertions.Expect(page.Locator("#unit-head-set")).ToBeVisibleAsync();
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(picker.Locator(".invalid-feedback")).ToContainTextAsync("Choose somebody");
    }

    /// <summary>
    /// The label is the label, not the name of a type.
    /// </summary>
    /// <remarks>
    /// <c>label="@T["Head of this unit"]"</c> hands the tag helper a <c>LocalizedHtmlString</c>, and
    /// a <c>string</c> property took it by calling <c>ToString()</c>, which that type does not
    /// override. The screen read
    /// <c>Microsoft.AspNetCore.Mvc.Localization.LocalizedHtmlString</c>.
    /// </remarks>
    [Fact]
    public async Task ThePickerLabelReadsAsALabel()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenSetHeadAsync(page, "Civil");

        await Assertions.Expect(Picker(page).Locator("label.form-label"))
            .ToHaveTextAsync("Head of this unit");
    }

    // ---- the two ways of selecting -----------------------------------------------------------

    private static ILocator Picker(IPage page) => page.Locator(".workmate-employee-picker");

    /// <summary>Types, then clicks an option with the mouse. Returns the option's text.</summary>
    private static async Task<string> SelectWithMouseAsync(IPage page, string code)
    {
        var picker = Picker(page);
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync(code);

        var option = picker.Locator(".workmate-picker-list [role='option']", new() { HasTextString = code }).First;

        await Assertions.Expect(option).ToBeVisibleAsync();

        var label = (await option.InnerTextAsync()).Trim();

        await option.ClickAsync();

        return label;
    }

    /// <summary>Types, then arrows down and presses Enter. Returns the option's text.</summary>
    private static async Task<string> SelectWithKeyboardAsync(IPage page, string code)
    {
        var picker = Picker(page);
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync(code);

        var option = picker.Locator(".workmate-picker-list [role='option']", new() { HasTextString = code }).First;

        await Assertions.Expect(option).ToBeVisibleAsync();

        var label = (await option.InnerTextAsync()).Trim();

        await input.PressAsync("ArrowDown");

        // aria-activedescendant is what tells a screen reader which row the keys are on. Without it
        // the list is navigable by sight only.
        await Assertions.Expect(input).ToHaveAttributeAsync(
            "aria-activedescendant", new System.Text.RegularExpressions.Regex(".+"));

        await input.PressAsync("Enter");

        return label;
    }

    /// <summary>
    /// The control holds the selection: a value to post, the name in the box, no list, no refusal.
    /// </summary>
    private static async Task AssertChosenAsync(IPage page, string label)
    {
        var picker = Picker(page);

        await Assertions.Expect(picker.Locator("select[data-employee-picker]")).Not.ToHaveValueAsync("");
        await Assertions.Expect(picker.Locator(".workmate-picker-input")).ToHaveValueAsync(label);
        await Assertions.Expect(picker.Locator(".workmate-picker-list [role='option']")).ToHaveCountAsync(0);

        await AssertNoFalseRefusalAsync(page);
    }

    /// <summary>
    /// The control is not telling a reader who has chosen somebody that it found nobody.
    /// </summary>
    private static async Task AssertNoFalseRefusalAsync(IPage page)
    {
        var picker = Picker(page);

        await Assertions.Expect(picker.Locator("select[data-employee-picker]")).Not.ToHaveValueAsync("");
        await Assertions.Expect(picker.Locator(".form-text[aria-live]")).Not.ToContainTextAsync("No match");
        await Assertions.Expect(picker.Locator(".workmate-picker-input")).Not.ToHaveAttributeAsync("aria-invalid", "true");
    }

    // ---- getting to the forms ----------------------------------------------------------------

    /// <summary>Adds an employee and leaves the browser on their editor, where the picker is.</summary>
    private static async Task<string> AddEmployeeAsync(IPage page, string code, string nameEn)
    {
        await page.GotoAsync("/Admin/Employees/Index");
        await page.ClickAsync("a[href*='Employees/Create']");

        await page.FillAsync("#employee-create input[name='Code']", code);
        await page.FillAsync("#employee-create input[name='NameEn']", nameEn);
        await page.FillAsync("#employee-create input[name='JoinDate']", "2024-01-01");
        await page.ClickAsync("#employee-create button[type='submit']");

        await Assertions.Expect(page.Locator("#employee-code")).ToContainTextAsync(code);

        return code;
    }

    private static async Task OpenSetHeadAsync(IPage page, string unitName)
    {
        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        await page.SelectOptionAsync("#structureId", new SelectOptionValue { Value = await ZenithIdAsync(page) });
        await Assertions.Expect(page.Locator("#designer-tree")).ToBeVisibleAsync();

        await DesignerTree.ExpandEverythingAsync(page);

        var card = DesignerTree.Node(page, unitName);

        await Assertions.Expect(card).ToBeVisibleAsync();

        await card.Locator(".designer-actions summary").First.ClickAsync();
        await card.Locator("[data-designer-action='sethead']").First.ClickAsync();

        await Assertions.Expect(page.Locator("#unit-head-set")).ToBeVisibleAsync();
    }

    /// <summary>The person's name out of an option's "Name (code) — Unit".</summary>
    private static string NameIn(string optionLabel) =>
        optionLabel.Split('(', 2)[0].Trim();

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
