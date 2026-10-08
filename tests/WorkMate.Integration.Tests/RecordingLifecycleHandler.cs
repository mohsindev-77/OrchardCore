using Microsoft.Extensions.DependencyInjection;
using WorkMate.Records.Services;

namespace WorkMate.Integration.Tests;

/// <summary>
/// A test subscriber to the employee lifecycle events, and the singleton it records into.
/// </summary>
/// <remarks>
/// Specification section 5 requires every lifecycle transition to raise "a domain event that leave,
/// attendance and payroll subscribe to", and the brief's test list names "lifecycle events reaching
/// a test subscriber" specifically. None of those three modules exists yet, so the only way to
/// prove the events actually reach a subscriber — rather than being raised into an empty collection
/// forever — is to be one.
///
/// <b>Why a singleton sink behind a scoped handler.</b> The handler runs inside the tenant's shell
/// scope and is gone the moment that scope ends, taking anything it recorded with it. The sink is
/// registered on the <em>host</em> container, which outlives every scope, so a test can call a
/// service inside <c>InTenantAsync</c> and then read what was raised after the scope has closed.
/// </remarks>
internal sealed class RecordingLifecycleHandler : IEmployeeLifecycleHandler
{
    private readonly RecordedLifecycleEvents _recorded;

    public RecordingLifecycleHandler(RecordedLifecycleEvents recorded) => _recorded = recorded;

    public Task EmployeeCreatedAsync(EmployeeCreated created, CancellationToken cancellationToken = default)
    {
        _recorded.Created.Add(created);

        return Task.CompletedTask;
    }

    public Task EmployeeStatusChangedAsync(
        EmployeeStatusChanged changed,
        CancellationToken cancellationToken = default)
    {
        _recorded.StatusChanges.Add(changed);

        return Task.CompletedTask;
    }
}

/// <summary>Everything <see cref="RecordingLifecycleHandler"/> has seen on this host.</summary>
/// <remarks>
/// Shared across every test in the collection, like the tenant itself, so a test asserts on the
/// events for <em>its own</em> employee rather than on the whole list. The collections are
/// concurrent because the fixture is shared and xUnit may run other collections alongside it.
/// </remarks>
public sealed class RecordedLifecycleEvents
{
    public System.Collections.Concurrent.ConcurrentBag<EmployeeCreated> Created { get; } = [];

    public System.Collections.Concurrent.ConcurrentBag<EmployeeStatusChanged> StatusChanges { get; } = [];

    /// <summary>Every status change raised for one employee code, earliest first.</summary>
    public IReadOnlyList<EmployeeStatusChanged> ChangesFor(string code) =>
        [.. StatusChanges
            .Where(change => string.Equals(change.Code, code, StringComparison.OrdinalIgnoreCase))
            .OrderBy(change => change.EffectiveFrom)];

    /// <summary>Whether a creation was raised for one employee code.</summary>
    public bool WasCreated(string code) =>
        Created.Any(created => string.Equals(created.Code, code, StringComparison.OrdinalIgnoreCase));
}

internal static class RecordingLifecycleHandlerRegistration
{
    /// <summary>
    /// Registers the sink on the host and the handler on every tenant.
    /// </summary>
    /// <remarks>
    /// The handler has to be registered on the <em>shell</em> container, not the host one, because
    /// that is where <c>EmployeeService</c> resolves <c>IEnumerable&lt;IEmployeeLifecycleHandler&gt;</c>
    /// from. <c>OrchardCoreBuilder.ConfigureServices</c> is the seam for that, and it is constructed
    /// directly against the existing collection rather than through <c>AddOrchardCore()</c>, which
    /// would re-run the whole framework's registration on a host that has already done it.
    ///
    /// <b>One instance, registered twice.</b> Registering the sink by type in both containers would
    /// produce two of it — Orchard builds a shell container from copies of the application's service
    /// descriptors, so a singleton descriptor yields one instance per shell. The test would then
    /// read an empty sink while the handler filled a different one. Registering the same object in
    /// both is what makes "what the tenant raised" and "what the test reads" the same list.
    /// </remarks>
    public static void AddRecordingLifecycleHandler(this IServiceCollection services)
    {
        var recorded = new RecordedLifecycleEvents();

        services.AddSingleton(recorded);

        new OrchardCoreBuilder(services).ConfigureServices(tenantServices =>
        {
            tenantServices.AddSingleton(recorded);
            tenantServices.AddScoped<IEmployeeLifecycleHandler, RecordingLifecycleHandler>();
        });
    }
}
