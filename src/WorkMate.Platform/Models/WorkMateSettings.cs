namespace WorkMate.Platform.Models;

/// <summary>
/// The platform-wide site settings section, per specification section 3.
/// Module-specific settings live in their own settings parts and are never added here.
/// </summary>
/// <remarks>
/// This type has public setters, which the "no public setters on domain models" rule
/// otherwise forbids. A site settings section is not a domain model: Orchard serialises it
/// into the site document and the display driver writes it back from the editor, both of
/// which need settable properties. The carve-out is the same one content parts have.
/// Every property carries a default, so a tenant that has never opened the settings screen
/// still behaves sensibly.
/// </remarks>
public sealed class WorkMateSettings
{
    /// <summary>The name Orchard stores this section under in the site document.</summary>
    public const string SectionName = nameof(WorkMateSettings);

    /// <summary>
    /// The culture a user sees before they have chosen one. Must be one of the tenant's
    /// supported cultures, which the base recipe sets to <c>en</c> and <c>ar</c>.
    /// </summary>
    public string DefaultCulture { get; set; } = DefaultCultureName;

    /// <summary>Which calendar dates are presented in. Dates are always stored Gregorian.</summary>
    public CalendarPreference Calendar { get; set; } = CalendarPreference.Gregorian;

    /// <summary>
    /// Whether the shell lays out right to left. The default follows the active culture,
    /// which is what a bilingual tenant wants; the two explicit values exist for a tenant
    /// that runs one direction regardless of the language a user picks.
    /// </summary>
    public TextDirectionPreference TextDirection { get; set; } = TextDirectionPreference.FollowCulture;

    /// <summary>The month the financial year begins, 1 to 12.</summary>
    public int FiscalYearStartMonth { get; set; } = DefaultFiscalYearStartMonth;

    /// <summary>The day of <see cref="FiscalYearStartMonth"/> the financial year begins.</summary>
    public int FiscalYearStartDay { get; set; } = DefaultFiscalYearStartDay;

    /// <summary>The ISO 4217 code money is expressed in unless an element overrides it.</summary>
    public string CurrencyCode { get; set; } = DefaultCurrencyCode;

    /// <summary>The days of the week that are working days.</summary>
    /// <remarks>
    /// An array rather than a list, deliberately. Deserialising into a collection property that
    /// already holds the defaults can populate it instead of replacing it, which silently turns
    /// a saved Sunday-to-Thursday week into the union of that and the Monday-to-Friday default.
    /// An array is always replaced.
    /// </remarks>
    public DayOfWeek[] WorkingDays { get; set; } = [.. DefaultWorkingDays];

    /// <summary>The day a week is considered to start on, for rosters and week-based reports.</summary>
    public DayOfWeek WeekStartsOn { get; set; } = DefaultWeekStartsOn;

    /// <summary>
    /// The customer's short code, the first part of the tenant name in
    /// <c>&lt;customer-code&gt;-&lt;env&gt;</c>. Empty until set; the settings service derives
    /// it from the tenant name rather than leaving a caller with an empty string.
    /// </summary>
    public string CustomerCode { get; set; } = string.Empty;

    /// <summary>
    /// Copies every value onto <paramref name="target"/>. The settings editor needs this:
    /// Orchard's section driver writes the section instance it handed us back into the site
    /// document after the driver returns, so the instance the service validated and the
    /// instance the framework persists have to hold the same values.
    /// </summary>
    public void CopyTo(WorkMateSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.DefaultCulture = DefaultCulture;
        target.Calendar = Calendar;
        target.TextDirection = TextDirection;
        target.FiscalYearStartMonth = FiscalYearStartMonth;
        target.FiscalYearStartDay = FiscalYearStartDay;
        target.CurrencyCode = CurrencyCode;
        target.WorkingDays = [.. WorkingDays];
        target.WeekStartsOn = WeekStartsOn;
        target.CustomerCode = CustomerCode;
    }

    public const string DefaultCultureName = "en";

    // The first tenants are in Bahrain, so the defaults are Bahraini: the dinar, and a
    // Sunday-to-Thursday week. A tenant elsewhere changes them once, on this screen or through
    // its own recipe; nothing in the platform assumes them.
    public const string DefaultCurrencyCode = "BHD";
    public const int DefaultFiscalYearStartMonth = 1;
    public const int DefaultFiscalYearStartDay = 1;
    public const DayOfWeek DefaultWeekStartsOn = DayOfWeek.Sunday;

    public static readonly DayOfWeek[] DefaultWorkingDays =
    [
        DayOfWeek.Sunday,
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
    ];
}
