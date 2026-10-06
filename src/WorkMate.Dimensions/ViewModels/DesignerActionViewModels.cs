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
    /// What should happen to the units underneath. Empty until the person retiring says, which is
    /// what stops the retirement going ahead while children are still attached.
    /// </summary>
    public ChildrenDispositionKind? ChildrenDisposition { get; set; }

    /// <summary>
    /// Where the children go when <see cref="ChildrenDisposition"/> is
    /// <see cref="ChildrenDispositionKind.MoveToParent"/>. Empty means the top of the structure.
    /// </summary>
    public string? NewParentRecordId { get; set; }

    /// <summary>
    /// Whether this viewer may move units at all — specification section 4's
    /// <c>MoveDimensionRecords</c>. Reparenting children is still reparenting, so the option is
    /// not offered to somebody who could not do it directly.
    /// </summary>
    [BindNever]
    public bool CanMoveChildren { get; set; }
}
