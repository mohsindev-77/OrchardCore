using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The unit picker, driven as a person drives it, on the tenant that makes it necessary.
/// </summary>
/// <remarks>
/// <b>Crescent is the point.</b> It has two departments called "Credit" and two called
/// "Operations", on one structure, under different branches. A flat list of names offers a reader
/// two identical rows and no way to choose correctly — and choosing wrong puts somebody in the
/// wrong branch of the bank, which nothing downstream would flag because both answers are valid
/// units. So what is asserted here is not that the search matches but that the two options read
/// differently.
///
/// Everything is clicked and typed. The hidden <c>&lt;select&gt;</c> is never set directly: it is
/// the form control and the combobox drives it, so setting it would skip the one thing worth
/// testing — that what the reader sees and what the form would post stay the same answer.
/// </remarks>
[Collection(UsesTheDemoCompanyTenant.Name)]
public sealed class UnitPickerBrowserTests
{
    private readonly DemoCompanyTenantFixture _tenant;

    public UnitPickerBrowserTests(DemoCompanyTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// Searching "Credit" offers both of Crescent's, told apart by their paths.
    /// </summary>
    [Fact]
    public async Task SearchingOffersBothCreditsAndTheirPathsTellThemApart()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenPlaceScreenFromACardAsync(page);

        var options = await OptionsForAsync(page, "Credit");

        options.Should().HaveCountGreaterThanOrEqualTo(2, "Crescent has a Credit under two branches");

        options.Should().Contain(text => text.Contains("Lahore Main Branch", StringComparison.Ordinal));
        options.Should().Contain(text => text.Contains("Karachi Main Branch", StringComparison.Ordinal));

        options.Should().OnlyHaveUniqueItems(
            "two options that read identically are two options a reader has to guess between");
    }

    /// <summary>The same for "Operations", which Crescent also has twice.</summary>
    [Fact]
    public async Task SearchingOffersBothOperationsDepartmentsDistinguishably()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenPlaceScreenFromACardAsync(page);

        var options = await OptionsForAsync(page, "Operations");

        options.Should().HaveCountGreaterThanOrEqualTo(2);
        options.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The "Within" filter narrows the search to a division's subtree, and choosing survives it.
    /// </summary>
    /// <remarks>
    /// Asserted from both sides. "Within Head Office" must drop the branch departments <em>and</em>
    /// keep Head Office's own, because a filter that returned nothing would pass the first
    /// assertion on its own.
    ///
    /// <b>On the record-create screen since slice F6b, not on an employee one.</b> Item 8 of that
    /// slice took the nested Within off every employee form: it narrows a <em>list of units</em>,
    /// which earns its place on a screen about units and is a control nobody uses on a form about
    /// one person — and on a position-based axis there is no unit list left for it to narrow.
    /// "Place under" is the question it was always for: which unit may contain the record being
    /// created. <see cref="TheWithinFilterIsOffOnEmployeeScreensAndOnOnTheUnitsAdminScreen"/> holds
    /// both ends of that decision.
    /// </remarks>
    [Fact]
    public async Task TheWithinFilterNarrowsTheSearchToTheChosenDivision()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenCreateRecordScreenAsync(page, "crescent-department");

        var picker = page.Locator(".workmate-unit-picker");
        var within = picker.Locator("[data-unit-picker-within]");

        // Rendered hidden by the server and unhidden by the script, because without the script it
        // would be a control that visibly does nothing.
        await Assertions.Expect(within).ToBeVisibleAsync();

        // Unnarrowed, the control offers every unit that may CONTAIN a department — Crescent's
        // divisions, which are Head Office and the two main branches.
        var everywhere = await OptionsForAsync(page, "Office");

        everywhere.Should().Contain(
            text => text.Contains("Head Office", StringComparison.Ordinal),
            "a department may be created under Head Office");

