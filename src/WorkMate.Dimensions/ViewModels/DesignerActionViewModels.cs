using Microsoft.AspNetCore.Mvc.ModelBinding;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// What every designer action form carries: where it came back to, and the date it takes effect.
/// </summary>
/// <remarks>
/// The effective date is a string, not a <see cref="DateOnly"/>. Model binding a date parses it
/// with the request's culture, and these forms render under Arabic as well as English — the same
/// hazard <see cref="Internal.IsoDate"/> exists for. An <c>&lt;input type="date"&gt;</c> always
/// posts <c>yyyy-MM-dd</c> whatever it displays, so the string is the true wire value and parsing
/// it invariantly is the only reading that means the same day for every user.
/// </remarks>
public abstract class DesignerActionViewModel
{
    public string StructureId { get; set; } = string.Empty;

    /// <summary>The effective date, ISO-8601. Defaults to today when the form is first shown.</summary>
    public string EffectiveFrom { get; set; } = string.Empty;

    /// <summary>The date the tree behind the form is being shown as at, so returning keeps it.</summary>
    [BindNever]
    public string AsAt { get; set; } = string.Empty;

    [BindNever]
    public string StructureNameEn { get; set; } = string.Empty;

    [BindNever]
    public string StructureNameAr { get; set; } = string.Empty;
}

/// <summary>Adding a unit: which type, under which parent, called what, from when.</summary>
public sealed class AddUnitViewModel : DesignerActionViewModel
{
    /// <summary>The unit it will sit under, or null for a root of the structure.</summary>
    public string? ParentRecordId { get; set; }

    public string DimensionTypeId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>The custom attributes the chosen dimension type declares, in schema order.</summary>
    public List<UnitAttributeInputViewModel> Attributes { get; set; } = [];

    [BindNever]
    public string ParentNameEn { get; set; } = string.Empty;

    [BindNever]
    public string ParentNameAr { get; set; } = string.Empty;

    /// <summary>
    /// The types that may go under this parent. One means no choice to make; several means the
    /// structure allows skipping a level or the type nests inside itself.
    /// </summary>
    [BindNever]
    public IReadOnlyList<DimensionTypeChoiceViewModel> PermittedTypes { get; set; } = [];

    public BilingualText Name => new(NameEn?.Trim() ?? string.Empty, NameAr?.Trim() ?? string.Empty);

    public IReadOnlyList<DimensionAttributeValue> ToAttributeValues() =>
        [.. Attributes.Select(attribute => new DimensionAttributeValue(
            attribute.Name, attribute.Value, attribute.ValueAr))];
}

/// <summary>One dimension type offered on the add-unit form.</summary>
public sealed record DimensionTypeChoiceViewModel(string DimensionTypeId, string NameEn, string Code);

/// <summary>
/// One custom attribute of the chosen dimension type, as an input on the add-unit form.
/// </summary>
/// <remarks>
/// The label and kind come back on the post as well as going out on the get, because a form that
/// fails validation has to be redrawn exactly as it was and the schema is not re-read to do it.
/// They are never trusted on the way in: the service checks every submitted name against the
/// type's real schema and rejects anything it does not declare.
/// </remarks>
public sealed class UnitAttributeInputViewModel
{
    public string Name { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;

    public string LabelAr { get; set; } = string.Empty;

    public DimensionAttributeKind Kind { get; set; }

    public bool IsRequired { get; set; }

    public string? Value { get; set; }

    public string? ValueAr { get; set; }

    public static UnitAttributeInputViewModel Of(DimensionAttributeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new UnitAttributeInputViewModel
        {
            Name = definition.Name,
            LabelEn = definition.Label.En,
            LabelAr = definition.Label.Ar,
            Kind = definition.Kind,
            IsRequired = definition.IsRequired,
        };
    }
}

/// <summary>Which of the two renames the architecture distinguishes the user means.</summary>
public enum RenameKind
{
    /// <summary>
    /// The unit genuinely became something else on a date. Prior periods keep the old name, so
    /// last year's reports still read as they did.
    /// </summary>
    Substantive,

