using FluentAssertions;
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
    /// </remarks>
    [Fact]
    public async Task TheWithinFilterNarrowsTheSearchToTheChosenDivision()
    {
        await using var context = await _tenant.SignedInContextAsync();
        var page = await context.NewPageAsync();

        await OpenPlaceScreenFromACardAsync(page);

        var picker = page.Locator(".workmate-unit-picker");
        var within = picker.Locator("[data-unit-picker-within]");

        // Rendered hidden by the server and unhidden by the script, because without the script it
        // would be a control that visibly does nothing.
        await Assertions.Expect(within).ToBeVisibleAsync();

        await within.SelectOptionAsync(new SelectOptionValue
        {
            Label = await LabelContainingAsync(page, "Head Office"),
        });

        var narrowed = await OptionsForAsync(page, "Credit");

        narrowed.Should().NotContain(
            text => text.Contains("Lahore Main Branch", StringComparison.Ordinal),
            "a branch department is not inside Head Office");

        narrowed.Should().Contain(
            text => text.Contains("Credit Risk", StringComparison.Ordinal),
            "and Head Office's own Credit Risk department still is — the filter narrows rather "
            + "than empties");

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
