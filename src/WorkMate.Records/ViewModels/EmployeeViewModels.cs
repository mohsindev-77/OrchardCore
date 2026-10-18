using System.Globalization;
using OrchardCore.DisplayManagement;
using WorkMate.Core;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;

namespace WorkMate.Records.ViewModels;

/// <summary>
/// One row of the employee list.
/// </summary>
/// <remarks>
/// Every bilingual half is <c>string?</c>, and every view model in this file follows the same rule.
/// Two reasons, easy to conflate and both real: an empty text box binds to <c>null</c> rather than
/// <c>""</c>, and a non-nullable reference property gets an implicit <c>required</c> in model state
/// — which would refuse an empty Arabic name before the controller ran, whatever the tenant's
/// setting said. ADR-0003's addendum, and <c>ViewModelsSurviveNullBindingTests</c> guards it.
/// </remarks>
public sealed class EmployeeListItemViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public EmploymentStatus Status { get; set; }

    public DateOnly JoinDate { get; set; }

    public DateOnly StatusEffectiveFrom { get; set; }

    /// <summary>The name in the reader's language, falling back rather than to a blank.</summary>
    public string Name => BilingualText.Display(NameEn, NameAr);

    /// <summary>
    /// The lifecycle moves valid for this employee right now, in the order to offer them.
    /// </summary>
    /// <remarks>
    /// From <c>EmployeeLifecycle.ActionsFrom</c>, so a row's menu and the editor's buttons offer
    /// the same set and neither can drift from what the service will accept. Whether the viewer may
    /// use them is a separate question — <c>EmployeeListViewModel.CanChangeStatus</c> — because an
    /// action that is valid and forbidden should not be rendered at all.
    /// </remarks>
    public IReadOnlyList<EmployeeTransition> Transitions => EmployeeLifecycle.ActionsFrom(Status);

    /// <summary>Where the main placement puts them today, or null when they are not placed.</summary>
    /// <remarks>
    /// Read through <c>IEmployeeAssignmentService</c> like everything else about placement, and
    /// shown on the list because "which department" is the first thing anybody scanning a list of
    /// people wants to know — and the one thing the employee record deliberately does not hold.
    ///
    /// <b>The main one, never an arbitrary one.</b> Somebody split 60/40 has two placements, and
    /// the list used to show whichever row the engine returned first — so the same person could
    /// read differently on two screens for no reason anybody could explain.
    /// </remarks>
    public string? PrimaryUnitName { get; set; }

    /// <summary>The main placement's full path, for the cell's title. See <see cref="OtherUnits"/>.</summary>
    public string? PrimaryUnitPath { get; set; }

    /// <summary>
    /// The placements beyond the main one, each with its full path and share — the "+N more".
    /// </summary>
    /// <remarks>
    /// Shown behind a disclosure rather than inline, because the overwhelming majority of rows have
    /// none and a column that widened for all of them to accommodate the few would make the list
    /// harder to scan. Full paths, because two of the units a person is split across routinely have
    /// the same name.
    /// </remarks>
    public IReadOnlyList<EmployeePlacementLine> OtherUnits { get; set; } = [];

    /// <summary>
    /// What they hold, where it sits and who answers for it — all derived. Slice F6c.
    /// </summary>
    /// <remarks>
    /// <b>One object rather than four properties</b>, because they are one read:
    /// <c>IEmployeeOrganisationService.GetLinesAsync</c> resolves the whole page in a bounded
    /// number of queries, and splitting its answer across four setters here would invite the next
    /// column to be filled by a fifth read per row.
    ///
    /// Null on a tenant with no positions at all, where none of these columns is shown — D30's own
    /// scope, and what keeps that tenant's list exactly as it was.
    /// </remarks>
    public EmployeeOrganisationLine? Organisation { get; set; }

    /// <summary>
    /// The star's tooltip — "Heads Civil, Mechanical" — or null when this person heads nothing.
    /// </summary>
    /// <remarks>
    /// <b>Every unit they head, not only the one the list is filtered to.</b> A head of three
    /// departments told "Heads this unit" has been told the least useful true thing available.
    /// ADR-0012: a headship is its own dated record, and one person holding several is an ordinary
    /// shape the Zenith demo deliberately contains.
    ///
    /// Composed by the controller, where the localiser and the culture are, for the reason
    /// <c>DesignerNodeViewModel.HeadLabel</c> is: a list separator is a property of the language,
    /// and a view that joined the names with ", " would be writing English punctuation into an
    /// Arabic sentence.
    /// </remarks>
    public string? HeadsLabel { get; set; }

    /// <summary>
    /// What this person is at the unit the list is filtered to: their primary placement, a
    /// secondary one, or the headship alone.
    /// </summary>
    /// <remarks>
    /// Null on the unfiltered list, where there is no unit for a role to be relative to. The
    /// distinction is the matrix: a site engineer is primary at their home department and secondary
    /// at the team, and a list that showed them the same way on both screens would be hiding the
    /// arrangement the whole dimension engine exists to express.
    /// </remarks>
    public UnitRole? RoleHere { get; set; }

    /// <summary>
    /// How much of this person the unit accounts for, when the list is filtered to one.
    /// </summary>
    /// <remarks>
    /// Null for a head who is not a member: an appointment carries no allocation — ADR-0012 — and
    /// rendering a zero would say the unit accounts for none of them, which is a different and
    /// false claim.
    /// </remarks>
    public decimal? AllocationPercent { get; set; }

    /// <summary>Whether this person heads the unit the list is filtered to.</summary>
    public bool IsHeadHere { get; set; }

    public static EmployeeListItemViewModel Of(EmployeeRecord employee)
    {
        ArgumentNullException.ThrowIfNull(employee);

        return new EmployeeListItemViewModel
        {
            EmployeeId = employee.EmployeeId,
            Code = employee.Code,
            NameEn = employee.NameEn,
            NameAr = employee.NameAr,
            Status = employee.Status,
            JoinDate = employee.JoinDate,
            StatusEffectiveFrom = employee.StatusEffectiveFrom,
        };
    }
}

/// <summary>
/// What <c>_LifecycleActions</c> needs: whose transitions, which ones, and how to draw them.
/// </summary>
/// <remarks>
/// One partial renders the editor's buttons and each list row's menu, so the two offer the same set
/// and a transition cannot be reachable from one screen and invisible on the other — which is the
/// defect this type was added to close.
/// </remarks>
/// <param name="AsMenu">
/// True on a list row, where a column has no space for five buttons; false on the editor, where
/// the actions are the point of the section they sit in.
/// </param>
public sealed record LifecycleActionsViewModel(
    string EmployeeId,
    IReadOnlyList<EmployeeTransition> Transitions,
    bool CanChangeStatus,
    bool AsMenu);

/// <summary>The employee list screen: its rows, and the filter that produced them.</summary>
public sealed class EmployeeListViewModel
{
    public IReadOnlyList<EmployeeListItemViewModel> Employees { get; set; } = [];

    public string? Search { get; set; }

