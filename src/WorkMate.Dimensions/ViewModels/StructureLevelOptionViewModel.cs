namespace WorkMate.Dimensions.ViewModels;

/// <summary>One dimension type offered as a candidate level in the structure editor.</summary>
public sealed class StructureLevelOptionViewModel
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// What the level picker shows: the name alone when the code is just the name with the case a
    /// code requires — "Division (Division)" tells a customer nothing a plain "Division" does
    /// not — and the code alongside it only when the two actually differ.
    /// </summary>
    public string DisplayLabel => string.Equals(Code, NameEn, StringComparison.Ordinal)
        ? NameEn
        : $"{NameEn} ({Code})";
}
