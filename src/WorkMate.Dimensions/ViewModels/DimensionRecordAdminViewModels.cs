using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// The list of one dimension type's records: the rows, and the filters that produced them.
/// </summary>
/// <remarks>
/// <b>Why this screen exists alongside the organisation designer.</b> The designer answers "what
/// sits under what", and it is the right screen for that and the wrong one for everything else: it
/// is a chart, it is navigated by expanding branches, and finding the one department whose code you
/// half remember means opening the branch it is in first. An HR administrator asking "show me the
/// departments" wants a list, a search box and a New button, which is what every other list in the
/// product is. So this is that, once, driven by the type rather than written per type.
/// </remarks>
public sealed class DimensionRecordListViewModel
{
    public string DimensionTypeId { get; set; } = string.Empty;

    public string TypeCode { get; set; } = string.Empty;

    /// <summary>
    /// The type's name, as it is stored: the singular.
    /// </summary>
    /// <remarks>
    /// <b>There is no plural, and the screens are written so as not to need one.</b> A stored
    /// plural was tried, for the withdrawn per-type menu, and the editor that maintained it went
    /// with the menu — so reading it here would be reading a field a customer can no longer
    /// correct. The headings say "Department units" rather than "Departments", which is a whole
    /// sentence the localiser can translate and which no rule has to guess at.
    /// </remarks>
    public string TypeName { get; set; } = string.Empty;

    public string? Search { get; set; }

    /// <summary>
    /// The structure the parent column, the "Within" filter and the headcount are about.
    /// </summary>
    /// <remarks>
    /// Which structures a record is on is a property of its <em>type</em>, so this never changes
    /// which records are listed — only what is said about them. The screen says so rather than
    /// leaving a reader to discover that choosing a structure changed the Parent column and not the
    /// row count.
    /// </remarks>
    public string? StructureId { get; set; }

    public string? Within { get; set; }

    public DimensionRecordStatus Status { get; set; } = DimensionRecordStatus.Active;

    /// <summary>The date the whole screen is resolved as at, ISO-8601. Today unless asked.</summary>
    public string AsAtIso { get; set; } = string.Empty;

    public int Skip { get; set; }

    public int Take { get; set; } = 50;

    public int Total { get; set; }

    public bool HasMore => Skip + Rows.Count < Total;

    public IReadOnlyList<DimensionRecordRowViewModel> Rows { get; set; } = [];

    /// <summary>The structures this type is a level of. Fewer than two means no choice to offer.</summary>
    public IReadOnlyList<StructureChoiceViewModel> Structures { get; set; } = [];

    /// <summary>
    /// The type's own attribute schema, each with whether this reader asked for it as a column.
    /// </summary>
    /// <remarks>
    /// Optional because a type with eight attributes would make an unreadable table and because
    /// which of them matter is the reader's question rather than ours. Off by default, carried in
    /// the query string, so a useful arrangement is a URL somebody can bookmark and share.
    /// </remarks>
    public IReadOnlyList<DimensionAttributeColumnViewModel> AttributeColumns { get; set; } = [];

    /// <summary>Whether this reader may add a record, edit one, or retire one.</summary>
    public bool CanEdit { get; set; }

    /// <summary>Whether this reader may follow a headcount into the employee list.</summary>
    public bool CanViewEmployees { get; set; }

    public IReadOnlyList<DimensionAttributeColumnViewModel> ShownColumns =>
        [.. AttributeColumns.Where(column => column.IsShown)];
}

/// <summary>One structure this type is a level of.</summary>
public sealed record StructureChoiceViewModel(string StructureId, string Code, string Name);

/// <summary>One of the type's attributes, and whether the list is showing it as a column.</summary>
public sealed record DimensionAttributeColumnViewModel(string Name, string Label, bool IsShown);