    /// <summary>The status filter, or null for every status including leavers.</summary>
    public EmploymentStatus? Status { get; set; }

    public int Total { get; set; }

    public int Skip { get; set; }

    public int Take { get; set; } = 50;

    public bool HasMore => Skip + Employees.Count < Total;

    public bool CanManage { get; set; }

    public bool CanChangeStatus { get; set; }

    public bool CanAssign { get; set; }

    /// <summary>The unit this list is filtered to, or null for the whole tenant.</summary>
    public UnitFilterViewModel? Unit { get; set; }

    /// <summary>
    /// Whether this tenant places employees at positions at all. Slice F6c, decision D30.
    /// </summary>
    /// <remarks>
    /// False keeps the list exactly as F6a shipped it — Code, Name, Status, Joined, Where they sit
    /// — because D30 is scoped to a structure that has positions and a tenant with none must see
    /// no change at all. Asked of the axes, never of this page's employees, who may simply not be
    /// placed yet.
    /// </remarks>
    public bool IsPositionBased { get; set; }

    /// <summary>The position the list is filtered to, or null. Slice F6c.</summary>
    public string? PositionRecordId { get; set; }

    /// <summary>Its name, for the heading and the cleared-filter link.</summary>
    public string? PositionName { get; set; }

    /// <summary>How the list is ordered. Slice F6c.</summary>
    public EmployeeListSort Sort { get; set; }

    /// <summary>
    /// Set when the list was asked to sort by something it could not, and why. Slice F6c.
    /// </summary>
    /// <remarks>
    /// Position and grade are not columns on <c>EmployeeIndex</c> and cannot be — see the module
    /// README. Sorting by them is done over the matching employees, which is only affordable up to
    /// a cap; past it the list keeps its name order and <b>says so</b> rather than quietly
    /// returning a sorted subset, which would look exactly like a correct answer.
    /// </remarks>
    public bool SortWasTooWide { get; set; }
}

/// <summary>
/// One sortable column heading: which order it asks for, and whether the list is in it.
/// </summary>
/// <remarks>
/// Carries the whole list model rather than the handful of filter values the link needs, because
/// the link's job is to change ONE thing and keep everything else — and a model that listed the
/// filters by hand would silently drop the next one somebody adds.
/// </remarks>
public sealed record EmployeeSortLinkViewModel(
    EmployeeListViewModel List,
    EmployeeListSort Sort,
    string Label)
{
    /// <summary>Whether the list is currently in this order.</summary>
    public bool IsCurrent => List.Sort == Sort;
}

/// <summary>How the employees list is ordered. Slice F6c.</summary>
/// <remarks>
/// <b>Name is index-backed and the other two are not</b>, which is the whole reason this is an
/// enum of three rather than a column name. <c>EmployeeIndex</c> can order by name and code
/// forever; a position's name lives on a dimension record and a grade's on an assignment row, so
/// ordering by either is a join YesSql cannot make and this module resolves by hand.
/// </remarks>
public enum EmployeeListSort
{
    /// <summary>By name, then code. The default, and the only one the index can do alone.</summary>
    Name,

    /// <summary>By the position they hold, then by name within it.</summary>
    Position,

    /// <summary>By the grade on their placement row, then by name within it.</summary>
    Grade,
}

/// <summary>
/// What a unit-filtered employee list is filtered by, and what to say about it.
/// </summary>
/// <param name="IncludeSubUnits">
/// Whether people beneath the unit are counted and listed as well.
/// </param>
/// <param name="MemberTotal">
/// How many people are <em>at</em> the unit, which is the number the designer card shows.
/// </param>
/// <remarks>
/// <b>Sub-units default to off.</b> The card is the way in, the card's number counts the node
/// itself, and a screen that opened showing a different number from the one just clicked would be
/// answering a question nobody asked. Turning it on is one click and the header says what changed.
///
/// <see cref="MemberTotal"/> comes from <c>IEmployeeAssignmentService</c> — the same call that
/// draws the card — rather than from the length of the list, which is a page and is narrowed by any
/// search. Two numbers from two sources is how a chart and a list come to disagree.
/// </remarks>
public sealed record UnitFilterViewModel(
    string StructureId,
    string RecordId,
    string StructureName,
    string UnitName,
    string AsAtIso,
    bool IncludeSubUnits,
    int MemberTotal);

/// <summary>What somebody is at the unit a list is filtered to.</summary>
/// <remarks>
/// <see cref="HeadOnly"/> is the case ADR-0012 exists for: a head who is not a member of the unit
/// they head. They are listed, because "who runs this department" is the first thing anybody asks
/// of a department, and not counted, because they work somewhere else and that somewhere else is
/// already counting them.
/// </remarks>
public enum UnitRole
{
    Primary,
    Secondary,
    HeadOnly,
}

/// <summary>Creating an employee. The only screen that does: see the Records README.</summary>
public sealed class EmployeeCreateViewModel
{
    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>ISO-8601 on the wire, always. <c>IsoDate</c> in Dimensions says why.</summary>
    public string? JoinDate { get; set; }

    public string? DateOfBirth { get; set; }

    public string? NationalityCode { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public string? LineManagerEmployeeId { get; set; }

    // ---- the fixed "Job and organisation" block (D30, design note §2.1 and §9.2) --------------

    /// <summary>
    /// The axis the position is chosen from. Settled by the screen when the tenant has one.
    /// </summary>
    public string? StructureId { get; set; }

    /// <summary>The position this person joins at, or null to create them unplaced.</summary>
    /// <remarks>
    /// <b>Offered inline and optional.</b> A new employee with no position is the state this slice
    /// exists to stop being normal, so the question is on the form rather than on a screen somebody
    /// has to find — but leaving it blank still creates an unplaced employee exactly as before.
    /// Making it required would break every tenant that hires before HR has built its position
    /// list, and would make ADR-0029's mixed state impossible to live with.
    /// </remarks>
    public string? PositionRecordId { get; set; }

    /// <summary>The grade held at that position. D26 G: one of the position's own, or none.</summary>
    public string? GradeRecordId { get; set; }

    /// <summary>Whether this axis places employees at positions. D30, ADR-0030.</summary>
    /// <remarks>
    /// False and the form is the one it has always been — which is the whole of D30's scope for a
    /// tenant that has built no positions.
    /// </remarks>
    public bool IsPositionBased { get; set; }

    /// <summary>Where the chosen position sits and who it answers to. Null until one is chosen.</summary>
    public PositionOrganisation? Organisation { get; set; }

    /// <summary>The axes an employee may be placed on, for the chooser. Never every structure.</summary>
    public IReadOnlyList<StructureOptionViewModel> Structures { get; set; } = [];

    /// <summary>Whether to ask which axis, which is only worth asking when there are several.</summary>
    public bool ShowsStructureChooser => Structures.Count > 1;

    /// <summary>
    /// True when this post is a redraw after the position or the axis changed, not a save.
    /// </summary>
    /// <remarks>
    /// The same flag and the same reason as the Placement screen's: changing the position used to
    /// run the whole save path and tell somebody off for not having finished a question they had
    /// only just been asked.
    /// </remarks>
    public bool IsRefresh { get; set; }

    /// <summary>
    /// Whether somebody asked to name a line manager the reporting line does not imply. D31.
    /// </summary>
    /// <remarks>
    /// <b>Not stored.</b> It selects which control the form renders; what is saved is the same
    /// <c>LineManagerEmployeeId</c> it always was. Whether the saved value <em>is</em> an exception
    /// is worked out by comparing it with the post's holders, not by trusting this flag — a form
    /// value cannot be the record of a fact.
    /// </remarks>
    public bool LineManagerOverride { get; set; }

    /// <summary>The line-manager control's state, derived from the chosen position. D31.</summary>
    public LineManagerViewModel LineManager =>
        LineManagerViewModel.Of(Organisation, LineManagerEmployeeId, LineManagerOverride);
}

/// <summary>Changing the editable core of an existing employee.</summary>
/// <remarks>
/// The code is not here and neither is the status. The code is the natural key and is immutable;
/// the status is only reachable through the dated transitions, because changing it closes
/// placements and headships and raises an event, none of which a field edit can carry out.
/// </remarks>
public sealed class EmployeeEditViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    /// <summary>Shown, never posted back. Here so the form says who it is about.</summary>
    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string? DateOfBirth { get; set; }

