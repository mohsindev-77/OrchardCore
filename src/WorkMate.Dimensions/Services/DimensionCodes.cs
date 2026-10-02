using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The rules a dimension type code, a structure code and an attribute name have to obey, and how
/// a content type name is derived from a code.
/// </summary>
/// <remarks>
/// This is separate from the services because the derivation is the thing most likely to be
/// wrong in a way nobody notices: a code that produces an invalid or colliding content type name
/// fails when the content definition is written, by which time the dimension type document has
/// already been saved. Keeping the rules here lets them be checked before anything is written,
/// and lets a test enumerate the awkward cases without standing up a tenant.
/// </remarks>
public static partial class DimensionCodes
{
    /// <summary>
    /// A code starts with a letter and continues with letters, digits, hyphens and underscores.
    /// Fifty characters is generous for a code and short enough that the derived content type
    /// name stays within what every provider allows for a table name.
    /// </summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern { get; }

    /// <summary>
    /// An attribute name is an identifier with no separators, because it becomes a field name in
    /// a content definition and Orchard addresses fields by name in Liquid and GraphQL, where a
    /// hyphen is not addressable.
    /// </summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeNamePattern { get; }

    public static bool IsValidCode(string? code) => code is not null && CodePattern.IsMatch(code);

    public static bool IsValidAttributeName(string? name) =>
        name is not null && AttributeNamePattern.IsMatch(name);

    /// <summary>
    /// The content type name for a code: separators removed, each segment's first letter upper
    /// cased, everything else left alone.
    /// </summary>
    /// <remarks>
    /// The rest of each segment is left as the customer typed it rather than lower cased, so that
    /// a code like <c>HR-BU</c> becomes <c>HRBU</c> and not <c>HrBu</c>. Customers use
    /// abbreviations as codes and mangling their capitalisation makes the generated content type
    /// unrecognisable to the person who created it.
    ///
    /// Two different codes can still derive the same name — <c>cost-centre</c> and
    /// <c>costcentre</c> both give <c>CostCentre</c> — so the derived name is checked for
    /// collision against the tenant's existing content types before anything is written, and
    /// stored on the document afterwards.
    /// </remarks>
    public static string ToContentTypeName(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var name = new StringBuilder(code.Length);
        var atSegmentStart = true;

        foreach (var character in code)
        {
            if (character is '-' or '_')
            {
                atSegmentStart = true;
                continue;
            }

            name.Append(atSegmentStart
                ? char.ToUpper(character, CultureInfo.InvariantCulture)
                : character);

            atSegmentStart = false;
        }

        return name.ToString();
    }
}
