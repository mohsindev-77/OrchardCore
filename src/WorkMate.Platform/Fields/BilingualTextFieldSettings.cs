namespace WorkMate.Platform.Fields;

/// <summary>
/// What an administrator can configure about a bilingual field when they attach it to a content
/// type, through the form designer or the content type editor.
/// </summary>
public class BilingualTextFieldSettings
{
    /// <summary>Help text shown under the field. Bilingual at definition time, per section 5.</summary>
    public string Hint { get; set; } = string.Empty;

    public string HintAr { get; set; } = string.Empty;

    /// <summary>Whether the English text is required. English is the sort key and the fallback.</summary>
    public bool RequireEnglish { get; set; } = true;

    /// <summary>Whether the Arabic text is required too.</summary>
    public bool RequireArabic { get; set; }

    /// <summary>Longest permitted value in either language. Zero means no limit.</summary>
    public int MaxLength { get; set; }
}
