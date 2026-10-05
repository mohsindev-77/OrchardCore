using System.Globalization;

namespace WorkMate.Dimensions.Internal;

/// <summary>
/// Reads and writes the one date format that travels between the browser and the server:
/// ISO-8601 <c>yyyy-MM-dd</c>, in the Gregorian calendar, whatever culture anyone is in.
/// </summary>
/// <remarks>
/// An <c>&lt;input type="date"&gt;</c> always submits <c>yyyy-MM-dd</c>, but it <em>displays</em>
/// in the browser's own locale: the same day reads as 05/10/2026 to one user and 10/05/2026 to
/// another. The wire format and the displayed format are different things, and the bug this guards
/// against is treating them as the same one — parsing or formatting the wire value with
/// <see cref="CultureInfo.CurrentCulture"/>, which silently resolves a different day for an Arabic
/// user than for an English one, and under a culture whose default calendar is not Gregorian
/// (ar-SA uses Umm al-Qura) resolves a different year.
///
/// It lives here, rather than inline in the controller, so that it can be pinned directly by tests
/// that run under several cultures. That is cheaper and sharper than inferring the behaviour from
/// a rendered page.
/// </remarks>
public static class IsoDate
{
    public const string Format = "yyyy-MM-dd";

    /// <summary>Parses a wire date, independently of the thread's culture and calendar.</summary>
    public static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Writes a wire date, independently of the thread's culture and calendar.</summary>
    public static string ToIso(this DateOnly date) => date.ToString(Format, CultureInfo.InvariantCulture);
}
