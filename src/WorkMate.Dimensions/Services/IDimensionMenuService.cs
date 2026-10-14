using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The Organisation menu's dimension section, built from the tenant's own types and values.
/// ADR-0024.
/// </summary>
/// <remarks>
/// <b>A service rather than a few calls inside the navigation provider</b>, because the answer is
/// cached per tenant and invalidated by signal, and a cache a navigation provider owned would be a
/// cache nothing else could invalidate. <c>CachedDimensionMenuService</c> is the decorator;
/// <c>DimensionMenuService</c> is the query behind it.
///
/// Internal, and for the same reason <see cref="IDimensionTypeLookup"/> is: it applies no permission
/// check and enters no authorisation scope, which is right for a menu whose every entry Orchard
/// filters by the <c>Permission</c> it declares, and wrong for anything outside this module to
/// reach.
/// </remarks>
internal interface IDimensionMenuService
{
    /// <summary>
    /// The menu as at today in the tenant's time zone.
    /// </summary>
    /// <remarks>
    /// Dated, because the values listed are the ones effective now: a department that closed in June
    /// is not in July's menu, and one that opens in September appears in September. The cache key
    /// carries the date for exactly that reason — see <c>DimensionCacheKeys.Menu</c>.
    /// </remarks>
    Task<DimensionMenu> GetAsync(CancellationToken cancellationToken = default);
}
