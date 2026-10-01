using OrchardCore.ContentManagement.Metadata.Models;
using WorkMate.Platform.Fields;

namespace WorkMate.Platform.ViewModels;

/// <summary>The editor's shape of a <see cref="BilingualTextField"/>.</summary>
/// <remarks>Not sealed: Orchard builds shape view models through Castle DynamicProxy.</remarks>
public class EditBilingualTextFieldViewModel
{
    public string En { get; set; } = string.Empty;

    public string Ar { get; set; } = string.Empty;

    public BilingualTextField Field { get; set; } = new();

    public ContentPartFieldDefinition? PartFieldDefinition { get; set; }

    public BilingualTextFieldSettings Settings { get; set; } = new();
}

/// <summary>The display shape of a <see cref="BilingualTextField"/>.</summary>
public class DisplayBilingualTextFieldViewModel
{
    public string En { get; set; } = string.Empty;

    public string Ar { get; set; } = string.Empty;

    public BilingualTextField Field { get; set; } = new();

    public ContentPartFieldDefinition? PartFieldDefinition { get; set; }
}

/// <summary>The settings editor shown when the field is attached to a type.</summary>
public class BilingualTextFieldSettingsViewModel
{
    public string Hint { get; set; } = string.Empty;

    public string HintAr { get; set; } = string.Empty;

    public bool RequireEnglish { get; set; } = true;

    public bool RequireArabic { get; set; }

    public int MaxLength { get; set; }
}
