using System.Text.RegularExpressions;

namespace WorkMate.Records.Services;

/// <summary>
/// The format rules for a form definition's code and for a field's name. Slice B3.
/// </summary>
/// <remarks>
/// <b>Two rules, and the second is much stricter than the first.</b> A form's code is a key a
/// person types into a recipe, so it follows <c>LookupCodes.IsValidListCode</c>'s shape. A
/// <em>field name</em> is not a key at all: it is a C# identifier in every Liquid template, every
/// GraphQL query and every report expression that ever touches the field, and Orchard puts it
/// straight into a form element's name attribute. A field called <c>visa-expiry</c> would be
/// addressed as <c>ContentItem.Content.X.visa-expiry</c>, which Liquid parses as a subtraction.
///
/// So the two are separate methods rather than one with a flag, and the field rule is the narrow
/// one the whole platform has to live with.
/// </remarks>
public static partial class FormCodes
{
    /// <summary>The longest a form code may be. Mirrored by the migration's column width.</summary>
    public const int MaxCodeLength = 64;

    /// <summary>The longest a field name may be. Orchard puts it in a column name on no table, but
    /// it does put it in a JSON property on every item, and an unbounded one is a column nobody
    /// can index later.</summary>
    public const int MaxFieldNameLength = 64;

    /// <summary>
    /// A form code: a leading letter, then letters, digits, hyphens and underscores.
    /// </summary>
    /// <remarks>
    /// The same shape a lookup list code has, for the same reasons: it is a route value on the
    /// designer screens B4 builds, and a JSON property value in B6's recipe step.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern { get; }

    /// <summary>
    /// A field name: a leading letter, then letters and digits only.
    /// </summary>
    /// <remarks>
    /// <b>No hyphens, no underscores, no dots</b> — a C# identifier, because that is what it
    /// becomes. Orchard addresses a field as <c>Content.&lt;Part&gt;.&lt;Field&gt;</c> in Liquid
    /// and as a property in GraphQL; a hyphen is a minus sign in the first and illegal in the
    /// second, and the failure arrives months later in somebody's report rather than here.
    ///
    /// Underscores are excluded as well, which is stricter than it has to be and is deliberate:
    /// the six shipped sections all use <c>PascalCase</c>, and a palette where half the names read
    /// one way and half the other is a palette nobody can guess at.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern { get; }

    /// <summary>Whether <paramref name="code"/> is a well-formed form code.</summary>
    public static bool IsValidCode(string? code) => code is not null && CodePattern.IsMatch(code);

    /// <summary>Whether <paramref name="name"/> is a well-formed field name.</summary>
    public static bool IsValidFieldName(string? name) => name is not null && FieldNamePattern.IsMatch(name);

    /// <summary>The code as it is stored and compared: trimmed, and nothing else.</summary>
    /// <remarks>
    /// Case preserved on the way in and ignored on the way out, exactly as a lookup list code and
    /// an employee code are. A recipe shouting <c>JOINING</c> finds the definition coded
    /// <c>joining</c> rather than creating a second one beside it.
    /// </remarks>
    public static string Normalise(string? code) => code?.Trim() ?? string.Empty;

    /// <summary>Whether two codes name the same thing.</summary>
    public static bool Same(string? left, string? right) =>
        string.Equals(Normalise(left), Normalise(right), StringComparison.OrdinalIgnoreCase);
}
