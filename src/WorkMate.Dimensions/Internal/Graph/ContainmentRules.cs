using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Internal.Graph;

/// <summary>
/// The one place that decides whether a child of one type may sit under a parent of another.
/// </summary>
/// <remarks>
/// It exists because the rule used to be written twice — once backwards in the validator, deciding
/// whether a placement is refused, and once forwards in <c>GetPermittedChildTypeIdsAsync</c>,
/// deciding what a picker offers — and the two disagreed. The forwards copy ignored
/// <c>IsStrict</c> altogether, so on a non-strict axis the validator accepted placements the picker
/// would never offer: exactly the case that flag exists for. ADR-0010 makes both call this, and a
/// property test asserts they agree over every ordered pair of a structure's types.
///
/// The structure's map is the whole answer, the diagonal included. A dimension type used to hold a
/// veto over nesting inside itself, which no axis could grant past; ADR-0010's addendum removed it,
/// because "may a Department contain a Department" is an axis-specific question for the same reason
/// "may a Division contain a Department" is. See the addendum for what that cost on the screen.
/// </remarks>
internal static class ContainmentRules
{
    /// <summary>What the rules say about one ordered pair of types on one axis.</summary>
    internal enum Outcome
    {
        /// <summary>The axis declares the pairing.</summary>
        Permitted,

        /// <summary>The axis declares no such pairing. Refused when strict, advisory when not.</summary>
        NotDeclared,

        /// <summary>
        /// Parent and child are the same type and this axis does not declare the pairing. An
        /// undeclared pair like any other — refused when strict, advisory when not — reported
        /// separately only so the message can name what is actually missing.
        /// </summary>
        SelfNestingNotDeclared,
    }

    /// <summary>Decides one pairing.</summary>
    public static Outcome Decide(
        StructureDocument structure,
        string parentDimensionTypeId,
        string childDimensionTypeId)
    {
        ArgumentNullException.ThrowIfNull(structure);

        if (structure.Permits(parentDimensionTypeId, childDimensionTypeId))
        {
            return Outcome.Permitted;
        }

        return string.Equals(parentDimensionTypeId, childDimensionTypeId, StringComparison.Ordinal)
            ? Outcome.SelfNestingNotDeclared
            : Outcome.NotDeclared;
    }

    /// <summary>
    /// Whether an outcome stops the write. An undeclared pairing does so only on a strict axis,
    /// where it is refused rather than warned about.
    /// </summary>
    public static bool Blocks(Outcome outcome, bool isStrict) => outcome switch
    {
        Outcome.Permitted => false,
        _ => isStrict,
    };

    /// <summary>
    /// Whether this pairing may be offered by a picker: the forwards reading of
    /// <see cref="Decide"/>, which is the same question as "would the write be refused".
    /// </summary>
    public static bool Offerable(
        StructureDocument structure,
        string parentDimensionTypeId,
        string childDimensionTypeId)
    {
        ArgumentNullException.ThrowIfNull(structure);

        return !Blocks(
            Decide(structure, parentDimensionTypeId, childDimensionTypeId),
            structure.IsStrict);
    }
}
