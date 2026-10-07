using WorkMate.Platform.Models;

namespace WorkMate.Platform.ViewModels;

/// <summary>
/// The editor's shape of <see cref="WorkMateSettings"/>. Working days are a set of checkboxes
/// rather than a list, because model binding a list of enum values from checkboxes is fiddly
/// and a seven-day week is not worth the indirection.
/// </summary>
/// <remarks>
/// Not sealed, deliberately. Orchard Core 3.0.1 builds a shape's view model through Castle
/// DynamicProxy, which subclasses it; a sealed view model fails at render with
/// "the parent type is sealed".
/// </remarks>
public class WorkMateSettingsViewModel
{
    public string DefaultCulture { get; set; } = WorkMateSettings.DefaultCultureName;

    public CalendarPreference Calendar { get; set; }

    public TextDirectionPreference TextDirection { get; set; }

    public int FiscalYearStartMonth { get; set; } = WorkMateSettings.DefaultFiscalYearStartMonth;

    public int FiscalYearStartDay { get; set; } = WorkMateSettings.DefaultFiscalYearStartDay;

    public string CurrencyCode { get; set; } = WorkMateSettings.DefaultCurrencyCode;

    public DayOfWeek WeekStartsOn { get; set; } = WorkMateSettings.DefaultWeekStartsOn;

    public string CustomerCode { get; set; } = string.Empty;

    /// <summary>Whether a name must be given in Arabic as well as English. Off by default.</summary>
    public bool RequireArabicNames { get; set; }

    public bool WorksMonday { get; set; }

    public bool WorksTuesday { get; set; }

    public bool WorksWednesday { get; set; }

    public bool WorksThursday { get; set; }

    public bool WorksFriday { get; set; }

    public bool WorksSaturday { get; set; }

    public bool WorksSunday { get; set; }

    /// <summary>The cultures the tenant supports, for the default-locale list.</summary>
    public IReadOnlyList<string> SupportedCultures { get; set; } = [];

    /// <summary>
    /// Fills a view model the shape factory has already created. Orchard builds the model
    /// instance itself, so this fills one rather than returning a new one.
    /// </summary>
    public static void Fill(
        WorkMateSettingsViewModel model,
        WorkMateSettings settings,
        IReadOnlyList<string> supportedCultures)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(settings);

        model.DefaultCulture = settings.DefaultCulture;
        model.Calendar = settings.Calendar;
        model.TextDirection = settings.TextDirection;
        model.FiscalYearStartMonth = settings.FiscalYearStartMonth;
        model.FiscalYearStartDay = settings.FiscalYearStartDay;
        model.CurrencyCode = settings.CurrencyCode;
        model.WeekStartsOn = settings.WeekStartsOn;
        model.CustomerCode = settings.CustomerCode;
        model.RequireArabicNames = settings.RequireArabicNames;
        model.WorksSunday = settings.WorkingDays.Contains(DayOfWeek.Sunday);
        model.WorksMonday = settings.WorkingDays.Contains(DayOfWeek.Monday);
        model.WorksTuesday = settings.WorkingDays.Contains(DayOfWeek.Tuesday);
        model.WorksWednesday = settings.WorkingDays.Contains(DayOfWeek.Wednesday);
        model.WorksThursday = settings.WorkingDays.Contains(DayOfWeek.Thursday);
        model.WorksFriday = settings.WorkingDays.Contains(DayOfWeek.Friday);
        model.WorksSaturday = settings.WorkingDays.Contains(DayOfWeek.Saturday);
        model.SupportedCultures = supportedCultures;
    }

    public WorkMateSettings ToSettings() => new()
    {
        DefaultCulture = DefaultCulture,
        Calendar = Calendar,
        TextDirection = TextDirection,
        FiscalYearStartMonth = FiscalYearStartMonth,
        FiscalYearStartDay = FiscalYearStartDay,
        CurrencyCode = (CurrencyCode ?? string.Empty).Trim().ToUpperInvariant(),
        WeekStartsOn = WeekStartsOn,
        CustomerCode = (CustomerCode ?? string.Empty).Trim().ToLowerInvariant(),
        RequireArabicNames = RequireArabicNames,
        WorkingDays = [.. SelectedWorkingDays()],
    };

    private IEnumerable<DayOfWeek> SelectedWorkingDays()
    {
        if (WorksSunday) yield return DayOfWeek.Sunday;
        if (WorksMonday) yield return DayOfWeek.Monday;
        if (WorksTuesday) yield return DayOfWeek.Tuesday;
        if (WorksWednesday) yield return DayOfWeek.Wednesday;
        if (WorksThursday) yield return DayOfWeek.Thursday;
        if (WorksFriday) yield return DayOfWeek.Friday;
        if (WorksSaturday) yield return DayOfWeek.Saturday;
    }
}
