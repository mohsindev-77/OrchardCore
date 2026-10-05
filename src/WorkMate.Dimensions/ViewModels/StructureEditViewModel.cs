using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// The structure editor: shared between create and edit. <see cref="Code"/> is editable only on
/// create — links, closure rows and assignments are scoped by the structure's id, and recipes
/// reference it by code, so it is immutable once created, the same rule
/// <see cref="DimensionTypeEditViewModel"/> applies to a dimension type.
/// </summary>
/// <remarks>
/// <see cref="StructureId"/> and <see cref="AvailableDimensionTypes"/> are display-only on every
/// path — <c>EditPost</c> always re-sets <see cref="StructureId"/> from the loaded document before
/// using it, and nothing ever reads a posted value for either — so both carry
/// <see cref="BindNeverAttribute"/>. <see cref="StructureId"/> was already nullable and so was
/// never at risk of the defect <c>DimensionTypeEditViewModel.ContentTypeName</c> had; the attribute
/// here is the same defence in depth, applied before a problem shows up rather than after.
/// </remarks>
public sealed class StructureEditViewModel
{
    [BindNever]
    public string? StructureId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; } = true;

    public bool IsPrimaryOrganisation { get; set; }

    /// <summary>The levels, root first. Ordinals are assigned from this order, not stored separately.</summary>
    public List<string> LevelDimensionTypeIds { get; set; } = [];

    /// <summary>Every dimension type the level builder may offer, for the picker.</summary>
    [BindNever]
    public List<StructureLevelOptionViewModel> AvailableDimensionTypes { get; set; } = [];

    public bool IsNew => string.IsNullOrEmpty(StructureId);

    public BilingualText Name => new(NameEn.Trim(), NameAr.Trim());

    public static StructureEditViewModel Of(StructureDocument document) => new()
    {
        StructureId = document.StructureId,
        Code = document.Code,
        NameEn = document.Name.En,
        NameAr = document.Name.Ar,
        AllowSkipLevel = document.AllowSkipLevel,
        IsStrict = document.IsStrict,
        IsPrimaryOrganisation = document.IsPrimaryOrganisation,
        LevelDimensionTypeIds =
        [
            .. document.Levels.OrderBy(level => level.Ordinal).Select(level => level.DimensionTypeId),
        ],
    };

    /// <summary>The posted levels with blank rows dropped, root first.</summary>
    public IReadOnlyList<string> ToLevelDimensionTypeIds() =>
        [.. LevelDimensionTypeIds.Where(id => !string.IsNullOrWhiteSpace(id))];
}
