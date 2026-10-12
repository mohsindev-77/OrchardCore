namespace WorkMate.Platform.Services;

/// <summary>
/// Whether a capability is available to this tenant right now.
/// </summary>
/// <remarks>
/// <b>A stub, and it says so.</b> Specification section 8 gives this to
/// <c>WorkMate.Entitlement</c>: one signed document per tenant holding edition, add-ons, packs and
/// validity, and <c>IEntitlementService.IsAllowedAsync(capability)</c> as the fine gate every
/// module asks. That module is a skeleton — a manifest, a permissions file and an empty startup —
/// and nothing of the entitlement record exists yet.
///
/// It is declared here, and called now by the services that need it, for the reason
/// <see cref="IVisibilityService"/> gives for doing the same thing one prompt earlier: a service
/// written without the check is a service whose call sites nobody can find when the check arrives.
/// CLAUDE.md rule 5 requires permissions <em>and</em> entitlement to be checked in the service, so
/// a module that cannot ask the question yet would otherwise simply not comply, quietly, in every
/// service it ships.
///
/// <b>Why the seam is in <c>WorkMate.Platform</c> rather than in <c>WorkMate.Entitlement</c>.</b>
/// Specification section 3 has every WorkMate module depend on <c>WorkMate.Platform</c> and nothing
/// depend on <c>WorkMate.Entitlement</c>; section 8 then has every module ask it a question. The
/// only way both hold is for the question to live in the platform and the answer to be registered
/// over it, which is exactly what <c>IVisibilityService</c> will do for prompt 9. When prompt 8
/// builds the entitlement record, it registers its own implementation of this interface and
/// nothing else in the product changes.
///
/// <see cref="AllowAllEntitlementService"/> is the implementation until then, and it allows
/// everything. That is the right default while the record that would restrict it does not exist:
/// a tenant whose every capability was refused is not a safer failure, only a more confusing one.
/// Permissions still apply and are checked separately, so "allows everything" means "everything
/// the caller already had permission to do".
/// </remarks>
public interface IEntitlementService
{
    /// <summary>
    /// Whether <paramref name="capability"/> is available to this tenant.
    /// </summary>
    /// <param name="capability">
    /// A constant from the owning module's <c>Capabilities</c> class. A string rather than an enum
    /// because the set is open by construction: specification section 8 has each module declare its
    /// own, and an enum in the platform would have to know all of them.
    /// </param>
    Task<bool> IsAllowedAsync(string capability, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// Allows everything, until prompt 8 builds the entitlement record. See
/// <see cref="IEntitlementService"/> for why that is the right stub rather than a restrictive one.
/// </remarks>
public sealed class AllowAllEntitlementService : IEntitlementService
{
    public Task<bool> IsAllowedAsync(string capability, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
