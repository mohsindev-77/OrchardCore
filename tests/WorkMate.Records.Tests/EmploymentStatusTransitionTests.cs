using FluentAssertions;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// Every ordered pair of lifecycle states, permitted and refused.
/// </summary>
/// <remarks>
/// Enumerated rather than sampled. The table is small enough to state in full, and the moves that
/// matter are the ones nobody thinks to write a test for — Exited back to Suspended, Active to
/// Active — which is exactly where a loosely-written condition lets something through.
/// </remarks>
public sealed class EmploymentStatusTransitionTests
{
    [Theory]
    [InlineData(EmploymentStatus.Prospective, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Prospective, EmploymentStatus.Exited)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Suspended)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Exited)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Suspended)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Exited)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.Exited)]
    [InlineData(EmploymentStatus.Exited, EmploymentStatus.Active)]
    public void APermittedMoveIsPermitted(EmploymentStatus from, EmploymentStatus to) =>
        EmployeeLifecycle.Permits(from, to).Should().BeTrue();

    /// <summary>
    /// A move to the state already held is refused, and that is not pedantry.
    /// </summary>
    /// <remarks>
    /// Accepting it would overwrite the date the current state began — which is the date leave
    /// accrual, probation and suspension are all counted from — on a request that said nothing
    /// about wanting to change it. Re-dating a state somebody is already in is a correction, and a
    /// correction should look like one.
    /// </remarks>
    [Theory]
    [InlineData(EmploymentStatus.Prospective)]
    [InlineData(EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Suspended)]
    [InlineData(EmploymentStatus.Exited)]
    public void AMoveToTheStateAlreadyHeldIsRefused(EmploymentStatus status) =>
        EmployeeLifecycle.Permits(status, status).Should().BeFalse();

    /// <summary>A leaver comes back to work, or stays gone. Nothing else.</summary>
    /// <remarks>
    /// Suspended and OnLeave both describe an employment that is running, and a leaver's is not
    /// until somebody says so. Prospective would be worse still: it would make a rehire look like
    /// somebody who had never worked here, and lose the service history gratuity is computed from.
    /// </remarks>
    [Theory]
    [InlineData(EmploymentStatus.Prospective)]
    [InlineData(EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Suspended)]
    public void AnExitedEmployeeCanOnlyBeReinstatedToActive(EmploymentStatus to) =>
        EmployeeLifecycle.Permits(EmploymentStatus.Exited, to).Should().BeFalse();

    /// <summary>
    /// A suspension is lifted before anything else happens.
    /// </summary>
    /// <remarks>
    /// Straight from Suspended to OnLeave would leave no record of the suspension having ended, so
    /// the employee's history would show a suspension that simply stopped being mentioned.
    /// </remarks>
    [Fact]
    public void ASuspendedEmployeeCannotGoStraightOnToLeave() =>
        EmployeeLifecycle.Permits(EmploymentStatus.Suspended, EmploymentStatus.OnLeave).Should().BeFalse();

    /// <summary>
    /// Somebody hired who never started can leave without ever having been active.
    /// </summary>
    /// <remarks>
    /// A real outcome, and the reason the model does not activate people by inferring it from a
    /// join date having passed.
    /// </remarks>
    [Fact]
    public void AProspectiveEmployeeWhoNeverStartedCanLeave() =>
        EmployeeLifecycle.Permits(EmploymentStatus.Prospective, EmploymentStatus.Exited).Should().BeTrue();

    /// <summary>Nothing returns anybody to Prospective: it is a state you leave once.</summary>
    [Theory]
    [InlineData(EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Suspended)]
    [InlineData(EmploymentStatus.Exited)]
    public void NobodyGoesBackToProspective(EmploymentStatus from) =>
        EmployeeLifecycle.Permits(from, EmploymentStatus.Prospective).Should().BeFalse();

    /// <summary>
    /// The table covers every state, so a state added later cannot be a dead end nobody noticed.
    /// </summary>
    [Fact]
    public void EveryStateHasSomewhereToGo()
    {
        foreach (var status in Enum.GetValues<EmploymentStatus>())
        {
            EmployeeLifecycle.From(status).Should().NotBeEmpty(
                $"{status} must have at least one permitted move, or an employee can be stranded in it");
        }
    }

    /// <summary>Every employment can be ended, from wherever it currently is.</summary>
    [Theory]
    [InlineData(EmploymentStatus.Prospective)]
    [InlineData(EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Suspended)]
    public void AnyLiveEmploymentCanBeEnded(EmploymentStatus from) =>
        EmployeeLifecycle.Permits(from, EmploymentStatus.Exited).Should().BeTrue();
}
