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

    /// <summary>
    /// Chooses a position on a form that redraws when one is chosen, and waits for the redraw.
    /// </summary>
    /// <remarks>
    /// <b>Why the position picker needs its own helper and the others do not.</b> Since D31 the
    /// employee forms carry <c>refresh-on-change</c> on this control: the organisation chain, the
    /// reporting line and the line manager are all rendered by the server from the chosen position,
    /// so following the choice means a round trip. Choosing therefore navigates, and whatever the
    /// test does next would otherwise race the redraw it just caused — which is how the same click
    /// passes alone and fails in a suite.
    ///
    /// One place to wait, rather than the wait being remembered at eight call sites.
    /// </remarks>
    internal static async Task ChoosePositionAsync(IPage page, string search)
    {
        ArgumentNullException.ThrowIfNull(page);

        await ChooseAsync(page, ".workmate-position-picker", search);

        // The redraw is a form post, so the document is replaced. NetworkIdle rather than Load:
        // the picker's own script re-asks for its options as the new page settles, and a test that
        // started typing into it mid-flight would be driving a control about to be rebuilt.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
