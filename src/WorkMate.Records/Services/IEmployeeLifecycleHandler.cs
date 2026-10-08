using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// How leave, attendance and payroll find out that something happened to an employee.
/// </summary>
/// <remarks>
/// <b>Why this exists at all rather than an event bus.</b> Verified against the pinned Orchard Core
/// 3.0.1 with the API probe: there is no general event bus. <c>ContentHandlerBase</c> offers
/// twenty-nine content lifecycle hooks, and none of them is the right shape — they fire on a content
/// item being published or updated, which is a storage event, and they carry no effective date.
/// "This employee left on 30 June" is not "an item was saved"; a subscriber that inferred one from
/// the other would run on every edit to an unrelated field and would have to guess the date.
///
/// So the module declares the events it raises as an interface and injects every implementation —
/// the same shape <c>WorkMate.Dimensions</c> already uses for
/// <c>IDimensionDeletionBlockerProvider</c>, and the same reason: a module that needs to know
/// something another module owns registers to be told, rather than the owner depending on it.
///
/// <b>Handlers run inside the same shell scope and the same session as the transition</b>, so a
/// handler that throws fails the transition. That is deliberate: if payroll cannot record that an
/// employee left, the employee has not left as far as this platform is concerned, and a silently
/// swallowed exception is how a leaver stays on a payroll run. A handler that wants to be
/// best-effort has to say so by catching its own exceptions.
///
/// Specification section 5 names the subscribers: "each dated and each raising a domain event that
/// leave, attendance and payroll subscribe to. Exit triggers final-settlement calculation in
/// payroll". Note which way round that is — this module raises the event and payroll decides what a
/// final settlement is. No calculation belongs here, per the hard boundary.
/// </remarks>
public interface IEmployeeLifecycleHandler
{
    /// <summary>An employee record now exists, in <see cref="EmploymentStatus.Prospective"/>.</summary>
    Task EmployeeCreatedAsync(EmployeeCreated created, CancellationToken cancellationToken = default);

    /// <summary>
    /// An employee moved between lifecycle states on a stated date.
    /// </summary>
    /// <remarks>
    /// One event for every transition rather than one per kind, because every subscriber has to
    /// branch on the pair anyway: leave stops accruing on the way into
    /// <see cref="EmploymentStatus.OnLeave"/> and starts again on the way out, and a subscriber
    /// given five separate callbacks would have to implement all five to notice.
    /// </remarks>
    Task EmployeeStatusChangedAsync(EmployeeStatusChanged changed, CancellationToken cancellationToken = default);
}

/// <summary>
/// An employee record was created.
/// </summary>
/// <param name="EmployeeId">The content item id. What assignments and head appointments refer to.</param>
/// <param name="Code">The natural key. What a recipe, an export or an integration refers to.</param>
/// <param name="JoinDate">The first day of employment, as stated — never defaulted.</param>
public sealed record EmployeeCreated(
    string EmployeeId,
    string Code,
    string NameEn,
    string NameAr,
    DateOnly JoinDate);

/// <summary>
/// An employee changed lifecycle state.
/// </summary>
/// <param name="EffectiveFrom">
/// The first day <paramref name="To"/> is true. For an exit this is the day <em>after</em> the last
/// working day — see <c>EmployeePart.StatusEffectiveFrom</c> for why the two are kept apart — and
/// <paramref name="LastDayOfService"/> carries the number a subscriber usually wants.
/// </param>
/// <param name="LastDayOfService">
/// The last day of employment, set only on a transition into <see cref="EmploymentStatus.Exited"/>.
/// Null otherwise.
/// </param>
/// <param name="AssignmentsClosed">
/// How many placements the exit closed, across every axis. Zero for every other transition, and
/// legitimately zero for an exit too: a prospective employee who never started was never placed.
/// </param>
/// <param name="HeadshipsClosed">How many units the exit left without a head.</param>
public sealed record EmployeeStatusChanged(
    string EmployeeId,
    string Code,
    EmploymentStatus From,
    EmploymentStatus To,
    DateOnly EffectiveFrom,
    DateOnly? LastDayOfService = null,
    int AssignmentsClosed = 0,
    int HeadshipsClosed = 0);
