using WorkMate.Core;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// One dimension value's own page: everything that is true of it today, and the ways to change it.
/// Decision D23 C.
/// </summary>
/// <remarks>
/// <b>Why a value has a page at all.</b> The Organisation menu now expands to a dimension's values,
/// and clicking one has to land somewhere. The three candidates already existed and none of them is
/// this: the list screen is about a dimension rather than a value, the editor is a form rather than
/// an answer, and the designer is a chart that has to be navigated to reach the branch the value is
/// on. What a reader clicking <em>Civil</em> wants is what Civil <em>is</em> — its code, where it
/// sits, who leads it, how many people are in it — with the ways to act on it beside the facts.
///
/// <b>Read-only, and it reuses rather than reimplements.</b> Every button leads to a screen that
/// already exists and already owns its rules: Edit is the record editor, Move is the designer's
/// dated move with its dry run, Retire is the designer's dated retirement with its orphan plan.
/// Nothing here writes, so there is no second write path and no second set of validation messages —
/// the same promise <c>DimensionRecordsAdminController</c> makes for the list.
/// </remarks>
public sealed class DimensionRecordDetailsViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string AliasEn { get; set; } = string.Empty;

    public string AliasAr { get; set; } = string.Empty;

    /// <summary>The dimension this value is of, and the group its menu entry belongs to.</summary>
    public string DimensionTypeId { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    /// <summary>The group key, so "back to the list" goes back to the list the reader came from.</summary>
    public string Group { get; set; } = string.Empty;

    public string GroupCaption { get; set; } = string.Empty;

    /// <summary>
    /// The organisation this value belongs to, when its dimension's name is shared by more than one
    /// type. Empty otherwise, for the reason the menu's brackets are absent then.
    /// </summary>
    public string Organisation { get; set; } = string.Empty;

    /// <summary>Where it sits, one entry per chart its type is a level of.</summary>
    public IReadOnlyList<DimensionRecordPlacementViewModel> Placements { get; set; } = [];

    /// <summary>Whether the value's own effective range covers the date this page is about.</summary>
    public bool IsEffective { get; set; }

    /// <summary>
    /// The value's own active flag, which is a different thing from existing on a date.
    /// </summary>
    /// <remarks>
    /// Shown beside the dates rather than merged with them, for the reason the list's Status column
    /// shows both: a customer can flag a unit inactive without closing it, and a closed unit can
    /// still be flagged active. One word that could mean either would be answering a question
    /// nobody asked.
    /// </remarks>
    public bool IsActive { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>The date the whole page is resolved as at, ISO-8601. Today unless asked.</summary>
    public string AsAtIso { get; set; } = string.Empty;

    /// <summary>
    /// The chart the head, the headcount and the Move and Retire buttons are about.
    /// </summary>
    /// <remarks>
    /// Null when the type is a level of several charts and the reader has not said which. A head is
    /// an appointment on one chart (ADR-0012) and a headcount counts employees placed on one, so
    /// both are questions about a structure — and a page that picked one would be the guess the list
    /// screen already refuses to make for its Parent column.
    /// </remarks>
    public string? StructureId { get; set; }

    /// <summary>The charts this value's type is a level of. Fewer than two means no choice to offer.</summary>
    public IReadOnlyList<StructureChoiceViewModel> Structures { get; set; } = [];

    /// <summary>Who leads this unit on the chosen chart today, or empty when nobody does.</summary>
    public string HeadName { get; set; } = string.Empty;

    /// <summary>The head's appointment dates, when there is a head.</summary>
    public DateOnly? HeadFrom { get; set; }

    /// <inheritdoc cref="HeadFrom"/>
    public DateOnly? HeadTo { get; set; }

    /// <summary>
    /// How many employees sit at this value on the chosen chart, or null when no chart is chosen.
    /// </summary>
    public int? EmployeeCount { get; set; }

    /// <summary>The value's own attributes and their values, in schema order.</summary>
    public IReadOnlyList<DimensionRecordAttributeViewModel> Attributes { get; set; } = [];

    public bool CanEdit { get; set; }

    public bool CanMove { get; set; }

    /// <summary>Whether this reader may follow the headcount into the employee list.</summary>
    public bool CanViewEmployees { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);

    public string Alias => BilingualText.Display(AliasEn, AliasAr);

    /// <summary>Whether there is a head to name.</summary>
    public bool HasHead => !string.IsNullOrWhiteSpace(HeadName);
}

/// <summary>One of the value's own attributes, label and value both resolved for the reader.</summary>
public sealed record DimensionRecordAttributeViewModel(string Label, string Value);
