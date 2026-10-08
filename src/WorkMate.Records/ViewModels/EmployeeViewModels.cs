using System.Globalization;
using WorkMate.Core;
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

    /// <summary>Where the primary placement puts them today, or null when they are not placed.</summary>
    /// <remarks>
    /// Read through <c>IEmployeeAssignmentService</c> like everything else about placement, and
    /// shown on the list because "which department" is the first thing anybody scanning a list of
    /// people wants to know — and the one thing the employee record deliberately does not hold.
    /// </remarks>
    public string? PrimaryUnitName { get; set; }

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

/// <summary>Placing an employee at a unit, or changing where they sit, from a stated date.</summary>
public sealed class PlaceEmployeeViewModel
{
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

    /// <summary>The units the chosen axis says may hold employees, on the chosen date.</summary>
    public IReadOnlyList<UnitOptionViewModel> Units { get; set; } = [];

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>Ending an employee's placements on one axis.</summary>
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

/// <summary>One axis, for a picker.</summary>
public sealed class StructureOptionViewModel
{
    public string StructureId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    public string Name => BilingualText.Display(NameEn, NameAr);
}

/// <summary>One unit, for a picker.</summary>
public sealed class UnitOptionViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? NameEn { get; set; }

    public string? NameAr { get; set; }

    /// <summary>
    /// Whether this axis says a unit of this type may hold employees.
    /// </summary>
    /// <remarks>
    /// The picker offers only units that may, so this is normally true. It is carried so the view
    /// can say why a list is short rather than leaving a reader wondering where their divisions
    /// went.
    /// </remarks>
    public bool HoldsEmployees { get; set; } = true;

    public string Name => BilingualText.Display(NameEn, NameAr);

    public static UnitOptionViewModel Of(DimensionNodeRef node, bool holdsEmployees = true)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new UnitOptionViewModel
        {
            RecordId = node.RecordId,
            Code = node.Code,
            NameEn = node.NameEn,
            NameAr = node.NameAr,
            HoldsEmployees = holdsEmployees,
        };
    }
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
}