        await within.SelectOptionAsync(new SelectOptionValue
        {
            Label = await LabelContainingAsync(page, "Head Office"),
        });

        // Narrowed to Head Office's subtree, the branches drop out and Head Office itself stays.
        // Both halves, because a filter that returned nothing would pass the first on its own.
        var narrowed = await OptionsForAsync(page, "Branch");

        narrowed.Should().NotContain(
            text => text.Contains("Lahore Main Branch", StringComparison.Ordinal),
            "a branch is not inside Head Office");

        narrowed = await OptionsForAsync(page, "Office");

        narrowed.Should().Contain(
            text => text.Contains("Head Office", StringComparison.Ordinal),
            "and Head Office itself still is — the filter narrows rather than empties");

        // Choosing one of them puts its whole path in the box and leaves the control resolved.
        var option = page.Locator(".workmate-unit-picker .workmate-picker-list [role='option']").First;

        await option.ClickAsync();

        await Assertions.Expect(page.Locator(".workmate-unit-picker .workmate-picker-input"))
            .Not.ToHaveAttributeAsync("aria-invalid", "true");

        await Assertions.Expect(page.Locator(".workmate-unit-picker select[data-unit-picker]"))
            .Not.ToHaveValueAsync("");
    }

    /// <summary>
    /// Text that names no unit is refused on the field, exactly as the employee picker refuses it.
    /// </summary>
    /// <remarks>
    /// The dangerous case is the opposite one: a control that kept the previously chosen id while
    /// showing text the reader had typed over would post a unit nobody on the screen had named.
    /// Both halves are asserted — the box is marked, and the form does not submit.
    /// </remarks>
    [Fact]
    public async Task TypedTextThatNamesNoUnitIsRefusedOnTheField()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenPlaceScreenFromACardAsync(page);

        var input = page.Locator(".workmate-unit-picker .workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync("a department that does not exist");

        await page.ClickAsync("#place-submit");

        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");

        // Still on the form: it refused to submit rather than posting a unit nobody named.
        await Assertions.Expect(page.Locator("#place-heading")).ToBeVisibleAsync();
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>
    /// Opens the Place screen from a Crescent card's ⋯ menu, which is the way in the brief names.
    /// </summary>
    private static async Task OpenPlaceScreenFromACardAsync(IPage page)
    {
        await page.GotoAsync("/Admin/Dimensions/Designer/Index?view=Chart");

        await page.SelectOptionAsync(
            "#structureId",
            new SelectOptionValue { Value = await StructureIdAsync(page, DemoCompanyTenantFixture.CrescentStructureCode) });

        await Assertions.Expect(page.Locator("#designer-tree")).ToBeVisibleAsync();

        // A branch, which is one of the two types Crescent's structure says may hold employees.
        // Deliberately not a division: since the demo recipes declare employeeAttachableTypeCodes,
        // a container's card does not offer "Place an employee here…" at all — that is the whole
        // point of declaring them, and DesignerPlaceEmployeeActionTenantTests is where it is
        // asserted. It is also at depth three, so its card is one the browser built.
        await DesignerTree.ExpandEverythingAsync(page);

        var branch = DesignerTree.Node(page, "Lahore Main Branch");

        await Assertions.Expect(branch).ToBeVisibleAsync();

        await branch.Locator(".designer-actions summary").First.ClickAsync();
        await branch.Locator("[data-designer-action='placeemployee']").First.ClickAsync();

        await Assertions.Expect(page.Locator(".workmate-unit-picker .workmate-picker-input")).ToBeVisibleAsync();
    }

    /// <summary>Types a search and returns what the list offers, as a person would read it.</summary>
    private static async Task<IReadOnlyList<string>> OptionsForAsync(IPage page, string search)
    {
        var picker = page.Locator(".workmate-unit-picker");
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync(search);

        var options = picker.Locator(".workmate-picker-list [role='option']");

        await Assertions.Expect(options.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        return await options.AllTextContentsAsync();
    }

    /// <summary>The full label of the "Within" option whose text contains a name.</summary>
    private static async Task<string> LabelContainingAsync(IPage page, string name) =>
        await page.EvaluateAsync<string>(
            """
            name => {
                const option = [...document.querySelectorAll('[data-unit-picker-within] option')]
                    .find(option => option.textContent.includes(name));

                return option ? option.textContent : '';
            }
            """,
            name);

    /// <summary>
    /// Opens the Add-record form for a dimension type, which is where "Place under" lives.
    /// </summary>
    /// <remarks>
    /// Reached through the records list rather than by hand-building the query string, because the
    /// list is how a person gets there and the form needs the type and the structure the list
    /// settled. Crescent's departments are the right subject: it has two Credits and two
    /// Operations under different branches, which is the whole reason a Within filter exists.
    /// </remarks>
    private async Task OpenCreateRecordScreenAsync(IPage page, string dimensionTypeCode)
    {
        var typeId = string.Empty;

        await _tenant.InTenantAsync(async services =>
            typeId = (await services
                .GetRequiredService<WorkMate.Dimensions.Services.IDimensionTypeService>()
                .GetByCodeAsync(dimensionTypeCode))!.DimensionTypeId);

        await page.GotoAsync($"/Admin/Dimensions/Records/Index?dimensionTypeId={typeId}");
        await page.ClickAsync("a[href*='Records/Create']");

        await Assertions.Expect(page.Locator(".workmate-unit-picker")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Slice F6b item 8, both ends: Within is gone from the employee screens and still on the
    /// screens about units.
    /// </summary>
    /// <remarks>
    /// <b>Two different controls share the word, and the test has to know which is which.</b> The
    /// records list's filter <em>is</em> a unit picker bound to <c>Within</c> — the whole control,
    /// which is why it carries <c>allow-within="false"</c>: you do not narrow the narrower. The
    /// marker <c>data-unit-picker-within</c> is the <em>nested</em> scope select a picker renders
    /// inside itself, and that is the one the employee forms dropped.
    ///
    /// Without this, "removed from employee screens" and "removed" are indistinguishable the next
    /// time somebody tidies up — which is exactly how the control nearly disappeared entirely.
    /// </remarks>
    [Fact]
    public async Task TheWithinFilterIsOffOnEmployeeScreensAndOnOnTheUnitsAdminScreen()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        // The employee screen: a unit picker, and no nested Within inside it.
        await OpenPlaceScreenFromACardAsync(page);

        await Assertions.Expect(page.Locator(".workmate-unit-picker")).ToBeVisibleAsync();

        await Assertions.Expect(page.Locator("[data-unit-picker-within]")).ToHaveCountAsync(0);

        // The units admin screen: its Within filter is a picker of its own, and it is still there.
        var typeId = string.Empty;

        await _tenant.InTenantAsync(async services =>
            typeId = (await services
                .GetRequiredService<WorkMate.Dimensions.Services.IDimensionTypeService>()
                .GetByCodeAsync("crescent-department"))!.DimensionTypeId);

        await page.GotoAsync($"/Admin/Dimensions/Records/Index?dimensionTypeId={typeId}");

        await Assertions.Expect(page.Locator("select[data-unit-picker]#Within")).ToBeAttachedAsync();

        // And the Add form keeps the nested one, which is what narrows a list of possible parents.
        await OpenCreateRecordScreenAsync(page, "crescent-department");

        await Assertions.Expect(
            page.Locator(".workmate-unit-picker [data-unit-picker-within]")).ToBeVisibleAsync();
    }

    private static async Task<string> StructureIdAsync(IPage page, string code) =>
        await page.EvaluateAsync<string>(
            """
            code => {
                const option = [...document.querySelectorAll('#structureId option')]
                    .find(option => option.textContent.includes(code));

                return option ? option.value : '';
            }
            """,
            code);
}