    /// <summary>
    /// The old text was a mistake. Applies retrospectively to the name in effect on the date, so
    /// last year's reports stop showing the typo.
    /// </summary>
    Corrective,
}

/// <summary>Renaming a unit, having been asked which kind of rename it is.</summary>
/// <remarks>
/// Architecture section 6: "A rename can be corrective or substantive, and these need different
/// answers … the designer asks the user which they mean rather than guessing." There is no
/// default that is right often enough to pick silently — guessing substantive leaves a typo in
/// every historical report, and guessing corrective quietly rewrites history.
/// </remarks>
public sealed class RenameUnitViewModel : DesignerActionViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public RenameKind Kind { get; set; } = RenameKind.Substantive;

    [BindNever]
    public string Code { get; set; } = string.Empty;

    [BindNever]
    public string CurrentNameEn { get; set; } = string.Empty;

    [BindNever]
    public string CurrentNameAr { get; set; } = string.Empty;

    public BilingualText Name => new(NameEn?.Trim() ?? string.Empty, NameAr?.Trim() ?? string.Empty);
}

/// <summary>
/// Retiring a unit: the assessment first, then the confirmation, in the dry-run-then-confirm shape
/// a level change, a move and a merge all use.
/// </summary>
public sealed class RetireUnitViewModel : DesignerActionViewModel
{
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// Set by the confirmation screen's own button. Absent on the first post, which is what makes
    /// the plan show before anything happens.
    /// </summary>
    public bool Confirmed { get; set; }

    [BindNever]
    public string Code { get; set; } = string.Empty;

    [BindNever]
    public string NameEn { get; set; } = string.Empty;

    [BindNever]
    public string NameAr { get; set; } = string.Empty;

    /// <summary>What the retirement would do. Null until it has been assessed.</summary>
    [BindNever]
    public RetirePlan? Plan { get; set; }

    /// <summary>
    /// What should happen to the units underneath, one answer per structure this unit is a parent
    /// on. Unanswered until the person retiring says, which is what stops the retirement going
    /// ahead while children are still attached — on any structure, not only the one on screen.
    /// </summary>
    public List<StructureDispositionViewModel> Dispositions { get; set; } = [];

    /// <summary>
    /// Whether this viewer may move units at all — specification section 4's
    /// <c>MoveDimensionRecords</c>. Reparenting children is still reparenting, so the option is
    /// not offered to somebody who could not do it directly.
    /// </summary>
    [BindNever]
    public bool CanMoveChildren { get; set; }

    /// <summary>The answers as the service wants them: keyed by structure.</summary>
    public IReadOnlyDictionary<string, ChildrenDisposition> ToDispositions() =>
        Dispositions
            .Where(entry => entry.Kind is not null && !string.IsNullOrEmpty(entry.StructureId))
            .ToDictionary(
                entry => entry.StructureId,
                entry => entry.Kind switch
                {
                    ChildrenDispositionKind.MoveToParent => ChildrenDisposition.MoveTo(
                        string.IsNullOrWhiteSpace(entry.NewParentRecordId) ? null : entry.NewParentRecordId),
                    ChildrenDispositionKind.RetireCascade => ChildrenDisposition.Cascade,
                    _ => ChildrenDisposition.Unplaced,
                },
                StringComparer.Ordinal);
}

/// <summary>
/// Moving a unit: where it goes, from when, previewed before it happens.
/// </summary>
/// <remarks>
/// The same screen behind both ways of starting a move — dragging a card onto another and picking
/// from the keyboard list — so the preview, the warnings and the confirmation are identical either
/// way. A drag that committed straight away would be the one mutation on this designer with no
/// preview, and the one most easily done by accident.
/// </remarks>
public sealed class MoveUnitViewModel : DesignerActionViewModel
{
    public string RecordId { get; set; } = string.Empty;

    /// <summary>The unit it would sit under, or empty for the top of the structure.</summary>
    public string? NewParentRecordId { get; set; }

    /// <summary>Set by the confirmation, absent on the post that only asks for the preview.</summary>
    public bool Confirmed { get; set; }

    [BindNever]
    public string Code { get; set; } = string.Empty;

    [BindNever]
    public string NameEn { get; set; } = string.Empty;

    [BindNever]
    public string NameAr { get; set; } = string.Empty;

