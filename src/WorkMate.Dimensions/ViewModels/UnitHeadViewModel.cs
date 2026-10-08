namespace WorkMate.Dimensions.ViewModels;

/// <summary>
/// Appointing or clearing one unit's head, and the terms already on record for it.
/// </summary>
/// <remarks>
/// Every bilingual half and every posted value is <c>string?</c>, like every other view model in
/// this module: an empty text box binds to <c>null</c> rather than <c>""</c>, and a non-nullable
/// reference property gets an implicit <c>required</c> in model state that would refuse the post
/// before the controller ran. ADR-0003's addendum, guarded by
/// <c>ViewModelsSurviveNullBindingTests</c>.
/// </remarks>
public sealed class UnitHeadViewModel
{
    public string? StructureId { get; set; }

    public string? StructureCode { get; set; }

    public string? StructureNameEn { get; set; }

    public string? StructureNameAr { get; set; }

    public string? RecordId { get; set; }

    public string? UnitCode { get; set; }

    public string? UnitNameEn { get; set; }

    public string? UnitNameAr { get; set; }

    /// <summary>The date the designer was being viewed as at, so the screen returns to it.</summary>
    public string? AsAt { get; set; }

    /// <summary>Who heads the unit on <see cref="AsAt"/>, or null when the post is vacant.</summary>
    public string? CurrentHeadName { get; set; }

    public DateOnly? CurrentHeadFrom { get; set; }

    /// <summary>Who is being appointed.</summary>
    public string? EmployeeId { get; set; }

    /// <summary>
    /// The first day of the new term, ISO-8601. Whatever it displaces closes the day before.
    /// </summary>
    public string? EffectiveFrom { get; set; }

    /// <summary>The last day of the current term, ISO-8601, when the post is being vacated.</summary>
    public string? LastDay { get; set; }

    /// <summary>
    /// Every term on record for this unit, earliest first.
    /// </summary>
    /// <remarks>
    /// Shown because "who led this unit last March" is the question dating an appointment exists to
    /// answer, and the person about to change it is the person most likely to want it answered.
    /// </remarks>
    public IReadOnlyList<UnitHeadTermViewModel> Terms { get; set; } = [];

    public string UnitName => BilingualDisplay.Name(UnitNameEn, UnitNameAr);

    public string StructureName => BilingualDisplay.Name(StructureNameEn, StructureNameAr);

    public bool IsVacant => string.IsNullOrEmpty(CurrentHeadName);
}

/// <summary>One period during which one person led the unit.</summary>
public sealed class UnitHeadTermViewModel
{
    public string? EmployeeName { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    public bool IsOpenEnded => To is null;
}
