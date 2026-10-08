using System.Text.RegularExpressions;

namespace WorkMate.Records.Services;

/// <summary>
/// What an employee code may look like, and how two of them are compared.
/// </summary>
/// <remarks>
/// Separate from the service for the same reason <c>DimensionCodes</c> is: the rule has to be
/// checkable before anything is written, and a test should be able to enumerate the awkward cases
/// without standing up a tenant.
/// </remarks>
public static partial class EmployeeCodes
{
    /// <summary>
    /// One to thirty-two characters of letters, digits, hyphen, underscore, dot or slash.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately looser than <c>DimensionCodes.IsValidCode</c>, which requires a leading
    /// letter.</b> That rule exists because a dimension code is turned into a content type name,
    /// and a type name cannot start with a digit. An employee code is turned into nothing: it is
    /// stored, compared and printed. Carrying the stricter rule across would refuse <c>00412</c>
    /// and <c>1001</c>, which is what most HR departments and every payroll file already use, and
    /// would force a migration on every customer bringing their existing numbers with them.
    ///
    /// The dot and the slash are here for the same reason — <c>HR/0042</c> and <c>12.345</c> are
    /// both real formats in the field. Whitespace is not, because a code with a space in it is a
    /// code half of whose uses will trim it and half will not.
    ///
    /// Thirty-two rather than fifty: nothing derives a name from it, so the only bound that matters
    /// is the indexed column, and a code longer than this is a description.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern { get; }

    /// <summary>The longest code the index column holds. Mirrored by the migration.</summary>
    public const int MaxLength = 32;

    /// <summary>Whether <paramref name="code"/> is a well-formed employee code.</summary>
    public static bool IsValid(string? code) => code is not null && CodePattern.IsMatch(code);

    /// <summary>
    /// The code as it is stored and compared: trimmed, and nothing else.
    /// </summary>
    /// <remarks>
    /// Case is <em>preserved</em> on the way in and ignored on the way out. A customer whose codes
    /// read <c>EMP-0042</c> sees them that way, and <c>emp-0042</c> is still the same employee — so
    /// an import cannot create a second record for one person by shouting.
    /// </remarks>
    public static string Normalise(string? code) => code?.Trim() ?? string.Empty;

    /// <summary>How two employee codes are compared, everywhere.</summary>
    /// <remarks>
    /// Ordinal and case-insensitive: these are identifiers, not words, so no culture's casing rules
    /// should get a say — the Turkish dotless i being the standard example of why
    /// <c>InvariantCultureIgnoreCase</c> is not the same thing.
    /// </remarks>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether two codes name the same employee.</summary>
    public static bool Same(string? left, string? right) =>
        Comparer.Equals(Normalise(left), Normalise(right));
}