    /// <summary>Everywhere this unit could go, by the structure's own level rules.</summary>
    [BindNever]
    public IReadOnlyList<DimensionNodeRef> Targets { get; set; } = [];

    /// <summary>What the move would do. Null until it has been planned.</summary>
    [BindNever]
    public MovePlan? Plan { get; set; }
}

/// <summary>Merging one unit into another, previewed before it happens.</summary>
public sealed class MergeUnitViewModel : DesignerActionViewModel
{
    /// <summary>The unit being folded in, and retired by the merge.</summary>
    public string SourceRecordId { get; set; } = string.Empty;

    /// <summary>The unit it is folded into.</summary>
    public string TargetRecordId { get; set; } = string.Empty;

    public bool Confirmed { get; set; }

    [BindNever]
    public string SourceNameEn { get; set; } = string.Empty;

    [BindNever]
    public string SourceNameAr { get; set; } = string.Empty;

    [BindNever]
    public string SourceCode { get; set; } = string.Empty;

    [BindNever]
    public IReadOnlyList<DimensionNodeRef> Targets { get; set; } = [];

    /// <summary>The target's name once one has been chosen, for the preview to read naturally.</summary>
    [BindNever]
    public string TargetNameEn { get; set; } = string.Empty;

    [BindNever]
    public string TargetNameAr { get; set; } = string.Empty;

    /// <summary>The children the merge would carry across, named rather than counted.</summary>
    [BindNever]
    public IReadOnlyList<DimensionNodeRef> ChildrenMoving { get; set; } = [];

    [BindNever]
    public MergePlan? Plan { get; set; }
}

/// <summary>
/// Undoing a recorded move: which one, and why.
/// </summary>
/// <remarks>
/// The reason is mandatory and goes on the audit entry. Cancelling is the one operation in this
/// module that removes a recorded fact rather than dating it, so it is never allowed to happen
/// without somebody saying what it was — see ADR-0005's addendum, which leans on exactly that to
/// justify letting a backdated move split rather than overwrite.
/// </remarks>
public sealed class CancelMoveViewModel : DesignerActionViewModel
{
    public string RecordId { get; set; } = string.Empty;

    /// <summary>Why. Recorded against the audit entry, and refused when blank.</summary>
    public string Reason { get; set; } = string.Empty;

    public bool Confirmed { get; set; }

    [BindNever]
    public string NameEn { get; set; } = string.Empty;

    [BindNever]
    public string NameAr { get; set; } = string.Empty;

    [BindNever]
    public string Code { get; set; } = string.Empty;

    /// <summary>The moves on record for this unit, newest first, for the picker.</summary>
    [BindNever]
    public IReadOnlyList<RecordedMoveViewModel> RecordedMoves { get; set; } = [];

    [BindNever]
    public CancelMovePlan? Plan { get; set; }

    /// <summary>What the move being cancelled did, so the confirmation reads as a sentence.</summary>
    [BindNever]
    public string CancelledParentName { get; set; } = string.Empty;

    [BindNever]
    public string CancelledParentNameAr { get; set; } = string.Empty;

    /// <summary>What it would be restored to, or empty for the top of the structure.</summary>
    [BindNever]
    public string RestoredParentName { get; set; } = string.Empty;

    [BindNever]
    public string RestoredParentNameAr { get; set; } = string.Empty;
}

/// <summary>
/// One decision on a unit's record, as an option on the cancel screen. <paramref name="LeftTheTree"/>
/// marks the one that took it off the chart rather than putting it somewhere.
/// </summary>
public sealed record RecordedMoveViewModel(
    string EffectiveFrom, string ParentNameEn, string ParentNameAr, string ParentCode, bool LeftTheTree = false);

/// <summary>One structure's answer to the children question on the retire screen.</summary>
public sealed class StructureDispositionViewModel
{
    public string StructureId { get; set; } = string.Empty;

    /// <summary>Unset until the person retiring chooses, which is what the refusal keys off.</summary>
    public ChildrenDispositionKind? Kind { get; set; }

    /// <summary>
    /// Where the children go when <see cref="Kind"/> is
    /// <see cref="ChildrenDispositionKind.MoveToParent"/>. Empty means the top of the structure.
    /// </summary>
    public string? NewParentRecordId { get; set; }
}