    public string? NationalityCode { get; set; }

    public Gender Gender { get; set; } = Gender.Unspecified;

    public string? LineManagerEmployeeId { get; set; }

    public string? PhotoPath { get; set; }

    public EmploymentStatus Status { get; set; }

    public DateOnly JoinDate { get; set; }

    /// <summary>Whether the viewer may move this employee through the lifecycle.</summary>
    public bool CanChangeStatus { get; set; }

    /// <summary>
    /// Whether this employee has just been created, so the screen can offer the obvious next step.
    /// </summary>
    /// <remarks>
    /// Everybody is created prospective and has to be activated by somebody. Landing on an editor
    /// that merely reports "Prospective" leaves the one thing the person came to do unlabelled, so
    /// the screen says it: "Activate now".
    /// </remarks>
    public bool IsNewlyCreated { get; set; }

    /// <summary>The lifecycle moves valid for this employee right now. See the list item's copy.</summary>
    public IReadOnlyList<EmployeeTransition> Transitions => EmployeeLifecycle.ActionsFrom(Status);

    /// <summary>The name in the reader's language, falling back rather than to a blank.</summary>
    public string Name => BilingualText.Display(NameEn, NameAr);

    // ---- the fixed core's second block, and the loop below it (design note §9.2) --------------

    /// <summary>
    /// Where this person sits today, derived from the position they hold. D30.
    /// </summary>
    /// <remarks>
    /// <b>Read-only here, and that is the decision rather than an omission.</b> The design note's
    /// §2.1: a display driver's <c>UpdateAsync</c> has nowhere to put a required effective date, and
    /// every write in this slice is dated with nothing defaulted (CLAUDE.md rule 3). So this block
    /// shows what is true and links to the dated forms — <em>Change position</em>, <em>Change
    /// grade</em> — which are on <c>EmployeePlacementAdminController</c> and are the one write path.
    ///
    /// The Create form is the exception that proves it: it may ask for a position <em>because it
    /// already asks for a join date</em>, which is the effective date the write needs.
    ///
    /// Null on a tenant with no positions, where the whole block is absent and the form is the one
    /// it has always been.
    /// </remarks>
    public EmployeeOrganisation? Organisation { get; set; }

    /// <summary>Whether this employee is on an axis that places people at positions. D30.</summary>
    public bool IsPositionBased { get; set; }

    /// <summary>Who manages this person, from the one read path. D31 addendum 2.</summary>
    /// <remarks>
    /// Derived live where the chart answers and stored only where somebody chose, so a change of
    /// holder above shows here without anybody editing this record.
    /// </remarks>
    public LineManagerAnswer LineManager { get; set; } = LineManagerAnswer.Nobody();

    /// <summary>
    /// The sections <em>this tenant</em> has, in its own order — never the six as a literal.
    /// </summary>
    /// <remarks>
    /// §9.2 Rule 2. The page renders the fixed core and then loops over this, so a section the form
    /// designer adds in B3 appears without this view or this model being touched. Read through
    /// <c>IEmployeeSectionCatalogue</c>, which asks the content definition rather than
    /// <c>EmployeeSections.All</c>.
    /// </remarks>
    public IReadOnlyList<EmployeeSectionRef> Sections { get; set; } = [];
}

/// <summary>A dated lifecycle transition, as a form posts it.</summary>
public sealed class EmployeeTransitionViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>What the employee is now, so the form can say what it is changing from.</summary>
    public EmploymentStatus CurrentStatus { get; set; }

    public DateOnly CurrentStatusFrom { get; set; }

    /// <summary>What they would become.</summary>
    public EmploymentStatus To { get; set; }

    /// <summary>
    /// The two statuses as words a reader understands, resolved by the controller.
    /// </summary>
    /// <remarks>
    /// Carried on the model rather than switched over in the view, because both go <em>inside</em>
    /// a translated sentence — "{0} has been {1} since {2}" — and a partial renders markup rather
    /// than a word. The controller has the localiser and <c>EmployeeStatusNames</c>; the view
    /// interpolates what it is given.
    ///
    /// Never <c>Status.ToString()</c>: that puts the enum member into the middle of an Arabic
    /// sentence and is invisible to the resource check.
    /// </remarks>
    public string? CurrentStatusName { get; set; }

    /// <inheritdoc cref="CurrentStatusName"/>
    public string? ToStatusName { get; set; }

    /// <summary>
    /// The date the change takes effect, ISO-8601. Defaulted on the <em>form</em> to today, which
    /// is not the same as defaulting it on the write — the service refuses a transition with no
    /// date, and this is a suggestion a person can see and change before they commit.
    /// </summary>
    public string? EffectiveFrom { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>
/// The exit dry run: everything ending employment would close, before any of it is closed.
/// </summary>
/// <remarks>
/// The same dry-run-then-apply shape as a move, a merge, a retirement and a level change. The head
/// appointments are the half worth reading: a department losing its approver on Friday is a fact
/// somebody has to act on, and discovering it from a stuck approval the following week is too late.
/// </remarks>
public sealed class EmployeeExitViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public EmploymentStatus CurrentStatus { get; set; }

    /// <summary>The last day of service, ISO-8601 — the number a person recognises.</summary>
    public string? LastDay { get; set; }

    /// <summary>Whether the plan below was computed, or the form is being shown for the first time.</summary>
    public bool HasPlan { get; set; }

    /// <summary>Placements that would be closed, one per axis the employee sits on.</summary>
    public IReadOnlyList<ExitPlacementViewModel> Placements { get; set; } = [];

    /// <summary>Units that would be left without a head.</summary>
    public IReadOnlyList<VacatedUnitViewModel> UnitsLosingTheirHead { get; set; } = [];

    public string Name => BilingualText.Display(NameEn, NameAr);

    public bool LeavesAUnitVacant => UnitsLosingTheirHead.Count > 0;
}

