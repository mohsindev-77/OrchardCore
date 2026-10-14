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
    /// How many employees sit at this value itself on the chosen chart, or null when no chart is
    /// chosen.
    /// </summary>
    /// <remarks>
    /// Distinct employees, not assignment rows: a split allocation is several rows for one person
    /// at one unit, and counting rows would say "3" for one person in three slices.
    /// </remarks>
    public int? EmployeeCount { get; set; }

    /// <summary>
    /// How many employees sit at this value <em>or anywhere beneath it</em>, or null when no chart
    /// is chosen.
    /// </summary>
    /// <remarks>
    /// <b>Both numbers are shown because either alone misleads.</b> A division whose people all sit
    /// in its departments has a direct count of zero, and a page that printed "0 here" said the
    /// division was empty when it had four hundred people in it. The reverse is just as bad on a
    /// unit that holds staff directly <em>and</em> contains others.
    ///
    /// Distinct across the whole subtree, which is a stronger statement than summing the direct
    /// counts: one person assigned to two units below this one is one person, and D1's
    /// <c>DISTINCT EmployeeId</c> rule is exactly about that.
    /// </remarks>
    public int? EmployeeCountIncludingSubUnits { get; set; }

    /// <summary>Whether this value has anything below it at all, which decides both the tree and the wording.</summary>
    public bool HasUnitsBelow => Tree.Count > 0;

    /// <summary>The value's own attributes and their values, in schema order.</summary>
    public IReadOnlyList<DimensionRecordAttributeViewModel> Attributes { get; set; } = [];

    /// <summary>
    /// Everything under this value on the chosen chart, in tree order, each row carrying its depth.
    /// Empty for a leaf — and then the whole section is absent rather than empty. Decision D24.
    /// </summary>
    /// <remarks>
    /// <b>Every dimension, not just this one.</b> This is the opposite of the dimension's own
    /// Tree/List screen, which Rule 2 scopes to one dimension: a reader who has opened
    /// <em>Corporate</em> wants to see the departments in it and the sections in those, which is
    /// the organisation rather than the dimension.
    ///
    /// Flattened on the server rather than rendered as nested lists, for the reason the list
    /// screen's tree is: the columns are a table and a table cannot nest. The parent-child
    /// relationship travels on the row as data attributes so the collapse script can find a node's
    /// descendants without a second shape to keep in step — the same markup contract, so the same
    /// script serves both screens.
    /// </remarks>
    public IReadOnlyList<DimensionRecordTreeRowViewModel> Tree { get; set; } = [];

    /// <summary>
    /// Which of the three renderings of <see cref="Tree"/> the reader asked for. Decision D25.
    /// </summary>
    /// <remarks>
    /// <b>Three views of one set of rows, not three queries.</b> The server reads the subtree once
    /// and hands the same rows to whichever view is showing, so switching cannot change what the
    /// section is about — the mistake the designer's own two views were built to avoid, where a
    /// second rendering path drifts from the first.
    /// </remarks>
    public UnitsView UnitsView { get; set; } = UnitsView.Tree;

    /// <summary>
    /// The value itself, with everything under it hanging off it. The hierarchy view's root box.
    /// </summary>
    /// <remarks>
    /// Null for a leaf, where there is no section at all. Built once in the controller rather than
    /// shaped in the view, so the nesting and the flat <see cref="Tree"/> cannot disagree about
    /// what is under what.
    /// </remarks>
    public DimensionRecordHierarchyNodeViewModel? Hierarchy { get; set; }

    /// <summary>
    /// Which column the list view is sorted by, and which way. Decision D25's "sortable by column".
    /// </summary>
    /// <remarks>
    /// <b>On the query string and sorted on the server</b>, so a sorted list is a URL somebody can
    /// bookmark and send, and so it works with no JavaScript — the same choice the dimension list's
    /// own filters and view toggle make. Empty means tree order, which is the order the other two
    /// views show and therefore the one a reader arriving from them expects.
    /// </remarks>
    public string? Sort { get; set; }

    /// <inheritdoc cref="Sort"/>
    public bool SortDescending { get; set; }

    /// <summary>Whether retired units are included in <see cref="Tree"/>.</summary>
    /// <remarks>
    /// Off by default: a chart is normally a question about today. The raw set is what the closure
    /// index says is <em>placed</em> here now, and the filter is on each record's own effective
    /// range — the same Active/All distinction the list screen's Status filter makes, rather than a
    /// second meaning of the word.
    /// </remarks>
    public bool IncludeRetired { get; set; }

    /// <summary>
    /// True when the subtree is larger than one page will draw and the tree shows part of it.
    /// </summary>
    /// <remarks>
    /// A tree and a page cannot be combined — a node whose parent fell outside the slice would be
    /// drawn as a root, which is a wrong shape rather than a partial one — so this says so instead,
    /// exactly as the dimension list's tree view does.
    /// </remarks>
    public bool TreeTruncated { get; set; }

    /// <summary>How many units are under this one in total, whether or not they all fit.</summary>
    public int UnitsBelow { get; set; }

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

