using WorkMate.Core;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>One choice a bound field offers, as a control renders it.</summary>
/// <param name="Value">
/// What gets stored if this is chosen. Always an entry code, or — for an unmatched option — the
/// value the record already holds.
/// </param>
/// <param name="Label">What the reader sees, in their language.</param>
/// <param name="IsRetired">
/// A retired entry that this record happens to hold. Offered so the selection round-trips, marked
/// so a reader knows it is not a choice they should make again.
/// </param>
/// <param name="IsUnmatched">
/// A value the record holds that is in no entry of the list. Never offered to anybody else; it
/// exists so that opening and saving a record cannot silently erase it.
/// </param>
public sealed record LookupOption(string Value, string Label, bool IsRetired = false, bool IsUnmatched = false);

/// <summary>
/// What a control should render for one bound field on one record.
/// </summary>
/// <param name="IsBound">
/// False when the field should fall back to a free-text box: the list does not exist, or holds
/// nothing to offer. See <see cref="ILookupChoiceProvider"/> for why that is a fallback rather than
/// an error.
/// </param>
/// <param name="Options">Every option to render, in the order to render them.</param>
/// <param name="StoredValueIsUnmatched">
/// Whether the value the record holds matches no entry. What the screen uses to say so, and what
/// the reconciliation screen exists to clear up.
/// </param>
public sealed record LookupChoices(
    bool IsBound,
    IReadOnlyList<LookupOption> Options,
    bool StoredValueIsUnmatched)
{
    /// <summary>A field that should render as free text.</summary>
    public static readonly LookupChoices Unbound = new(false, [], false);
}

/// <summary>
/// Turns a list code and the value a record holds into the choices a control renders.
/// </summary>
/// <remarks>
/// <b>One implementation, three callers.</b> The <c>&lt;workmate-lookup&gt;</c> tag helper on this
/// module's own admin forms, the alternate editor view for a bound content field, and the
/// reconciliation screen's "map to" picker all ask the same question, and two answers to it would
/// disagree on exactly the case the mechanism exists for — the employee picker's own history is the
/// warning: the same person read "Mechanical" on the picker and "Not placed" on the list, because
/// two pieces of code answered "where do they sit" separately.
///
/// <b>Falling back to free text is deliberate and is what makes binding safe to ship.</b> A tenant
/// that has not loaded the standard lists — every tenant that existed before this slice — has the
/// binding on its fields and nothing in the list behind it. Rendering an empty dropdown there would
/// take away the ability to type a value, which is a regression dressed as a feature. So a missing
/// or empty list renders exactly what the tenant sees today, and the screens change the moment the
/// lists are loaded and not before.
/// </remarks>
public interface ILookupChoiceProvider
{
    /// <summary>
    /// The choices for <paramref name="listCode"/>, with <paramref name="storedValue"/> kept
    /// whatever it is.
    /// </summary>
    Task<LookupChoices> ForAsync(
        string? listCode,
        string? storedValue,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class LookupChoiceProvider : ILookupChoiceProvider
{
    private readonly ILookupListLookup _lists;

    public LookupChoiceProvider(ILookupListLookup lists) => _lists = lists;

    public async Task<LookupChoices> ForAsync(
        string? listCode,
        string? storedValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listCode))
        {
            return LookupChoices.Unbound;
        }

        var list = await _lists.GetByCodeAsync(listCode, cancellationToken);

        if (list is null || list.Items.Count == 0)
        {
            return LookupChoices.Unbound;
        }

        var stored = LookupCodes.Normalise(storedValue);
        var active = list.Items.Where(item => item.IsActive).ToList();

        var held = stored.Length == 0
            ? null
            : list.Items.FirstOrDefault(item => LookupCodes.Same(item.Code, stored));

        // A list whose every entry is retired and that this record does not hold one of has nothing
        // to offer. Free text rather than an empty dropdown, for the reason on the interface.
        if (active.Count == 0 && held is null)
        {
            return LookupChoices.Unbound;
        }

        var options = new List<LookupOption>(active.Count + 1);

        options.AddRange(active.Select(Option));

        // The retired entry this record holds, kept so that opening and saving the record does not
        // quietly move it to something else. Last, because it is not a choice to make again.
        if (held is not null && !held.IsActive)
        {
            options.Add(Option(held) with { IsRetired = true });
        }

        var unmatched = stored.Length > 0 && held is null;

        if (unmatched)
        {
            // The value itself as its own option, carrying no name because the list has none for
            // it. This is the whole of "never rewrite an existing value": the control cannot
            // represent it any other way, and leaving it out would make the first save after
            // binding erase it.
            options.Add(new LookupOption(stored, stored, IsUnmatched: true));
        }

        return new LookupChoices(IsBound: true, options, unmatched);
    }

    private static LookupOption Option(LookupListItem item) =>
        new(item.Code, Label(item));

    /// <summary>
    /// The entry's name in the reader's language, with the code after it.
    /// </summary>
    /// <remarks>
    /// The code is shown as well as the name because the code is what the record stores and what
    /// every report, export and integration names — a reader reconciling a payroll file against
    /// this screen needs to see the thing the file contains. The name comes first because that is
    /// what the reader is choosing by.
    ///
    /// Arabic is optional since ADR-0003's addendum, so the name falls back to the other language
    /// rather than rendering blank: an Arabic reader looking at an untranslated entry sees the
    /// English one, which they can act on.
    /// </remarks>
    private static string Label(LookupListItem item)
    {
        var name = BilingualText.Display(item.Name.En, item.Name.Ar);

        return string.IsNullOrWhiteSpace(name) ? item.Code : $"{name} ({item.Code})";
    }
}
