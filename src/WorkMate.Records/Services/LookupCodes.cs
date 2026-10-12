using System.Text.RegularExpressions;

namespace WorkMate.Records.Services;

/// <summary>
/// What a lookup list's code and an entry's code may look like, and how two of them are compared.
/// </summary>
/// <remarks>
/// Separate from the service for the reason <c>EmployeeCodes</c> and <c>DimensionCodes</c> are
/// separate from theirs: the rule has to be checkable before anything is written, and a test should
/// be able to enumerate the awkward cases without standing up a tenant.
///
/// <b>Two rules, not one, and the difference is the point.</b> A list code is an identifier
/// <em>we</em> author — <c>job-title</c>, <c>grade</c>, <c>document-type</c> — which appears in a
/// URL, in a recipe step and, from slice B2, in a field's binding settings. An entry code is
/// whatever the customer already calls the thing: <c>G1</c>, <c>001</c>, <c>FT</c>,
/// <c>PASSPORT</c>. Holding both to the stricter rule would refuse data customers arrive with;
/// holding both to the looser one would let a slash into a URL segment.
/// </remarks>
public static partial class LookupCodes
{
    /// <summary>The longest code either kind may be. Mirrored by the migration's column width.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// A list code: a leading letter, then letters, digits, hyphens and underscores.
    /// </summary>
    /// <remarks>
    /// The leading letter and the absence of dots and slashes are both about where this code is
    /// used rather than about tidiness. It is a route value on the admin screens, a JSON property
    /// value in a recipe, and — from slice B2 — the binding stored in a field's settings. A slash
    /// would split a URL segment; a leading digit reads as an index to anybody scanning a recipe.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ListCodePattern { get; }

    /// <summary>
    /// An entry code: one to sixty-four characters of letters, digits, hyphen, underscore, dot or
    /// slash.
    /// </summary>
    /// <remarks>
    /// Deliberately as loose as <c>EmployeeCodes</c> and for the same reason: this is the value a
    /// customer already holds. A grade called <c>G1</c>, a document type called <c>ID.CARD</c> and
    /// an employment type called <c>FT</c> are all real, and a rule that refused any of them would
    /// mean a migration on every customer bringing their own codes. Whitespace is still refused,
    /// because a code with a space in it is a code half of whose uses will trim it and half
    /// will not.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ItemCodePattern { get; }

    /// <summary>Whether <paramref name="code"/> is a well-formed lookup list code.</summary>
    public static bool IsValidListCode(string? code) => code is not null && ListCodePattern.IsMatch(code);

    /// <summary>Whether <paramref name="code"/> is a well-formed entry code.</summary>
    public static bool IsValidItemCode(string? code) => code is not null && ItemCodePattern.IsMatch(code);

    /// <summary>The code as it is stored and compared: trimmed, and nothing else.</summary>
    /// <remarks>
    /// Case is preserved on the way in and ignored on the way out, exactly as an employee code is.
    /// A customer whose grades read <c>G1</c> sees them that way, and a recipe shouting <c>g1</c>
    /// finds the same entry rather than creating a second one beside it.
    /// </remarks>
    public static string Normalise(string? code) => code?.Trim() ?? string.Empty;

    /// <summary>How two lookup codes are compared, everywhere.</summary>
    /// <remarks>
    /// Ordinal and case-insensitive: identifiers, not words, so no culture's casing rules get a say
    /// — the Turkish dotless i being the standard reason <c>InvariantCultureIgnoreCase</c> is not
    /// the same thing.
    /// </remarks>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether two codes name the same list or the same entry.</summary>
    public static bool Same(string? left, string? right) =>
        Comparer.Equals(Normalise(left), Normalise(right));
}