/// <summary>One placement an exit would close, named rather than identified.</summary>
public sealed class ExitPlacementViewModel
{
    public string? StructureName { get; set; }

    public string? UnitName { get; set; }

    public string? UnitCode { get; set; }

    public decimal AllocationPercent { get; set; }

    public bool IsPrimary { get; set; }
}

/// <summary>One unit an exit would leave vacant.</summary>
public sealed class VacatedUnitViewModel
{
    public string? StructureCode { get; set; }

    public string? RecordCode { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);

    public static VacatedUnitViewModel Of(VacatedUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        return new VacatedUnitViewModel
        {
            StructureCode = unit.StructureCode,
            RecordCode = unit.RecordCode,
            NameEn = unit.NameEn,
            NameAr = unit.NameAr,
        };
    }
}

/// <summary>
/// Where one employee sits, on every axis, and what they lead.
/// </summary>
/// <remarks>
/// The Placement section the brief asks for, as its own screen rather than as a driver on the
/// employee editor. A driver's <c>UpdateAsync</c> has nowhere to put a required effective date, and
/// every write here is dated — so the editor shows placement read-only and links here, and each
/// operation is its own small form with its own date, exactly as the organisation designer's
/// actions are.
/// </remarks>
public sealed class EmployeePlacementViewModel
{
    /// <summary>Who manages this person, from the one read path. D31 addendum 2.</summary>
    /// <remarks>
    /// Carried so this screen's "Reports to" row can name the person on the record rather than the
    /// one the reporting line implies — the two differ wherever somebody overrode it, and a page
    /// that showed the implied one would contradict the record it is drawn from.
    /// </remarks>
    public LineManagerAnswer LineManager { get; set; } = LineManagerAnswer.Nobody();

    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>The date being shown, ISO-8601. Placement is always a question about a date.</summary>
    public string? AsAt { get; set; }

    public IReadOnlyList<AxisPlacementViewModel> Axes { get; set; } = [];

    public IReadOnlyList<HeadshipViewModel> Headships { get; set; } = [];

    public bool CanAssign { get; set; }

    /// <summary>
    /// Where this person sits, derived from the position they hold. Decision D30.
    /// </summary>
    /// <remarks>
    /// Null when they are on no axis at all. <c>HasNoPositionYet</c> is the other state worth
    /// branching on: somebody placed directly in a unit on an axis that has positions, which is
    /// what ADR-0029 leaves behind and what the <em>Choose position</em> action acts on.
    /// </remarks>
    public EmployeeOrganisation? Organisation { get; set; }

    /// <summary>Every dated period this person has held, newest first.</summary>
    public IReadOnlyList<JobHistoryRowViewModel> JobHistory { get; set; } = [];

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>What one employee's placement looks like on one axis on one date.</summary>
public sealed class AxisPlacementViewModel
{
    public string StructureId { get; set; } = string.Empty;

    public string? StructureCode { get; set; }

    public string? StructureNameEn { get; set; }

    public string? StructureNameAr { get; set; }

    /// <summary>Every placement effective on the date, primary first. Empty when not placed.</summary>
    public IReadOnlyList<PlacementRowViewModel> Rows { get; set; } = [];

    /// <summary>Whether this axis places employees at positions. D30, ADR-0030.</summary>
    /// <remarks>
    /// Decides what the row offers: <em>Change position</em> on a position-based axis, the split
    /// screen on one that still places people at units. The second is not a lesser case — a
    /// tenant with no positions keeps it exactly as it was.
    /// </remarks>
    public bool IsPositionBased { get; set; }

    public string StructureName => BilingualText.Display(StructureNameEn, StructureNameAr);

    public bool IsPlaced => Rows.Count > 0;
}

/// <summary>One placement row.</summary>
public sealed class PlacementRowViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string? UnitCode { get; set; }

    public string? UnitNameEn { get; set; }

    public string? UnitNameAr { get; set; }

    /// <summary>
    /// The unit's full path — "Crescent › Retail Banking › Credit" — where the screen has room for
    /// it and the reader needs it to tell two units of the same name apart. Null where it was not
    /// resolved.
    /// </summary>
    public string? UnitPath { get; set; }

    public decimal AllocationPercent { get; set; }

    public bool IsPrimary { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    public string UnitName => BilingualText.Display(UnitNameEn, UnitNameAr);
}

/// <summary>One unit this employee leads.</summary>
public sealed class HeadshipViewModel
{
    public string StructureId { get; set; } = string.Empty;

    public string? StructureCode { get; set; }

    public string RecordId { get; set; } = string.Empty;

    public string? UnitCode { get; set; }

    public string? UnitNameEn { get; set; }

    public string? UnitNameAr { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    public string UnitName => BilingualText.Display(UnitNameEn, UnitNameAr);
}

/// <summary>Placing an employee at a unit, from a stated date.</summary>
/// <remarks>
/// <b>Two ways in, one screen.</b> From an employee, where the person is known and the unit is the
/// question; and from a unit's card on the organisation designer, where it is the other way round.
/// The second is why <see cref="EmployeeId"/> can arrive empty and the form grows an employee
/// picker: a reader looking at Credit who wants somebody in it should not have to go and find the
/// employee list first, and an action that only worked from one direction would be an action half
/// the readers never discover.
/// </remarks>
public sealed class PlaceEmployeeViewModel
{
    /// <summary>Empty when the screen was opened from a unit rather than from a person.</summary>
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string? StructureId { get; set; }

    public string? RecordId { get; set; }

    public string? EffectiveFrom { get; set; }

    /// <summary>
    /// How much of the employee this placement accounts for. 100 unless the cost is split.
    /// </summary>
    public decimal AllocationPercent { get; set; } = 100m;

    /// <summary>
    /// Whether this is the placement that answers "where does this person work".
    /// </summary>
    /// <remarks>
    /// True by default, because the overwhelmingly common case is somebody's only placement, and
    /// exactly one of an employee's concurrent placements on an axis must be primary. The matrix
    /// case — a home department and a site team — is the one where somebody unticks it.
    /// </remarks>
    public bool IsPrimary { get; set; } = true;

    /// <summary>
    /// The grade held here, when the unit chosen is a position. Decision D26 G, slice F6a.
    /// </summary>
    /// <remarks>
    /// <b>Null for a placement at a unit</b>, which is what this screen has always made and what
    /// ADR-0029 keeps valid: a tenant has unit placements and position placements side by side, and
    /// only the second kind carries a grade.
    ///
    /// <b>Posted but not yet picked on this form.</b> F6a makes a position assignable through this
    /// screen and carries the grade through the write path; F6b gives it the drop-down D26 G
    /// describes — empty and disabled until a position is chosen, refreshed when it changes. Until
    /// then the grade can be stated by a recipe and by a test, and the validator refuses one that
    /// is not among the position's allowed grades wherever it comes from.
    /// </remarks>
    public string? GradeRecordId { get; set; }

