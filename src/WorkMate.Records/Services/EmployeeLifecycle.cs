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

    private static HashSet<EmploymentStatus> Set(params EmploymentStatus[] statuses) => [.. statuses];
}
