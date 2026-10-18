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

    /// <summary>
    /// Whether <see cref="StructureId"/> is a reporting line rather than an organisation chart.
    /// </summary>
    /// <remarks>
    /// <b>What a position's page is about.</b> A position sits on two axes (ADR-0028) and only one
    /// of them is a tree it is the parent of: on the organisation chart a post is a leaf under a
    /// department, so a page scoped there has no section at all, which is what manual testing found
    /// on <em>Manager Technology</em> and <em>Site Supervisor</em>. Their reporting lines were three
    /// and one deep respectively, and the page drew neither.
    ///
    /// So a position's page is always resolved to its reporting axis and this is true; every other
    /// dimension keeps the structure the reader chose, and this is false.
    /// </remarks>
    public bool IsReportingAxis { get; set; }

    /// <summary>
    /// The reporting axis this post sits on, for the <em>Change reports to…</em> action. F6c.
    /// </summary>
    public string? ReportingStructureId { get; set; }

    /// <summary>
    /// Whether to offer the Structure chooser. Never on a position.
    /// </summary>
    /// <remarks>
    /// The chooser exists for a type that is a level of several charts and no way to tell which the
    /// reader means. A position is always on two — its unit's chart and its reporting line — but
    /// there is nothing to ask: its page is about the reporting line, and where it sits in the
    /// business is one row of the facts above. A control whose only two settings are "the right one"
    /// and "a chart where this is a leaf" is a way to make the page wrong.
    /// </remarks>
    public bool ShowsStructureChooser => !IsPosition && Structures.Count > 1;

    /// <summary>
    /// The units above this position on the organisation axis, highest first. Positions only.
    /// </summary>
    /// <remarks>
    /// <b>A position's page shows its two parents as two rows, not as one list of placements.</b>
    /// The generic Parent row prints one line per axis — <em>"Zenith reporting line: CEO › Director
    /// Technology"</em> beside <em>"Zenith Engineering: Corporate › HR"</em> — which is accurate and
    /// unreadable: the two are different questions about the post and a reader has to work out which
    /// line answers which. ADR-0028 says a position has two parents on two axes; the page now says
    /// the same thing in two rows with their own names.
    /// </remarks>
    public IReadOnlyList<DimensionRecordReportsToViewModel> OrganisationPath { get; set; } = [];

    /// <summary>
    /// What to call the organisation row — the dimension the post actually sits under.
    /// </summary>
    /// <remarks>
    /// <b>"Department" on Zenith, and not hard-coded to it.</b> A structure decides what may contain
    /// a position, so on another tenant the answer is Division or Section or whatever a customer has
    /// renamed those to under FRS Rule 6. A fixed caption would be right for the demo and a lie
    /// everywhere else, so the row is named for the immediate parent's own dimension and falls back
    /// to <em>Organisation</em> when the post is not placed and there is nothing to name.
    /// </remarks>
    public string OrganisationLabel { get; set; } = string.Empty;

    /// <summary>
    /// Who holds this position today. Empty until slice F6 gives employees their positions.
    /// </summary>
    /// <remarks>
    /// <b>The empty state ships now, deliberately.</b> An establishment of three approved and none
    /// filled is a fact about a post that a reader will immediately want the other half of, and a
    /// page that simply had no such section leaves them wondering whether it is missing or whether
    /// nobody holds the post. Saying "no one holds this position yet" answers that, and it is the
    /// true answer until F6 — nothing assigns a position to an employee before then, so this list
    /// cannot be anything but empty, and that is asserted rather than assumed.
    /// </remarks>
    public IReadOnlyList<PositionHolderViewModel> Holders { get; set; } = [];

    /// <summary>
    /// How many hold it in total, which is not <c>Holders.Count</c> on a post with more than a
    /// page of them.
    /// </summary>
    /// <remarks>
    /// <em>Labourer</em> has an approved headcount of 44 in the brief's own example and a real one
    /// could have hundreds, so the list is paged and says so rather than quietly showing the first
    /// fifty — the same judgement the value tree's truncation notice makes.
    /// </remarks>
    public int HoldersTotal { get; set; }

    /// <summary>
    /// The posts between the top of the reporting line and this one, highest first. Empty for a
    /// position that reports to nobody, and on every other dimension.
    /// </summary>
    /// <remarks>
    /// <b>The chain above, where the section below is the chain beneath.</b> A reader opening
    /// <em>Software Engineer</em> is shown who answers to it — nobody — and would otherwise be told
    /// nothing at all about where it sits on the line. One row of links is the whole answer, and it
    /// is the same ancestor-path read the Placements row already makes, on the other axis.
    /// </remarks>
    public IReadOnlyList<DimensionRecordReportsToViewModel> ReportsTo { get; set; } = [];

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
    /// Whether this value is a position, which is what puts its establishment and its grades on the
    /// page. Decisions D26 C and D26 E.
    /// </summary>
    /// <inheritdoc cref="IsGrade" path="/remarks"/>
    public bool IsPosition { get; set; }

    /// <summary>
    /// How many of this post are approved, filled and vacant. Null on every other dimension.
    /// Decision D26 E.
    /// </summary>
    /// <remarks>
    /// <b>Beside the facts rather than only on the chart.</b> The badge on a reporting box answers
    /// "how full is that one" while a reader is scanning; this answers "how full is this one" on the
    /// page they opened to find out. The same three numbers the Positions list draws, from the same
    /// read.
    /// </remarks>
    public Services.PositionEstablishment? Establishment { get; set; }

    /// <summary>
    /// The grades this position allows: its levels, in the order somebody chose. Decision D26 C.
    /// </summary>
    /// <remarks>
    /// <b>Shown even when empty</b>, for the reason <see cref="UsedByPositions"/> is: a position
    /// with no grades is a post nobody can be given a level in, and that is what somebody looking at
    /// it needs to be told.
    /// </remarks>
    public IReadOnlyList<DimensionRecordReferrerViewModel> AllowedGrades { get; set; } = [];

    /// <summary>
    /// Whether this value is a grade, which is the one thing that puts <see cref="UsedByPositions"/>
    /// on the page. Decision D26 C.
    /// </summary>
    /// <remarks>
    /// Read off the dimension type's declared <c>StandardRole</c>, never off its code or its name —
    /// <c>DimensionTypeRole</c> gives the argument, and the short form of it is that the demo
    /// recipes ship <c>zenith-grade</c> and FRS Rule 6 lets a customer rename Grade to Band.
    /// </remarks>
    public bool IsGrade { get; set; }

    /// <summary>
    /// The positions that allow this grade, effective on the date the page is about. Decision
    /// D26 C's <em>"Used by positions: …"</em>.
    /// </summary>
    /// <remarks>
    /// <b>Empty is a real answer and is said out loud</b>, rather than the section being hidden: a
    /// grade no position allows is a grade nobody can be given, and that is exactly what somebody
    /// looking at a grade's page needs to be told. It is the opposite judgement from
    /// <see cref="Tree"/>, where a leaf shows no section at all — a unit with nothing under it is
    /// ordinary, and a grade nothing uses is a loose end.
    /// </remarks>
    public IReadOnlyList<DimensionRecordReferrerViewModel> UsedByPositions { get; set; } = [];

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
/// One record that points at the value whose page this is — a position that allows this grade.
/// </summary>
/// <param name="UnitName">
/// Where that position sits in the business, so two positions of the same name on two charts are
/// tellable apart. Empty when it is unplaced, which is a state the page shows rather than hides.
/// </param>
public sealed record DimensionRecordReferrerViewModel(
    string RecordId,
    string Code,
    string Name,
    string UnitName);