    /// <summary>
    /// Whether this axis places employees at positions. Decision D30, ADR-0030.
    /// </summary>
    /// <remarks>
    /// <b>What the form becomes rather than a flag it carries.</b> True and the screen asks for a
    /// Position, a Grade and a date, and shows where that puts them; false and it is the unit
    /// picker it has always been. A tenant with no positions sees no change at all, which is D30's
    /// own scope.
    /// </remarks>
    public bool IsPositionBased { get; set; }

    /// <summary>Where the chosen position sits and who it answers to. Null until one is chosen.</summary>
    public PositionOrganisation? Organisation { get; set; }

    /// <summary>
    /// Whether this redraw was caused by the AXIS changing, rather than by the position. D31.
    /// </summary>
    /// <remarks>
    /// Only an axis change drops the chosen node: a unit or position belongs to the axis that was
    /// showing and means nothing on another. A position change redraws so that the derived block
    /// and the line manager follow it, and must keep the very choice that caused it.
    /// </remarks>
    public bool AxisChanged { get; set; }

    /// <summary>Whether somebody asked to name a line manager outside the reporting line. D31.</summary>
    public bool LineManagerOverride { get; set; }

    /// <summary>The line-manager control's state, derived from the chosen position. D31.</summary>
    public LineManagerViewModel LineManager =>
        LineManagerViewModel.Of(Organisation, LineManagerEmployeeId, LineManagerOverride);

    /// <summary>
    /// The line manager, defaulted from the parent position's sole holder where there is one.
    /// Decision D28 A.
    /// </summary>
    /// <remarks>
    /// A <em>default</em>, never a derivation: with several holders nothing is chosen for the
    /// operator, and the field is editable in every case. <c>Reports to</c> is the fact about the
    /// post; this is the fact about the person, and they are two rows because they are two things.
    /// </remarks>
    public string? LineManagerEmployeeId { get; set; }

    /// <summary>
    /// True when the structure was changed and the form is only being redrawn.
    /// </summary>
    /// <remarks>
    /// <b>The fix for a posted form that validated something nobody had asked to save.</b> Changing
    /// the structure submitted the form, which ran the save path and answered <em>"Choose a
    /// structure and a unit"</em> before anybody had chosen either. A refresh is navigation, not a
    /// write, so it skips validation and simply redraws with the new structure's choices.
    /// </remarks>
    public bool IsRefresh { get; set; }

    /// <summary>
    /// The axes an employee can actually be placed on — never a grade ladder or a reporting line.
    /// </summary>
    /// <remarks>
    /// Filtered by <c>IPositionService.GetEmployeeAxesAsync</c>, which excludes both by shape. They
    /// were always wrong to offer and became visible the moment the chooser was the only control
    /// left on the screen. ADR-0030.
    /// </remarks>
    public IReadOnlyList<StructureOptionViewModel> Structures { get; set; } = [];

    /// <summary>
    /// Whether to offer the structure chooser at all: only when there is more than one axis.
    /// </summary>
    /// <remarks>
    /// One axis is the ordinary tenant, and a drop-down with one option is a control that asks a
    /// question with one answer. The same refusal to offer a meaningless choice that the position
    /// page's own chooser makes.
    /// </remarks>
    public bool ShowsStructureChooser => Structures.Count > 1;

    /// <summary>
    /// The unit's full path, when the screen was opened from a card and already knows it.
    /// </summary>
    /// <remarks>
    /// Shown as a sentence above the picker so the reader can see what was prefilled without
    /// opening the control, and so arriving from the wrong card is obvious immediately rather than
    /// after the save.
    /// </remarks>
    public string? UnitPath { get; set; }

    /// <summary>
    /// Whether this screen was opened from a unit's card, which is what decides whether it asks for
    /// a person or for a unit first.
    /// </summary>
    public bool FromUnit => !string.IsNullOrEmpty(RecordId);

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>
/// Changing where one employee sits on one structure: the whole split, as editable rows, from one
/// stated date.
/// </summary>
/// <remarks>
/// <b>The set, not a row at a time.</b> The rules that matter — allocations totalling 100, exactly
/// one main — are about the set as a whole, so applying a matrix one placement at a time would be
/// invalid at every step but the last. That is why <c>IEmployeeAssignmentService</c> has
/// <c>ReallocateAsync</c> at all, and this screen is the control surface for it: every save here is
/// one call to that method with the same changes/split shape the <c>employee-assignments</c> recipe
/// step builds. There is no second write path.
///
/// <b>One date for the whole change.</b> Everything running when it starts is closed the day
/// before, so a transfer is two sets of rows rather than an overwrite, and nothing is deleted.
/// </remarks>
public sealed class ChangePlacementViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>The structure being changed. One screen, one structure — see the class remark.</summary>
    public string? StructureId { get; set; }

    /// <summary>ISO-8601 on the wire, always. <c>IsoDate</c> in Dimensions says why.</summary>
    public string? EffectiveFrom { get; set; }

    /// <summary>
    /// The split as it will be from <see cref="EffectiveFrom"/>. Prefilled from what is effective
    /// today, so the common edit is changing one number.
    /// </summary>
    public List<PlacementEditRowViewModel> Rows { get; set; } = [];

    public IReadOnlyList<StructureOptionViewModel> Structures { get; set; } = [];

    /// <summary>
    /// The date of the latest change already recorded on this structure, for the note that says
    /// what the effective date may not be earlier than.
    /// </summary>
    public DateOnly? LatestChange { get; set; }

    /// <summary>What they are placed at today, read-only, so the form says what it is replacing.</summary>
    public IReadOnlyList<PlacementRowViewModel> Current { get; set; } = [];

    public string Name => BilingualText.Display(NameEn, NameAr);

    /// <summary>The index of the row marked main, for the radio group. Null when none is.</summary>
    public int? MainRowIndex
    {
        get
        {
            var index = Rows.FindIndex(row => row.IsMain);

            return index < 0 ? null : index;
        }
    }
}

/// <summary>One editable row of a split.</summary>
/// <remarks>
/// <see cref="IsMain"/> is posted as the index of the chosen radio rather than as a checkbox per
/// row, because "exactly one" is what a radio group means and a set of checkboxes is a set of
/// independent answers. The controller sets this from the posted index before anything is
/// validated.
/// </remarks>
public sealed class PlacementEditRowViewModel
{
    public string? RecordId { get; set; }

    /// <summary>The unit's path as it stood when the form was drawn, for the read-only echo.</summary>
    public string? UnitPath { get; set; }

    public decimal AllocationPercent { get; set; } = 100m;

    public bool IsMain { get; set; }
}

/// <summary>Ending an employee's placements on one structure.</summary>
public sealed class EndPlacementViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string? StructureId { get; set; }

    public string? StructureName { get; set; }