/// <summary>
/// The three ways the "Units under this one" section can be drawn. Decision D25.
/// </summary>
/// <remarks>
/// <b>Named in the order the switch shows them</b>, which is widest-to-narrowest: the chart, the
/// indented tree, the flat table. The <em>default</em> is <see cref="Tree"/> rather than the first
/// of them, because it is what the section already was and a reader who had learnt it should not
/// have to find it again.
/// </remarks>
public enum UnitsView
{
    /// <summary>A read-only org chart: a box per unit, joined by lines, zoomable.</summary>
    Hierarchy,

    /// <summary>Indented rows with expand and collapse. The default.</summary>
    Tree,

    /// <summary>A flat table of every descendant, sortable by column.</summary>
    List,
}

/// <summary>
/// One box in the hierarchy chart: a unit, and the units under it. Decision D25.
/// </summary>
/// <remarks>
/// <b>Nested, where <see cref="DimensionRecordTreeRowViewModel"/> is flat</b>, because the chart's
/// markup is nested — <c>ul &gt; li &gt; .designer-card + ul</c>, the same shape the organisation
/// designer's own chart draws, which is what lets this reuse that layout wholesale instead of
/// growing a second one. Both are built from the same subtree read in the same pass, so they
/// cannot disagree about what is under what.
///
/// <b>It carries no actions.</b> The designer's card has an ⋯ menu; this deliberately has none —
/// D25 is explicit that editing stays in the designer and on the value pages, and a read-only chart
/// that grew an edit control would be a second write path into the one thing that most needs one.
/// </remarks>
public sealed class DimensionRecordHierarchyNodeViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>The dimension this unit is of. Empty on the root, which is the page's own value.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Employees here and anywhere below, distinct.</summary>
    public int EmployeeCountIncludingSubUnits { get; set; }

    /// <summary>Employees at this unit itself, distinct. The badge's hover text.</summary>
    public int EmployeeCount { get; set; }

    /// <summary>Whether the unit's own effective range covers the date the page is about.</summary>
    public bool IsEffective { get; set; } = true;

    /// <summary>
    /// True for the page's own value, which is drawn as the root box and does not link anywhere.
    /// </summary>
    /// <remarks>
    /// A link from the root to the page the reader is already on is a control that does nothing,
    /// which is worse than no control. The designer's own structure card is marked the same way.
    /// </remarks>
    public bool IsRoot { get; set; }

    public IReadOnlyList<DimensionRecordHierarchyNodeViewModel> Children { get; set; } = [];

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>
/// One unit under the value whose page this is: what it is, where it sits, and how many people are
/// in it. Decision D24.
/// </summary>
public sealed class DimensionRecordTreeRowViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>The dimension this unit is of — <em>Department</em>, <em>Section</em>.</summary>
    /// <remarks>
    /// Shown as a column because this tree crosses dimensions: without it a reader cannot tell a
    /// department from the section inside it except by the indentation, which is also doing the job
    /// of showing containment.
    /// </remarks>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>How deep below the page's value this sits. One for an immediate child.</summary>
    public int Depth { get; set; }

    /// <summary>The unit it sits directly under, or null when it is an immediate child of the page's value.</summary>
    public string? ParentRecordId { get; set; }

    /// <summary>
    /// What that unit is called, for the list view's Parent column. The page's own value for an
    /// immediate child, because that is what it sits under.
    /// </summary>
    /// <remarks>
    /// The list view is flat, so the indentation that carries containment in the other two views
    /// is not there to carry it — the column is the whole of what tells a reader where a unit sits.
    /// </remarks>
    public string ParentName { get; set; } = string.Empty;

    /// <summary>How many units sit directly under this one, which decides whether it has a toggle.</summary>
    public int ChildCount { get; set; }

    /// <summary>Employees at this unit and everything below it, distinct.</summary>
    public int EmployeeCountIncludingSubUnits { get; set; }

    /// <summary>Employees at this unit itself, distinct. Shown on hover.</summary>
    public int EmployeeCount { get; set; }

    /// <summary>Whether the unit's own effective range covers the date the page is about.</summary>
    public bool IsEffective { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}
