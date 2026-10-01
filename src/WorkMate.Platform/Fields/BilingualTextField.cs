using OrchardCore.ContentManagement;
using WorkMate.Core;

namespace WorkMate.Platform.Fields;

/// <summary>
/// A name in both platform languages. Every name field on the platform is one of these, per
/// specification section 5.
/// </summary>
/// <remarks>
/// This lives in WorkMate.Platform rather than WorkMate.Core because an Orchard content field
/// needs OrchardCore.ContentManagement and a Razor project, and Core is required to have no
/// Orchard dependency. <see cref="BilingualText"/>, the value object services pass around,
/// stays in Core. ADR-0003 records the split.
///
/// Not sealed: Orchard deserialises fields and builds their editor shapes through proxies.
/// </remarks>
public class BilingualTextField : ContentField
{
    /// <summary>The English text. This is the sort key and the fallback.</summary>
    public string En { get; set; } = string.Empty;

    /// <summary>The Arabic text.</summary>
    public string Ar { get; set; } = string.Empty;

    /// <summary>The value object services use, so that nothing outside this module handles the field itself.</summary>
    public BilingualText ToBilingualText() => new(En, Ar);

    /// <summary>Replaces both languages at once.</summary>
    public void Set(BilingualText value)
    {
        ArgumentNullException.ThrowIfNull(value);

        En = value.En;
        Ar = value.Ar;
    }

    /// <summary>
    /// The text to show in <paramref name="culture"/>, falling back to the other language rather
    /// than to nothing: a half-translated record should still be readable, and an empty name in a
    /// picker is worse than a name in the wrong language.
    /// </summary>
    public string ForCulture(string? culture)
    {
        var wantsArabic = culture is not null &&
            culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        var preferred = wantsArabic ? Ar : En;
        var fallback = wantsArabic ? En : Ar;

        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }
}
