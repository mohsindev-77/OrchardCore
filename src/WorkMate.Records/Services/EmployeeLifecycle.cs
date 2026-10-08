using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// Which lifecycle moves are possible, declared once.
/// </summary>
/// <remarks>
/// A table rather than a condition inside each transition method, so that the screen that decides
/// which buttons to offer and the service that decides whether to accept a post are reading the
/// same thing. Two copies of this would disagree the first time a state was added, and the
/// disagreement would show up as a button that does nothing.
///
/// Separate from the service so a unit test can enumerate every ordered pair without a tenant;
/// <c>EmploymentStatusTransitionTests</c> does exactly that.
/// </remarks>
public static class EmployeeLifecycle
{
    private static readonly Dictionary<EmploymentStatus, IReadOnlySet<EmploymentStatus>> Permitted = new()
    {
        // Hired and not started: they start, or they never do and they leave without ever having
        // been active. The second is why Exited is reachable from here.
        [EmploymentStatus.Prospective] = Set(EmploymentStatus.Active, EmploymentStatus.Exited),

        [EmploymentStatus.Active] = Set(
            EmploymentStatus.OnLeave, EmploymentStatus.Suspended, EmploymentStatus.Exited),

        // Back to work, or straight to suspended — an investigation does not wait for somebody to
        // return from maternity leave — or they resign while away, which is common.
        [EmploymentStatus.OnLeave] = Set(
            EmploymentStatus.Active, EmploymentStatus.Suspended, EmploymentStatus.Exited),

        // No route from Suspended to OnLeave. A suspension is lifted before anything else happens,
        // and a transition that skipped that would leave no record of the suspension having ended.
        [EmploymentStatus.Suspended] = Set(EmploymentStatus.Active, EmploymentStatus.Exited),

        // Reinstatement, and nothing else. A leaver cannot go straight to suspended or on leave:
        // both describe an employment that is running, and theirs is not until somebody says so.
        [EmploymentStatus.Exited] = Set(EmploymentStatus.Active),
    };

    /// <summary>Whether an employee may move from one state to another.</summary>
    /// <remarks>
    /// A move to the state already held is <em>not</em> permitted. Re-activating somebody who is
    /// already active is either a mistake or a re-dating of the state they are in, and neither is
    /// something to accept silently: accepting it would overwrite the date the current state began,
    /// which is the date leave accrual and probation are counted from.
    /// </remarks>
    public static bool Permits(EmploymentStatus from, EmploymentStatus to) =>
        Permitted.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Every state reachable from this one, for a screen deciding what to offer.</summary>
    public static IReadOnlySet<EmploymentStatus> From(EmploymentStatus status) =>
        Permitted.TryGetValue(status, out var allowed) ? allowed : Set();

    /// <summary>
    /// What a screen should offer an employee in this state: the transitions that are valid, named
    /// by the action that performs each one.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists, and why it is here rather than on a view model.</b> <see cref="Permits"/>
    /// answers in terms of the state being moved <em>to</em>; a screen needs the action that gets
    /// there, and the two are not the same mapping. <see cref="EmploymentStatus.Active"/> is
    /// reached by <c>Activate</c> from three states and by <c>Reinstate</c> from
    /// <see cref="EmploymentStatus.Exited"/>, and offering a leaver a button marked "Activate"
    /// would describe a rehire as something else.
    ///
    /// Session A2 shipped all five actions with no control that reached four of them: every screen
    /// decided for itself what to show, and nothing checked that between them they covered the
    /// table. One list, read by the editor, by each list row and by the test that asserts the
    /// coverage, is what makes that impossible rather than unlikely.
    ///
    /// Ordered the way somebody works through them: the ordinary move first, the exceptional ones
    /// next, ending employment last — because a destructive action at the top of a menu is one
    /// somebody eventually clicks by reflex.
    /// </remarks>
    public static IReadOnlyList<EmployeeTransition> ActionsFrom(EmploymentStatus status) =>
    [
        .. AllTransitions.Where(transition =>
            Permits(status, transition.To) &&
            (transition.OnlyFrom is null || transition.OnlyFrom == status) &&
            transition.NotFrom != status),
    ];

    /// <summary>Every transition this product has, each naming the action that performs it.</summary>
    /// <remarks>
    /// <see cref="EmploymentStatus.Active"/> appears twice, which is the point of the two
    /// qualifiers: one destination, two actions, and which applies depends on where the employee is
    /// coming from.
    /// </remarks>
    private static readonly IReadOnlyList<EmployeeTransition> AllTransitions =
    [
        new(EmploymentStatus.Active, "Activate", NotFrom: EmploymentStatus.Exited),
        new(EmploymentStatus.Active, "Reinstate", OnlyFrom: EmploymentStatus.Exited),
        new(EmploymentStatus.OnLeave, "PutOnLeave"),
        new(EmploymentStatus.Suspended, "Suspend"),
        new(EmploymentStatus.Exited, "Exit"),
    ];

    private static HashSet<EmploymentStatus> Set(params EmploymentStatus[] statuses) => [.. statuses];
}

/// <summary>
/// One lifecycle move a screen can offer: where it goes, and the action that takes it there.
/// </summary>
/// <param name="Action">
/// The controller action's name on <c>EmployeeLifecycleAdminController</c>. A screen builds its
/// link from this, so a transition that exists in the table and not on the controller is a broken
/// link rather than a missing button — which is the louder of the two failures, and the one a test
/// catches.
/// </param>
/// <param name="OnlyFrom">The single state this action applies from, or null for any.</param>
/// <param name="NotFrom">A state this action does not apply from, even though the move is valid.</param>
public sealed record EmployeeTransition(
    EmploymentStatus To,
    string Action,
    EmploymentStatus? OnlyFrom = null,
    EmploymentStatus? NotFrom = null)
{
    /// <summary>
    /// Whether this is the one that ends employment, so a screen can style it as destructive and
    /// put it last.
    /// </summary>
    public bool IsExit => To == EmploymentStatus.Exited;
}