/// <summary>One record on the list.</summary>
public sealed class DimensionRecordRowViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>Where it sits, one entry per structure the screen is reporting on.</summary>
    public IReadOnlyList<DimensionRecordPlacementViewModel> Placements { get; set; } = [];

    /// <summary>Whether the record's own effective range covers the date the screen is about.</summary>
    public bool IsEffective { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>
    /// The record's own active flag, which is a different thing from existing on a date.
    /// </summary>
    /// <remarks>
    /// A customer can flag a unit inactive — "do not use this for new transactions" — without
    /// closing it, and a unit can be closed while the flag says active. Both are shown, because a
    /// screen that merged them would be answering a question nobody asked with a word that could
    /// mean either.
    /// </remarks>
    public bool IsActive { get; set; }

    /// <summary>How many employees sit here, on the structure the screen is reporting on.</summary>
    public int? EmployeeCount { get; set; }

    /// <summary>The attribute values, in the order of <see cref="DimensionRecordListViewModel.ShownColumns"/>.</summary>
    public IReadOnlyList<string> AttributeValues { get; set; } = [];

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>Where one record sits on one structure.</summary>
/// <param name="Path">
/// The parent's full path, or empty when the record is at the top of the structure or not on it at
/// all — which <paramref name="State"/> tells apart.
/// </param>
public sealed record DimensionRecordPlacementViewModel(
    string StructureId,
    string StructureName,
    string Path,
    DimensionRecordPlacementState State);

/// <summary>Whether a record is placed on a structure, at the top of it, or not on it.</summary>
/// <remarks>
/// Three states rather than two, because "no parent" means two completely different things. A
/// Division at the top of the chart is where a Division belongs; a Department with no parent is
/// invisible to every roll-up and is what the designer's unplaced panel exists to reveal. A column
/// that printed a dash for both would say the second was fine.
/// </remarks>
public enum DimensionRecordPlacementState
{
    /// <summary>Under a parent. <c>Path</c> names the chain.</summary>
    Placed,

    /// <summary>On the structure with no parent, which is what a root type is for.</summary>
    TopLevel,

    /// <summary>Not on the structure at all on that date.</summary>
    Unplaced,
}

/// <summary>
/// Creating a record of one dimension type, with its place on a structure, as one form.
/// </summary>
/// <remarks>
/// <b>One operation, not two.</b> Saving calls <c>IDimensionService.AddUnitAsync</c>, which creates
/// the record and its dated <c>DimensionLink</c> together and validates both before writing either.
/// The alternative — create, then move — is what leaves a record on no structure when the second
/// call fails, which is exactly the half-finished state the unplaced panel exists to reveal. The
/// designer's own Add unit screen calls the same method; there is no second write path and no
/// second set of validation messages.
/// </remarks>
public sealed class DimensionRecordCreateViewModel
{
    public string DimensionTypeId { get; set; } = string.Empty;

    [BindNever]
    public string TypeName { get; set; } = string.Empty;

    public string? StructureId { get; set; }

    /// <summary>The unit it will sit under, or empty for the top of the structure.</summary>
    public string? ParentRecordId { get; set; }

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>ISO-8601 on the wire, always. <c>IsoDate</c> says why.</summary>
    public string? EffectiveFrom { get; set; }

    public List<UnitAttributeInputViewModel> Attributes { get; set; } = [];

    [BindNever]
    public IReadOnlyList<StructureChoiceViewModel> Structures { get; set; } = [];

    /// <summary>
    /// Whether this structure allows a record of this type at the top, which decides whether the
    /// parent picker offers "no parent" at all.
    /// </summary>
    [BindNever]
    public bool MayBeTopLevel { get; set; }

    /// <summary>
    /// Whether this structure has anywhere a record of this type may go, top level or under a
    /// parent. False is a real answer and the form says so rather than offering an empty picker.
    /// </summary>
    [BindNever]
    public bool HasAnywhereToGo { get; set; }

    public BilingualText Name => new(NameEn?.Trim() ?? string.Empty, NameAr?.Trim() ?? string.Empty);

    public IReadOnlyList<DimensionAttributeValue> ToAttributeValues() =>
        [.. Attributes.Select(attribute => new DimensionAttributeValue(
            attribute.Name, attribute.Value, attribute.ValueAr))];
}

/// <summary>
/// Editing an existing record: its names, its attributes, and the dated nature of a rename.
/// </summary>
/// <remarks>
/// <b>Moving a unit is not here.</b> A move changes what every historical report under the unit
/// resolves to, which is why it has its own permission, its own dry run and its own dated form in
/// the designer. This screen links to that one rather than reimplementing it — the brief's "moving
/// a unit stays a dated move, reachable from the list".
///
/// <b>A rename is dated and comes in two kinds.</b> Substantive — the unit genuinely became
/// something else on a date, and prior periods keep the old name — or corrective, meaning the old
/// text was always a mistake. The form asks which, because the two produce different answers to
/// "what was this called last March" and nothing downstream can infer it.
/// </remarks>
public sealed class DimensionRecordEditViewModel
{
    public string RecordId { get; set; } = string.Empty;

    [BindNever]
    public string DimensionTypeId { get; set; } = string.Empty;

    [BindNever]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>The natural key. Shown, never posted: a code names nothing that can be renamed.</summary>
    [BindNever]
    public string Code { get; set; } = string.Empty;

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    [BindNever]
    public string CurrentNameEn { get; set; } = string.Empty;

    [BindNever]
    public string CurrentNameAr { get; set; } = string.Empty;

    /// <summary>The date a substantive rename takes effect from. Ignored by a correction.</summary>
    public string? EffectiveFrom { get; set; }

    /// <summary>
    /// True when the old text was always wrong, so the new name applies to the period that already
    /// covers <see cref="EffectiveFrom"/> instead of starting a new one.
    /// </summary>
    public bool IsCorrection { get; set; }

    public List<UnitAttributeInputViewModel> Attributes { get; set; } = [];

    /// <summary>Where it sits now, read-only, with a link to the move screen for each.</summary>
    [BindNever]
    public IReadOnlyList<DimensionRecordPlacementViewModel> Placements { get; set; } = [];

    [BindNever]
    public string? StructureId { get; set; }

    public BilingualText Name => new(NameEn?.Trim() ?? string.Empty, NameAr?.Trim() ?? string.Empty);

    /// <summary>Whether either half of the name differs from what is stored.</summary>
    public bool NameChanged =>
        !string.Equals(Name.En, CurrentNameEn?.Trim() ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(Name.Ar, CurrentNameAr?.Trim() ?? string.Empty, StringComparison.Ordinal);

    public IReadOnlyList<DimensionAttributeValue> ToAttributeValues() =>
        [.. Attributes.Select(attribute => new DimensionAttributeValue(
            attribute.Name, attribute.Value, attribute.ValueAr))];
}
