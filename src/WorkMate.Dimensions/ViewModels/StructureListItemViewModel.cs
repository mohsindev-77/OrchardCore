using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>One row of the structures list.</summary>
public sealed class StructureListItemViewModel
{
    public string StructureId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>The levels, root first, as human-readable labels rather than ids.</summary>
    public IReadOnlyList<string> LevelLabels { get; set; } = [];

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; }

    public bool IsPrimaryOrganisation { get; set; }

    public static StructureListItemViewModel Of(
        StructureDocument document, IReadOnlyDictionary<string, DimensionTypeDocument> typesById) => new()
    {
        StructureId = document.StructureId,
        Code = document.Code,
        NameEn = document.Name.En,
        NameAr = document.Name.Ar,
        AllowSkipLevel = document.AllowSkipLevel,
        IsStrict = document.IsStrict,
        IsPrimaryOrganisation = document.IsPrimaryOrganisation,
        LevelLabels =
        [
            .. document.Levels
                .OrderBy(level => level.Ordinal)
                .Select(level => typesById.TryGetValue(level.DimensionTypeId, out var type)
                    ? type.Name.En
                    : level.DimensionTypeId),
        ],
    };
}
