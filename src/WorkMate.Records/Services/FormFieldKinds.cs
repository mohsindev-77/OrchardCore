using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// What each designer field type is backed by in Orchard. Design note §3.2, slice B3.
/// </summary>
/// <remarks>
/// <b>One table, in one place, because three things have to agree about it.</b> The compiler
/// writes the content-definition field type; B4's palette shows the choices; and B6's recipe step
/// reads them back. Three copies of this mapping would be three chances for a definition to
/// compile into a different field from the one it names.
///
/// <b>Nothing here invents a field type.</b> Every one is Orchard's own or
/// <c>BilingualTextField</c>, which <c>WorkMate.Platform</c> already ships under ADR-0003 — the
/// same rule the dimension engine's <c>DimensionAttributeKinds</c> follows, and the reason D22's
/// retirement was a settings change rather than a migration: a <see cref="FormFieldType.Choice"/>
/// is a <c>TextField</c> with a different editor, so turning free text into a managed choice costs
/// nothing and is reversible.
/// </remarks>
internal static class FormFieldKinds
{
    /// <summary>The Orchard content field type that backs a designer type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A designer type with no backing, which is a mistake in this module's own source rather
    /// than in anybody's input — the palette is closed and this switch is its other half.
    /// </exception>
    public static string FieldTypeNameFor(FormFieldType type) => type switch
    {
        FormFieldType.Text => "TextField",

        // The same field. What differs is the editor, which is the whole of §3.3's argument:
        // one line and several lines are a presentation difference, not a storage one, so
        // changing a field from one to the other must never be a type change.
        FormFieldType.MultilineText => "TextField",

        FormFieldType.BilingualText => "BilingualTextField",
        FormFieldType.Number => "NumericField",
        FormFieldType.Date => "DateField",
        FormFieldType.YesNo => "BooleanField",

        // A code in a TextField, rendered by the lookup editor. Not a new field type — §3.3.
        FormFieldType.Choice => "TextField",

        FormFieldType.EmployeePicker => "ContentPickerField",
        FormFieldType.File => "MediaField",

        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "The designer palette is closed and this type is not on it."),
    };

    /// <summary>
    /// The editor name a designer type needs, or empty for the field type's default.
    /// </summary>
    /// <remarks>
    /// Orchard resolves an editor name into the shape alternate
    /// <c>&lt;FieldType&gt;_Edit__&lt;Editor&gt;</c>, which is how both of the non-default editors
    /// here are supplied without this module registering a field driver — see
    /// <c>LookupFieldEditor</c> for the full account of why registering one would render the
    /// field twice.
    /// </remarks>
    public static string EditorNameFor(FormFieldType type) => type switch
    {
        // Orchard's own multi-line editor for TextField. TextFieldSettings.Type is a
        // FieldBehaviorType and this is the editor that honours it.
        FormFieldType.MultilineText => "TextArea",

        FormFieldType.Choice => LookupFieldEditor.Name,

        _ => string.Empty,
    };

    /// <summary>Whether this type stores its value through a managed list. Only one does.</summary>
    public static bool NeedsAList(FormFieldType type) => type == FormFieldType.Choice;
}
