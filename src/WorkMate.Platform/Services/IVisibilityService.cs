namespace WorkMate.Platform.Services;

/// <summary>
/// Which employees the current caller is allowed to see.
/// </summary>
/// <remarks>
/// <b>A stub, and it says so.</b> Specification section 9 — data visibility by node — is prompt 9's
/// work: a manager sees their own branch of the organisation, an HR officer sees a region, and
/// nobody sees the whole tenant by default. None of that exists yet.
///
/// It is declared now, and used now by the employee picker, for one reason: a picker that offers
/// every employee in the tenant and is later retrofitted with filtering is a picker whose call
/// sites nobody can find. Every candidate set in this product goes through here from the day it is
/// written, so when prompt 9 replaces the implementation there is nothing to go and change.
///
/// <see cref="AllowAllVisibilityService"/> is the implementation until then, and it allows
/// everything. That is the right default while the mechanism that would restrict it does not
/// exist: the alternative is a product where nobody can see anybody, which is not a safer failure,
/// only a more confusing one. Permissions still apply — <c>ViewEmployees</c> is checked before any
/// of this is reached — so "allows everything" means "everything the caller already had permission
/// to see".
/// </remarks>
public interface IVisibilityService
{
    /// <summary>Whether the current caller may see this employee at all.</summary>
    Task<bool> CanSeeEmployeeAsync(string employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The subset of <paramref name="employeeIds"/> the current caller may see, in the order given.
    /// </summary>
    /// <remarks>
    /// The set-shaped method exists so a caller filtering a page of candidates issues one question
    /// rather than one per row. A picker that asked per row would be the N+1 that visibility
    /// filtering is most likely to introduce, and it would appear in prompt 9 rather than here —
    /// by which time the call sites would be written the wrong way round.
    /// </remarks>
    Task<IReadOnlyList<string>> FilterEmployeesAsync(
        IReadOnlyList<string> employeeIds,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// Allows everything, until prompt 9 builds visibility by node. See <see cref="IVisibilityService"/>
/// for why that is the right stub rather than a restrictive one.
/// </remarks>
public sealed class AllowAllVisibilityService : IVisibilityService
{
    public Task<bool> CanSeeEmployeeAsync(string employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<IReadOnlyList<string>> FilterEmployeesAsync(
        IReadOnlyList<string> employeeIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(employeeIds ?? []);
}
