using Microsoft.Playwright;

namespace WorkMate.Browser.Tests;

/// <summary>
/// Driving a WorkMate combobox — the employee picker, the unit picker — the way a person does.
/// </summary>
/// <remarks>
/// <b>Never by setting the hidden <c>&lt;select&gt;</c>.</b> The select is still the form control
/// and the combobox drives it, so a test that set it directly would skip the one thing worth
/// testing: that what the reader sees and what the form posts stay the same answer. That is the
/// defect the picker was rebuilt around, and it is invisible to a test that bypasses the control.
///
/// One copy, shared, because two of these had already been written by the time the unit picker
/// arrived — <c>DesignerTree</c> records what two copies of a browser helper cost the last time.
/// </remarks>
internal static class Pickers
{
    /// <summary>
    /// Types <paramref name="search"/> into a picker and chooses the option that matches it.
    /// </summary>
    /// <param name="pickerSelector">
    /// The wrapper — <c>.workmate-unit-picker</c>, <c>.workmate-employee-picker</c>, or a row's own
    /// one on a form that has several.
    /// </param>
    public static async Task ChooseAsync(IPage page, string pickerSelector, string search)
    {
        ArgumentNullException.ThrowIfNull(page);

        var picker = page.Locator(pickerSelector);
        var input = picker.Locator(".workmate-picker-input");

        await input.ClickAsync();
        await input.FillAsync(search);

        var option = picker.Locator(".workmate-picker-list [role='option']", new()
        {
            HasTextString = search,
        }).First;

        // Generous, because this waits on a round trip to the server after a debounce rather than
        // on anything already in the page.
        await Assertions.Expect(option).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await option.ClickAsync();
    }
}
