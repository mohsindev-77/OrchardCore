using System.Globalization;
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
    public string EmployeeId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>The date being shown, ISO-8601. Placement is always a question about a date.</summary>
    public string? AsAt { get; set; }

    public IReadOnlyList<AxisPlacementViewModel> Axes { get; set; } = [];

    public IReadOnlyList<HeadshipViewModel> Headships { get; set; } = [];

    public bool CanAssign { get; set; }

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

    public IReadOnlyList<StructureOptionViewModel> Structures { get; set; } = [];

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
