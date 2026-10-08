using Microsoft.Extensions.Localization;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// An employment status as a word a reader understands, in their own language.
/// </summary>
/// <remarks>
/// <b>Never <c>status.ToString()</c> inside a localised sentence.</b> That compiles, reads correctly
/// in English, and puts the enum member — "OnLeave" — into the middle of an otherwise translated
/// Arabic sentence. It is also invisible to <c>LocalisationResourceTests</c>, which looks for string
/// literals, so the five names would have no resource entry and nothing would report it.
///
/// One switch, called by every service and controller message that names a status. The views have
/// their own copies as partials, because a view's localiser is an <c>IHtmlLocalizer</c> and returns
/// markup rather than a string — the same rule, expressed where it has to be.
/// </remarks>
public static class EmployeeStatusNames
{
    /// <summary>The status named, for interpolation into a localised sentence.</summary>
    public static string Of(IStringLocalizer localiser, EmploymentStatus status)
    {
        ArgumentNullException.ThrowIfNull(localiser);

        return status switch
        {
            EmploymentStatus.Prospective => localiser["Prospective"],
            EmploymentStatus.Active => localiser["Active"],
            EmploymentStatus.OnLeave => localiser["On leave"],
            EmploymentStatus.Suspended => localiser["Suspended"],
            EmploymentStatus.Exited => localiser["Exited"],
            _ => localiser["Unknown"],
        };
    }
}
