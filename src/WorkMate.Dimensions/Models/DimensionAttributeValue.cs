namespace WorkMate.Dimensions.Models;

/// <summary>
/// One custom attribute's value as it arrives from a form or an import, before it is turned into
/// the content field the dimension type declared.
/// </summary>
/// <param name="Name">
/// The attribute's technical name, matching a <see cref="DimensionAttributeDefinition.Name"/> on
/// the record's dimension type.
/// </param>
/// <param name="Value">
/// The value as text: the text itself, the English half of a bilingual attribute, a number, an
/// ISO-8601 date, or "true"/"false". Null or blank means the attribute was left empty.
/// </param>
/// <param name="ValueAr">The Arabic half, for a bilingual attribute only.</param>
/// <remarks>
/// Text rather than a typed union because that is how a value actually reaches the service: every
/// one of these comes off an HTML form or a recipe's JSON, both of which are text. Converting once,
/// in the service, against the schema the dimension type declares, means there is a single place
/// that decides what "not a number" does — and it can answer with a
/// <see cref="Services.DimensionError"/> naming the attribute, like every other violation, rather
/// than with a binding failure the caller has to translate.
///
/// Dates are parsed with <see cref="Internal.IsoDate"/> for the same reason the designer's "as at"
/// is: the wire format is ISO-8601 whatever culture the person filling the form is in.
/// </remarks>
public sealed record DimensionAttributeValue(string Name, string? Value, string? ValueAr = null)
{
    /// <summary>
    /// Whether this value counts as not supplied, for a required attribute.
    /// </summary>
    /// <remarks>
    /// Keyed on the English half alone. <see cref="Value"/> is that half for a bilingual attribute
    /// and the only half for every other kind, and ADR-0003's addendum makes English the required
    /// language — so a required bilingual attribute filled in Arabic only is not filled.
    ///
    /// It used to be <c>Value is empty AND ValueAr is empty</c>, which let the Arabic half satisfy
    /// a requirement the English half is the subject of: the exact inverse of the rule everywhere
    /// else on the platform, and more visible now that Arabic may legitimately be the empty one.
    /// </remarks>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}
