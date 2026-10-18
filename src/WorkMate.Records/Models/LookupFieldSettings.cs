namespace WorkMate.Records.Models;

/// <summary>
/// Binds a content field to a managed lookup list.
/// </summary>
/// <remarks>
/// <b>Binding changes the editor, never the stored shape.</b> A bound field stays the field type it
/// already was — Orchard's <c>TextField</c>, holding the same plain string it held as free text.
/// What changes is which editor renders it: <c>ContentPartFieldSettings.Editor</c> is set to
/// <see cref="LookupFieldEditor.Name"/>, and these settings say which list to offer.
///
/// That is what makes binding free. Turning an existing free-text field into a managed choice needs
/// no type change — which <c>ILookupListService</c> and the form designer both refuse anyway — no
/// data migration, no downtime, and it is reversible by clearing two settings. A value already
/// stored that matches no entry is preserved and surfaced for reconciliation rather than rewritten,
/// so "no data lost" is a property of the mechanism rather than of a script somebody has to get
/// right once.
/// </remarks>
public sealed class LookupFieldSettings
{
    /// <summary>
    /// The code of the list this field offers, or empty when the field is not bound.
    /// </summary>
    /// <remarks>
    /// By code rather than by the list's generated id, because a binding travels: it is part of the
    /// content definition a form definition regenerates in slice B3, and an id means nothing in the
    /// tenant a recipe lands in. The same rule every other reference on this platform follows.
    /// </remarks>
    public string ListCode { get; set; } = string.Empty;
}

/// <summary>The editor name that turns a text field into a lookup.</summary>
/// <remarks>
/// A constant rather than a literal because it appears in three places that must agree: the
/// migration that binds a field, the shape alternate the view file is named for, and any code
/// asking whether a field is bound. A typo in one of the three produces a field that renders as
/// plain text with a list attached to it, which looks like a configuration mistake rather than a
/// bug.
/// </remarks>
public static class LookupFieldEditor
{
    /// <summary>
    /// The value of <c>ContentPartFieldSettings.Editor</c> on a bound field.
    /// </summary>
    /// <remarks>
    /// Orchard turns this into the shape alternate <c>TextField_Edit__WorkMateLookup</c>, which
    /// resolves to <c>Views/TextField-WorkMateLookup.Edit.cshtml</c> in this module. That is
    /// Orchard's own mechanism for an alternate editor — the same one its <c>PredefinedList</c>
    /// text editor uses — and taking it means this module registers no field driver at all and
    /// cannot collide with the one <c>OrchardCore.ContentFields</c> already registers for
    /// <c>TextField</c>.
    /// </remarks>
    public const string Name = "WorkMateLookup";
}

/// <summary>
/// The editor name that retires a field: kept, readable, and no longer offered. D22, slice F6c.
/// </summary>
/// <remarks>
/// <b>An editor, not a deletion, and not a setting on the part.</b> D22's whole shape is that the
/// field stays — a tenant's history is exactly the values it already typed — while the thing that
/// answers the question moves somewhere else. Orchard's editor alternate is the sanctioned way to
/// say that: <c>ContentPartFieldSettings.Editor</c> becomes this name, Orchard resolves the shape
/// alternate <c>TextField_Edit__WorkMateRetired</c>, and this module supplies the view. No field
/// driver is registered and nothing collides with the one <c>OrchardCore.ContentFields</c> already
/// has, exactly as <see cref="LookupFieldEditor"/> records.
///
/// <b>The view must round-trip the value in a hidden input.</b> Orchard's field drivers bind their
/// editor's model and write the result back, so an editor that rendered nothing at all would post
/// nothing and the driver would store the empty string — retiring the field by deleting its
/// contents, which is the one outcome D22 forbids.
/// </remarks>
public static class RetiredFieldEditor
{
    /// <summary>The value of <c>ContentPartFieldSettings.Editor</c> on a retired field.</summary>
    public const string Name = "WorkMateRetired";
}