    /// <summary>The last day the placement runs, inclusive.</summary>
    public string? LastDay { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>One structure, for a picker.</summary>
public sealed class StructureOptionViewModel
{
    public string StructureId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>Formatting a date for a reader, as opposed to for the wire.</summary>
/// <remarks>
/// On the wire a date is ISO-8601, because there it has to mean the same day to everybody. On
/// screen it is whatever the reader's culture says a date looks like. The same split the dimension
/// engine draws between <c>IsoDate</c> and <c>BilingualDisplay</c>; this is the screen half, kept
/// here so the views do not each invent one.
/// </remarks>
public static class EmployeeDisplay
{
    /// <summary>A date in the reader's own format.</summary>
    public static string Date(DateOnly value) => value.ToString("d", CultureInfo.CurrentCulture);

    /// <inheritdoc cref="Date(DateOnly)"/>
    public static string Date(DateOnly? value) => value is null ? "—" : Date(value.Value);

    /// <summary>A wire date in the reader's own format, for a date that arrived as a string.</summary>
    public static string DateFromIso(string? value) =>
        IsoDate.TryParse(value, out var date) ? Date(date) : value ?? string.Empty;

    /// <summary>
    /// An allocation, in the reader's own numerals, without trailing zeros nobody needs.
    /// </summary>
    /// <remarks>
    /// Whole percentages are the overwhelming case — 100, or a 60/40 split — and "100.00%" on every
    /// row of a list is noise that makes the one row reading "33.33%" harder to notice rather than
    /// easier. The current culture, so an Arabic reader sees Arabic-Indic digits like every other
    /// number on the screen.
    /// </remarks>
    public static string Percent(decimal value) =>
        value == decimal.Truncate(value)
            ? value.ToString("0'%'", CultureInfo.CurrentCulture)
            : value.ToString("0.##'%'", CultureInfo.CurrentCulture);
}

/// <summary>
/// The employee sections screen: who the record is about, and the editor for the sections on it.
/// </summary>
/// <remarks>
/// <b>It carries a shape, which no other view model in this module does.</b> The sections are
/// <c>BagPart</c>s edited by Orchard's own display drivers, and what those produce is a shape tree
/// rather than a value this module could usefully project. Flattening it into properties would mean
/// reimplementing the editor for every field type a section holds, and again for every type the form
/// designer adds in slice B3 — see <c>EmployeeSectionsAdminController</c>.
///
/// The name and the code are beside it for the reason every other screen in this module carries
/// them: a form about a person says which person, so that somebody who opened two tabs can tell
/// them apart before they type into one.
/// </remarks>
public sealed class EmployeeSectionsViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    /// <summary>Whether the reader may change what they are looking at.</summary>
    /// <remarks>
    /// An auditor holds <c>ViewEmployees</c> and not <c>ManageEmployees</c>: reading somebody's
    /// documents is their job and changing them is not. They get the screen without a Save button —
    /// not rendered rather than rendered and refused, the rule the lifecycle controls and the
    /// lookup list screens already follow. The service refuses the post either way.
    /// </remarks>
    public bool MaySave { get; set; }

    /// <summary>The editor shape for the employee's content item.</summary>
    public IShape Editor { get; set; } = default!;
}

/// <summary>
/// An increment: a new grade within the position somebody already holds. Decision D26 G.
/// </summary>
/// <remarks>
/// <b>Its own form because it is its own change.</b> A promotion or a transfer is a new position; an
/// increment is a new grade within the one they hold. One form for both would make somebody
/// re-choose a position they are not changing, and would produce history that could not tell the
/// two apart.
/// </remarks>
public sealed class ChangeGradeViewModel
{
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string? StructureId { get; set; }

    /// <summary>The position they hold, which this form does not change.</summary>
    public string? PositionRecordId { get; set; }

    public string? PositionName { get; set; }

    /// <summary>What they are on today, so the form can say what is changing from what.</summary>
    public string? CurrentGradeName { get; set; }

    /// <summary>The new grade. Limited to the position's allowed set by the control and the validator.</summary>
    public string? GradeRecordId { get; set; }

    /// <summary>
    /// The day the position they hold started — the earliest this change may be dated.
    /// </summary>
    /// <remarks>
    /// Back-dating before it would not be a change to that row: it would be a statement about a
    /// period the row does not cover, and the write would close the row on a day before it started.
    /// Refused, naming the date it would have to be on or after.
    /// </remarks>
    public DateOnly? CurrentFrom { get; set; }

    public string? EffectiveFrom { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>
/// One dated period of somebody's job: what they held, where it sat, at what grade, and when.
/// </summary>
/// <remarks>
/// <b>The organisation path is as at this row's own start date</b>, never as at today. A department
/// renamed in June must read on a March row as it read in March; resolving it as at today would
/// make the history quietly agree with the present, which is the one thing a history must not do.
/// This is the same reason <c>IDimensionService.GetAsync</c> takes a date at all.
/// </remarks>
public sealed class JobHistoryRowViewModel
{
    public string RecordId { get; set; } = string.Empty;

    /// <summary>The position, or the unit for a row written before anybody held one (ADR-0029).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Where it sat on that date, root first. Empty for a node that was not placed then.</summary>
    public string OrganisationPath { get; set; } = string.Empty;

    /// <summary>Empty where no grade was recorded, which is every row before slice F6a.</summary>
    public string GradeName { get; set; } = string.Empty;

    public DateOnly From { get; set; }

    /// <summary>Null while the row is open, which is what "and still" means on a history.</summary>
    public DateOnly? To { get; set; }
}

/// <summary>
/// Which of the line-manager cases a screen is in. Decision D31, resting on D28 A and D30.
/// </summary>
/// <remarks>
/// <b>The line manager follows the position</b>, because the reporting line already answers "who
/// does this post answer to" and asking a second, unrelated question produced an A–Z list of every
/// employee in the tenant beside a block that had just derived the answer.
///
/// <b>D28 A still decides what may be derived.</b> One holder of the post above is a fact and is
/// shown; several is not, and nothing picks between them — the operator chooses, from those
/// holders and no one else. What D31 adds is that the choice is <em>constrained</em> to them and
/// required, where D28 A left the field free and optional, with
/// <see cref="Override"/> as the deliberate, visible way out for a dotted line.
/// </remarks>
public enum LineManagerMode
{
    /// <summary>No position chosen yet, so there is nothing to follow.</summary>
    Hidden,

    /// <summary>
    /// This post is the top of the reporting line, so there is genuinely nobody above it.
    /// </summary>
    /// <remarks>
    /// <b>Split from <see cref="NobodyAbove"/>, which is the point of the D31 addendum.</b> These
    /// were one case called <c>None</c> and read <em>"Nobody — this post answers to no one on the
    /// reporting line"</em>. That is true at the top and false everywhere else: a Software Engineer
    /// under a vacant Manager Technology answers to the Director above the gap, not to no one, and
    /// the screen said otherwise.
    /// </remarks>
    TopOfLine,

    /// <summary>
    /// The post above is vacant and nothing above <em>it</em> is held either, all the way up.
    /// </summary>
    /// <remarks>
    /// The honest end of the walk: a chain of empty posts has nobody to act. Distinct from
    /// <see cref="TopOfLine"/> because the reason differs and so must the sentence — this one says
    /// the posts exist and are empty, which is something somebody can act on.
    /// </remarks>
    NobodyAbove,

    /// <summary>Exactly one holder of the post above. Shown, not chosen.</summary>
    Derived,

    /// <summary>Several holders. The operator picks one of them, and only them.</summary>
    Choose,

    /// <summary>Somebody asked for anyone at all — a dotted line, a matrix, a secondment.</summary>
    Override,

    /// <summary>
    /// A stored choice whose person has left the post it was made from. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Choose"/>, which is nobody having answered yet. Here somebody did
    /// answer, and what they answered is no longer true — so the screen names the person, says
    /// what changed, and asks again. Never silently replaced: picking another holder would be the
    /// product inventing a line manager, which is what D28 A refuses.
    /// </remarks>
    NeedsChoice,
}

/// <summary>
/// The line-manager control's whole state, worked out once so three screens cannot differ.
/// </summary>
/// <param name="DiffersFromTheReportingLine">
/// Whether the person named is somebody the reporting line does not imply. Shown on the form and
/// on the employee's page, because a line manager who is not the holder of the post above is a
/// deliberate exception and reads as an error when it is not labelled.
/// </param>
public sealed record LineManagerViewModel(
    LineManagerMode Mode,
    string PositionName,
    IReadOnlyList<PositionHolderRef> Holders,
    string? EmployeeId,
    bool DiffersFromTheReportingLine)
{
    /// <summary>The one holder, when there is exactly one. Null in every other mode.</summary>
    public PositionHolderRef? Sole => Mode == LineManagerMode.Derived && Holders.Count > 0
        ? Holders[0]
        : null;

    /// <summary>
    /// The name of the one person this control is about, or empty where it is about several or
    /// about nobody.
    /// </summary>
    /// <remarks>
    /// Read by the modes that name somebody the <em>Reports to</em> row does not — an acting
    /// manager, and an override. The one-holder case deliberately does not use it: the row names
    /// that person already, and printing them again underneath was the duplicate this property's
    /// callers were separated out to stop.
    /// </remarks>
    public string PersonName => Holders.Count == 1 ? Holders[0].Name : string.Empty;

    /// <summary>
    /// Whether the person offered holds a post <em>above</em> the vacant one. D31 addendum.
    /// </summary>
    /// <remarks>
    /// Carries the label rather than the rule: the control is the same control either way — one
    /// holder shown, several offered — and what changes is that it says <em>acting</em> and names
    /// the post they actually hold. A reader who is not told would see a manager two levels up
    /// appear with no explanation.
    /// </remarks>
    public bool IsActing { get; init; }

    /// <summary>The post an acting manager actually holds. Empty unless <see cref="IsActing"/>.</summary>
    public string ActingPositionName { get; init; } = string.Empty;

    /// <summary>
    /// The empty posts above, nearest first, when nobody above holds anything. Slice F6c.
    /// </summary>
    /// <remarks>
    /// What turns "No one above holds a post yet" from a true sentence into an actionable one. See
    /// <c>ReportsTo.VacantAbove</c>.
    /// </remarks>
    public IReadOnlyList<string> VacantAbove { get; init; } = [];

    /// <summary>
    /// The state for a screen that <em>states</em> rather than asks. D31 addendum.
    /// </summary>
    /// <remarks>
    /// <b>The stored person wins over the derived one, and that is the difference from
    /// <see cref="Of"/>.</b> A form is working out what the answer <em>should</em> be; a page is
    /// saying what it <em>is</em>. Where somebody overrode the reporting line, the page must name
    /// the person on the record — labelled as differing — rather than the holder the line implies,
    /// which would make the page contradict the record it is drawn from.
    ///
    /// The post's own state still comes from the chart, because that is a fact about the post and
    /// not about this employee: a vacant post above reads vacant whoever is acting for it.
    /// </remarks>
    public static LineManagerViewModel FromAnswer(LineManagerAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        IReadOnlyList<PositionHolderRef> named = string.IsNullOrEmpty(answer.EmployeeId)
            ? []
            : [new PositionHolderRef(answer.EmployeeId, answer.Name, string.Empty)];

        return answer.How switch
        {
            // Nothing derived and nothing stored. The post above's own state decides which
            // sentence belongs: no post at all is the top of the line; a post that exists and is
            // empty, with nothing held above it, is something somebody can act on.
            LineManagerHow.None => new LineManagerViewModel(
                string.IsNullOrEmpty(answer.PositionName)
                    ? LineManagerMode.TopOfLine
                    : LineManagerMode.NobodyAbove,
                answer.PositionName,
                [],
                null,
                false)
            {
                VacantAbove = answer.VacantAbove,
            },

            // Somebody answered, and what they answered is no longer true.
            LineManagerHow.NeedsChoice when !string.IsNullOrEmpty(answer.EmployeeId) =>
                new LineManagerViewModel(
                    LineManagerMode.NeedsChoice,
                    answer.PositionName,
                    answer.Candidates,
                    null,
                    false)
                {
                    StalePersonName = answer.Reason,
                },

            // Several hold the post and nobody has answered yet. D28 A.
            LineManagerHow.NeedsChoice => new LineManagerViewModel(
                LineManagerMode.Choose, answer.PositionName, answer.Candidates, null, false),

            // ITS OWN MODE, NOT Derived WITH A FLAG. It used to borrow Derived so the read-only
            // partial would print the name, and that conflated two opposite statements: Derived
            // means "the post above has one holder and this is them", which the Reports to row
            // already says in full. An override is the person the row does NOT name. The partial
            // prints one and stays silent about the other, which it cannot do from one mode.
            LineManagerHow.Override => new LineManagerViewModel(
                LineManagerMode.Override, answer.PositionName, named, answer.EmployeeId, true),

            // A plain stored manager on somebody with no position: no reporting line for them to
            // be an exception to, so no label. Every pre-F6 employee and every no-positions tenant.
            LineManagerHow.Stored => new LineManagerViewModel(
                LineManagerMode.Derived, string.Empty, named, answer.EmployeeId, false),

            // Derived, acting, chosen or chosen-acting: one person, named. Acting carries the post
            // they actually hold, so a manager above the gap is never unexplained.
            _ => new LineManagerViewModel(
                LineManagerMode.Derived, answer.PositionName, named, answer.EmployeeId, false)
            {
                IsActing = answer.How is LineManagerHow.Acting or LineManagerHow.ChosenActing,
                ActingPositionName = answer.How is LineManagerHow.Acting or LineManagerHow.ChosenActing
                    ? answer.PositionName
                    : string.Empty,
            },
        };
    }

    /// <summary>
    /// The person a stale choice named, for the sentence that asks again. D31 addendum 2.
    /// </summary>
    public string StalePersonName { get; init; } = string.Empty;

    /// <summary>
    /// What kind of answer this control is about to save. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// <b>The form knows something the record cannot work out later.</b> Whether the people in the
    /// picker hold the post above or are standing in for a vacant one is visible here and nowhere
    /// afterwards: once the post is filled again, a stand-in and a departed holder both read as
    /// "not a holder". So the form states which it was, and the record keeps it.
    ///
    /// Nobody named is no kind of answer, whatever the override flag says — an empty box with a
    /// flag beside it has said nothing.
    /// </remarks>
    /// <param name="storing">
    /// The id the form is actually about to save, which is not always this model's own — a derived
    /// answer names somebody here and stores nobody.
    /// </param>
    public StoredLineManagerKind KindFor(string? storing) =>
        string.IsNullOrEmpty(storing) ? StoredLineManagerKind.None
        : Mode == LineManagerMode.Override ? StoredLineManagerKind.Override
        : IsActing ? StoredLineManagerKind.ChosenActing
        : StoredLineManagerKind.Chosen;

    /// <summary>
    /// Works the state out from what the position implies and what the form was given.
    /// </summary>
    /// <remarks>
    /// <b>One place, because the Create form, the Place form and the employee page must agree.</b>
    /// Each of them had the makings of its own slightly different answer, and three readings of
    /// "who manages this person" is how a screen comes to contradict the block above it.
    /// </remarks>
    public static LineManagerViewModel Of(
        PositionOrganisation? position,
        string? lineManagerEmployeeId,
        bool overrideRequested)
    {
        if (position is null)
        {
            // Nothing chosen: the control says what it will do rather than offering a question
            // whose answer is about to be replaced.
            return new LineManagerViewModel(
                LineManagerMode.Hidden, string.Empty, [], lineManagerEmployeeId, false);
        }

        var reportsTo = position.ReportsTo;

        // WHOSE HOLDERS THE CONTROL IS ABOUT. Normally the post directly above; when that post is
        // vacant, the nearest held post above the gap, whose holders are acting (D31 addendum).
        // Everything below is the same four-way decision either way, which is the point of
        // resolving it here rather than branching the whole method twice.
        var acting = reportsTo.Kind == ReportsToKind.Vacant ? reportsTo.Acting : null;
        var holders = acting?.Holders ?? reportsTo.Holders;
        var postName = acting?.PositionName ?? reportsTo.PositionName;

        var differs = !string.IsNullOrEmpty(lineManagerEmployeeId) &&
            !holders.Any(holder => string.Equals(
                holder.EmployeeId, lineManagerEmployeeId, StringComparison.Ordinal));

        if (overrideRequested)
        {
            return new LineManagerViewModel(
                LineManagerMode.Override, postName, holders, lineManagerEmployeeId, differs)
            {
                IsActing = acting is not null,
                ActingPositionName = acting?.PositionName ?? string.Empty,
            };
        }

        // The top of the line: genuinely nobody above, which is a fact rather than a gap.
        if (reportsTo.Kind == ReportsToKind.Top)
        {
            return new LineManagerViewModel(
                LineManagerMode.TopOfLine, reportsTo.PositionName, [], null, false);
        }

        // Vacant, and nothing above it is held either. The posts exist and are empty, which is a
        // different sentence from "answers to no one" and is something somebody can act on.
        if (reportsTo.Kind == ReportsToKind.Vacant && acting is null)
        {
            return new LineManagerViewModel(
                LineManagerMode.NobodyAbove, reportsTo.PositionName, [], null, false)
            {
                // Named, not counted — slice F6c. A list of empty chairs is something to act on.
                VacantAbove = reportsTo.VacantAbove ?? [],
            };
        }

        return holders.Count == 1
            ? new LineManagerViewModel(
                LineManagerMode.Derived, postName, holders, holders[0].EmployeeId, false)
            {
                IsActing = acting is not null,
                ActingPositionName = acting?.PositionName ?? string.Empty,
            }

            // D28 A: several hold it and nothing picks between them. D31's difference is that the
            // list offered is theirs alone — and the addendum's, that "theirs" may be the acting
            // post's holders rather than the vacant post's.
            : new LineManagerViewModel(
                LineManagerMode.Choose,
                postName,
                holders,
                holders.Any(holder => string.Equals(
                    holder.EmployeeId, lineManagerEmployeeId, StringComparison.Ordinal))
                    ? lineManagerEmployeeId
                    : null,
                false)
            {
                IsActing = acting is not null,
                ActingPositionName = acting?.PositionName ?? string.Empty,
            };
    }
}

/// <summary>
/// What the derived organisation block renders: where a position sits, and who it answers to.
/// D31 addendum.
/// </summary>
/// <remarks>
/// <b>One model because it is one row.</b> <em>Reports to</em> and <em>Line manager</em> used to be
/// two rows rendered from two models, and they read as two independent facts — which invited the
/// reader to wonder why a post could report to one place and a person to another. They are one
/// fact: the post above, and whoever answers for it.
///
/// <b><see cref="Editable"/> is the only difference between the four screens that render this.</b>
/// The Create and Place forms let somebody choose where there is a choice; the employee page and
/// the Placement screen state what is true. Everything else — the wording, the four cases, the
/// acting label — is identical, which is the whole reason it is one partial.
/// </remarks>
public sealed record JobAndOrganisationViewModel(
    PositionOrganisation? Position,
    LineManagerViewModel? LineManager,
    bool Editable)
{
    /// <summary>The read-only form, for the employee page and the Placement screen.</summary>
    public static JobAndOrganisationViewModel ReadOnly(
        PositionOrganisation? position,
        LineManagerViewModel? lineManager) => new(position, lineManager, Editable: false);

    /// <summary>
    /// Whether the <em>Reports to</em> row already names the person the control is about.
    /// </summary>
    /// <remarks>
    /// <b>The duplicate, and why the test is not simply "is it derived".</b> With one holder the
    /// row reads <em>"Director Technology — Ali Tahir (line manager)"</em>, so the control printing
    /// "Ali Tahir" underneath made one person look like two. But the control's <em>Derived</em>
    /// mode covers more than that one case: a person chosen among several holders is shown the same
    /// way, and there the row reads <em>"Site Supervisor (3 holders)"</em> and names nobody —
    /// suppressing the control's text would hide the line manager altogether.
    ///
    /// So the question is not what mode the control is in. It is whether the row above has already
    /// said this, which is true only when the post above has exactly one holder and that holder is
    /// the answer. An acting manager fails it because the row reads <em>"(vacant)"</em>, and an
    /// override fails it because an override is by definition not the holder.
    /// </remarks>
    public bool RowAlreadyNamesThePerson =>
        Position?.ReportsTo is { Kind: ReportsToKind.OneHolder, Holders.Count: 1 } reportsTo &&
        LineManager?.EmployeeId is { Length: > 0 } person &&
        string.Equals(reportsTo.Holders[0].EmployeeId, person, StringComparison.Ordinal);
}
