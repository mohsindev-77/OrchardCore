namespace WorkMate.Platform.Models;

/// <summary>
/// How dates are presented to users. Storage is unaffected: specification section 5 fixes
/// dates as stored Gregorian, and this only decides what a screen shows.
/// </summary>
public enum CalendarPreference
{
    /// <summary>Gregorian only.</summary>
    Gregorian,

    /// <summary>Umm al-Qura (Hijri) only.</summary>
    UmmAlQura,

    /// <summary>Gregorian, with the Hijri equivalent shown alongside it.</summary>
    GregorianWithHijri,
}
