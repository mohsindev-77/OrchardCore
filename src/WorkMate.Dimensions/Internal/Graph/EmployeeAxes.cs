namespace WorkMate.Dimensions.Internal.Graph;

/// <summary>
/// The one test for "is this structure somewhere an employee goes". Decision D30.
/// </summary>
/// <remarks>
/// <b>Three callers had to agree and did not.</b> <c>PositionService.GetEmployeeAxesAsync</c> knew
/// the rule and used it to decide which axes the placement screens offer;
/// <c>PositionService.PermitsEmployeesAtAsync</c> — which the organisation designer's card menu
/// reads — did not, and <c>DimensionValidator</c> did not either. So the designer offered
/// <em>Place an employee here…</em> on a position card of the <b>reporting line</b>, and the old
/// Place screen opened with an allocation percentage and a <em>main placement</em> checkbox for a
/// structure that is not a place at all. The write would have gone through.
///
/// <b>Asked of the axis's shape, never of its code or its name.</b> A tenant may rename anything
/// and the demo tenants prefix everything, so a rule written against <c>"reporting"</c> would hold
/// on the demo data and nowhere else. The shape is decisive on its own: a structure whose every
/// level is a Position is a reporting line (ADR-0028 — a position's second parent axis, which no
/// assignment is ever written on), and one whose every level is a Grade is a grade ladder (D26 C —
/// a grade is a reference, and no employee rolls up to one).
///
/// <b>An ordinary organisation axis is untouched</b>, including a position-based one: its levels
/// are divisions and departments <em>and</em> positions, so neither test fires. A tenant with no
/// positions at all is likewise unaffected, which is D30's own scope.
/// </remarks>
internal static class EmployeeAxes
{
    /// <summary>
    /// Whether <paramref name="levels"/> describe a structure employees may be placed on at all.
    /// </summary>
    /// <param name="levels">The structure's dimension type ids, in order. Empty holds nothing.</param>
    /// <param name="positionTypeIds">Every type in the Position role, in this tenant.</param>
    /// <param name="gradeTypeIds">Every type in the Grade role, in this tenant.</param>
    public static bool HoldsEmployees(
        IReadOnlyCollection<string> levels,
        IReadOnlySet<string> positionTypeIds,
        IReadOnlySet<string> gradeTypeIds)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(positionTypeIds);
        ArgumentNullException.ThrowIfNull(gradeTypeIds);

        // An axis with no levels holds nothing. The base recipe ships exactly one — the empty
        // primary organisation a tenant fills in — and offering it would be offering somewhere
        // with no units.
        if (levels.Count == 0)
        {
            return false;
        }

        // A reporting line: positions all the way down. ADR-0028 makes it a position's SECOND
        // parent axis — what a post answers to — and nothing ever writes an assignment on it.
        if (levels.All(positionTypeIds.Contains))
        {
            return false;
        }

        // A grade ladder: grades all the way down. D26 C — a grade is a reference carried on the
        // assignment row (ADR-0026), not a node anybody is placed at, and an assignment here would
        // roll employees up under grades in the closure index.
        return !levels.All(gradeTypeIds.Contains);
    }
}
