using Microsoft.Extensions.Localization;
using WorkMate.Dimensions.ViewModels;

namespace WorkMate.Records.Services;

/// <summary>
/// What each broken rule says to the reader, in their own language.
/// </summary>
/// <remarks>
/// Separate from <see cref="PlacementSplit"/> so the rules stay a pure function with no localiser
/// in them, and so every case is a unit test rather than a tenant test — the same split
/// <c>EmployeeStatusNames</c> draws between a status and the word for it.
///
/// A service rather than a static method with a localiser parameter, because
/// <c>LocalisationResourceTests</c> finds a localised string by the name of the thing it is indexed
/// on: a field called <c>S</c> is what it looks for, and a string it cannot see is a string with no
/// Arabic and nothing to report it.
/// </remarks>
public sealed class PlacementSplitMessages
{
    private readonly IStringLocalizer S;

    public PlacementSplitMessages(IStringLocalizer<PlacementSplitMessages> stringLocalizer) =>
        S = stringLocalizer;

    /// <summary>The message for one violation. Never an enum member in a sentence.</summary>
    public string Describe(PlacementSplitViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        var arguments = violation.Arguments ?? [];

        return violation.Rule switch
        {
            PlacementSplitRule.UnitMissing =>
                S["Choose a unit for this row, or remove it."],

            PlacementSplitRule.DuplicateUnit =>
                S["This unit is already on another row. One unit, one row: add the two shares together instead."],

            PlacementSplitRule.AllocationNotPositive =>
                S["An allocation must be greater than zero. Remove the row instead."],

            PlacementSplitRule.AllocationTotal =>
                S["The allocations must total 100%. These total {0}%.", Argument(arguments, 0)],

            PlacementSplitRule.MainCount =>
                S["Exactly one row must be the main placement, so that \"where do they work\" has one answer. {0} are marked main.",
                    Argument(arguments, 0)],

            PlacementSplitRule.NoRows =>
                S["A placement needs at least one unit. To take this person off the structure entirely, use \"End here\" instead, which asks for a last day."],

            PlacementSplitRule.EffectiveDateMissing =>
                S["An effective date is needed. Nothing here is written without one."],

            PlacementSplitRule.EffectiveDateBeforeLatestChange =>
                S["This structure already has a placement change recorded from {0}. Saving a change dated before it would replace that one rather than slot in behind it, so it is refused. Use a date on or after {0}.",
                    Date(arguments, 0)],

            _ => S["This placement cannot be saved."],
        };
    }

    /// <summary>
    /// The argument at this position, or an empty string.
    /// </summary>
    /// <remarks>
    /// Never null. A missing argument leaves a gap in a sentence, which is a presentation defect;
    /// a null one throws inside the localiser, which takes down the screen that was about to
    /// explain what the reader got wrong.
    /// </remarks>
    private static object Argument(IReadOnlyList<object?> arguments, int index) =>
        (index < arguments.Count ? arguments[index] : null) ?? string.Empty;

    /// <summary>
    /// A date argument in the reader's own format. ISO-8601 belongs on the wire; a sentence a
    /// person reads gets their calendar's version of the same day.
    /// </summary>
    private static object Date(IReadOnlyList<object?> arguments, int index) =>
        Argument(arguments, index) is DateOnly date ? BilingualDisplay.Date(date) : Argument(arguments, index);
}
