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
        /// Parent and child are the same type and that type forbids nesting inside itself. Always
        /// refused: the type holds a veto, and no axis can grant past it.
        /// </summary>
        SelfNestingVetoedByType,

        /// <summary>
        /// Parent and child are the same type, the type permits self-nesting, and this axis does
        /// not declare it. Refused when strict, advisory when not — an undeclared pair like any
        /// other, reported separately only so the message can say which of the two was missing.
        /// </summary>
        SelfNestingNotDeclared,
    }

    /// <summary>
    /// Decides one pairing. <paramref name="childTypeAllowsSelfNesting"/> is the child type's own
    /// flag and is read only when the two types are the same.
    /// </summary>
    public static Outcome Decide(
        StructureDocument structure,
        string parentDimensionTypeId,
        string childDimensionTypeId,
        bool childTypeAllowsSelfNesting)
    {
        ArgumentNullException.ThrowIfNull(structure);

        var sameType = string.Equals(parentDimensionTypeId, childDimensionTypeId, StringComparison.Ordinal);

        if (sameType && !childTypeAllowsSelfNesting)
        {
            return Outcome.SelfNestingVetoedByType;
        }

        if (structure.Permits(parentDimensionTypeId, childDimensionTypeId))
        {
            return Outcome.Permitted;
        }

        return sameType ? Outcome.SelfNestingNotDeclared : Outcome.NotDeclared;
    }

    /// <summary>
    /// Whether an outcome stops the write. The type's veto always does; everything else does only
    /// on a strict axis, where it is refused rather than warned about.
    /// </summary>
    public static bool Blocks(Outcome outcome, bool isStrict) => outcome switch
    {
        Outcome.Permitted => false,
        Outcome.SelfNestingVetoedByType => true,
        _ => isStrict,
    };

    /// <summary>
    /// Whether this pairing may be offered by a picker: the forwards reading of
    /// <see cref="Decide"/>, which is the same question as "would the write be refused".
    /// </summary>
    public static bool Offerable(
        StructureDocument structure,
        string parentDimensionTypeId,
        string childDimensionTypeId,
        bool childTypeAllowsSelfNesting)
    {
        ArgumentNullException.ThrowIfNull(structure);

        return !Blocks(
            Decide(structure, parentDimensionTypeId, childDimensionTypeId, childTypeAllowsSelfNesting),
            structure.IsStrict);
    }
}