/// <summary>
/// One step in a path above this record — a unit on the organisation axis, or a post on the
/// reporting line. Linked, so the chain walks.
/// </summary>
public sealed record DimensionRecordReportsToViewModel(string RecordId, string Name);

/// <summary>
/// One person holding a position: who, at what grade, since when. Decision D26 D.
/// </summary>
/// <remarks>
/// Nothing produces one of these yet — an employee's position arrives in slice F6. The shape is
/// fixed here because the section that shows it ships now with its empty state, and a section whose
/// row shape was decided later would be a second design meeting with the first one's markup already
/// in the wild.
/// </remarks>
public sealed record PositionHolderViewModel(
    string EmployeeId,
    string Name,
    string GradeName,
    DateOnly Since);

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

    /// <summary>
    /// How many of this post are approved and filled, when the chart is a reporting line.
    /// Decision D26 E.
    /// </summary>
    /// <remarks>
    /// <b>Null on every chart but a reporting one</b>, and the box then shows the employee badge it
    /// has always shown. On a reporting chart the employee counts are the wrong number to draw: an
    /// assignment names the organisation axis, so "employees below this box" on a reporting line
    /// counts nobody, and a badge reading zero on every box would be worse than no badge.
    ///
    /// <b>Green when full, amber when there is a vacancy</b>, which is the brief's own wording and
    /// is deliberately not the other way round: a reader scanning a reporting chart for something to
    /// act on is looking for the gaps.
    /// </remarks>
    public Services.PositionEstablishment? Establishment { get; set; }

    /// <summary>
    /// The unit this post sits in, on a reporting chart. Empty elsewhere, and empty for a position
    /// nothing has placed in the business yet.
    /// </summary>
    /// <remarks>
    /// On a reporting chart the box's dimension is <em>Position</em> on every box, which tells a
    /// reader nothing. What tells two posts called <em>Supervisor</em> apart is where they sit, and
    /// that is a question about the <em>other</em> axis — which is the whole of ADR-0028 showing up
    /// on a screen.
    /// </remarks>
    public string UnitName { get; set; } = string.Empty;

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

    /// <summary>
    /// How many of this post are filled and approved, on a reporting line. Null on every other
    /// chart.
    /// </summary>
    /// <remarks>
    /// <b>The same reason the hierarchy box carries it</b> (D26 E), arriving in the other two views:
    /// an assignment names the organisation axis, so an employee count on a reporting line is zero
    /// on every row. The Tree and List views show <em>Holders</em> here instead, filled over
    /// approved, from the one read that fills the chart.
    /// </remarks>
    public Services.PositionEstablishment? Establishment { get; set; }

    /// <summary>Whether the unit's own effective range covers the date the page is about.</summary>
    public bool IsEffective { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}
