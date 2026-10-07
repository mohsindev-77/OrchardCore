using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

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

    /// <summary>The two halves of the name, as posted.</summary>
    /// <remarks>
    /// Declared nullable, and that is load-bearing twice over. It is what the binder actually
    /// produces — an empty box binds to null, because <c>ConvertEmptyStringToNull</c> defaults to
    /// true — so a non-nullable declaration was a claim the framework was never going to honour.
    ///
    /// And it is what stops MVC inventing a rule of its own. A non-nullable reference type property
    /// gets an implicit <c>required</c> in model state, so an empty Arabic box failed
    /// <c>ModelState.IsValid</c> with "The NameAr field is required" before the controller called
    /// anything — which would have refused an optional Arabic name whatever the tenant's setting
    /// said. Validation belongs to <c>IDimensionValidator</c>, which is the only thing that knows
    /// whether this tenant requires Arabic.
    /// </remarks>
    public string? NameEn { get; set; }

    /// <inheritdoc cref="NameEn"/>
    public string? NameAr { get; set; }

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; } = true;

    public bool IsPrimaryOrganisation { get; set; }

    /// <summary>
    /// The axis's types in reading order. Ordinals are assigned from this order, not stored
    /// separately. Since ADR-0010 this is the structure's vocabulary — the rows and columns of the
    /// containment grid and the order of every picker — rather than its containment rule.
    /// </summary>
    public List<string> LevelDimensionTypeIds { get; set; } = [];

    /// <summary>The types ticked as permitted at the top of the structure.</summary>
    public List<string> RootDimensionTypeIds { get; set; } = [];

    /// <summary>
    /// The ticked cells of the containment grid, each <c>"parentId&gt;childId"</c>.
    /// </summary>
    /// <remarks>
    /// Flat strings rather than indexed model binding because a grid is checkboxes, and an
    /// unticked checkbox posts nothing at all. Indexed binding would need a hidden companion field
    /// per cell to carry the "false", on a grid whose size changes in the browser as the type list
    /// is edited; a list of the ticks is the same information with nothing to keep in step.
    /// </remarks>
    public List<string> ContainmentPairs { get; set; } = [];

    /// <summary>Every dimension type the level builder may offer, for the picker.</summary>
    [BindNever]
    public List<StructureLevelOptionViewModel> AvailableDimensionTypes { get; set; } = [];

    public bool IsNew => string.IsNullOrEmpty(StructureId);

    /// <summary>
    /// The two halves as the value object services take.
    /// </summary>
    /// <remarks>
    /// Null-guarded, and it has to be. MVC's <c>ComplexObjectModelBinder</c> reads every public
    /// getter on the model while it is binding, and its
    /// <c>ModelMetadata.ConvertEmptyStringToNull</c> defaults to <see langword="true"/> — so an
    /// empty text box arrives as <see langword="null"/>, not as an empty string, whatever the
    /// property's initialiser says and whatever nullable annotations claim. An unguarded
    /// <c>NameAr.Trim()</c> therefore threw inside the binder, which is before any controller code
    /// or validation runs: a blank name box produced a 500 rather than a message under the field.
    /// </remarks>
    public BilingualText Name => new(NameEn?.Trim() ?? string.Empty, NameAr?.Trim() ?? string.Empty);

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
        RootDimensionTypeIds = [.. document.RootDimensionTypeIds],
        ContainmentPairs =
        [
            .. document.Containment.Select(pair => Pair(pair.ParentDimensionTypeId, pair.ChildDimensionTypeId)),
        ],
    };

    /// <summary>The posted levels with blank rows dropped, root first.</summary>
    public IReadOnlyList<string> ToLevelDimensionTypeIds() =>
        [.. LevelDimensionTypeIds.Where(id => !string.IsNullOrWhiteSpace(id))];

    /// <summary>
    /// The posted grid as the rules the service writes, with anything naming a type no longer in
    /// the list dropped.
    /// </summary>
    /// <remarks>
    /// A tick whose type has just been removed from the vocabulary is dropped rather than
    /// reported: it is the grid's memory of a row that is gone, not a mistake the user made, and
    /// the validator would otherwise refuse the save naming a type the screen no longer shows.
    ///
    /// Null when nothing at all is ticked, which hands the service the chain to derive from. That
    /// is the honest reading of an empty grid: a Create form starts with no types chosen and so no
    /// grid to tick, and a client with no script never grows one — a post from either is
    /// describing a plain chain, which is what the screen offers as its own starting point. Tick
    /// one root and the grid becomes the authority, including about what is <em>not</em> ticked.
    /// </remarks>
    public StructureShape? ToShape()
    {
        if (RootDimensionTypeIds.Count == 0 && ContainmentPairs.Count == 0)
        {
            return null;
        }

        var vocabulary = ToLevelDimensionTypeIds().ToHashSet(StringComparer.Ordinal);

        var containment = ContainmentPairs
            .Select(Split)
            .Where(pair =>
                pair is not null &&
                vocabulary.Contains(pair.ParentDimensionTypeId) &&
                vocabulary.Contains(pair.ChildDimensionTypeId))
            .Select(pair => pair!);

        return new StructureShape(
            [.. RootDimensionTypeIds.Where(vocabulary.Contains)],
            [.. containment]);
    }

    /// <summary>Whether the grid's cell for this pairing is ticked.</summary>
    public bool Permits(string parentDimensionTypeId, string childDimensionTypeId) =>
        ContainmentPairs.Contains(Pair(parentDimensionTypeId, childDimensionTypeId), StringComparer.Ordinal);

    /// <summary>Whether this type is ticked as one that may sit at the top.</summary>
    public bool IsRoot(string dimensionTypeId) =>
        RootDimensionTypeIds.Contains(dimensionTypeId, StringComparer.Ordinal);

    /// <summary>One grid cell's posted value. The separator cannot occur in a generated id.</summary>
    public static string Pair(string parentDimensionTypeId, string childDimensionTypeId) =>
        $"{parentDimensionTypeId}>{childDimensionTypeId}";

    private static StructureContainment? Split(string pair)
    {
        var parts = (pair ?? string.Empty).Split('>', 2);

        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part))
            ? new StructureContainment(parts[0], parts[1])
            : null;
    }
}
