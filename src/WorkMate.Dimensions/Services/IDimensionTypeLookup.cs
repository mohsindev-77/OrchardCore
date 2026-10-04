using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// Read-only access to dimension types: does this reference exist, and what does it look like.
/// </summary>
/// <remarks>
/// This is the read side of the dimension-type aggregate, split out from
/// <see cref="IDimensionTypeService"/> so that something which only needs to answer "does this
/// exist" can depend on that question alone, rather than on the full service with its
/// permissions, validation and content-definition machinery.
///
/// It is what breaks the cycle between <c>DimensionTypeService</c> and <c>IDimensionValidator</c>:
/// the service depends on the validator to check a write, and the validator depends on this
/// lookup to check a reference — never on the service that would depend back on it.
/// <c>DimensionTypeLookup</c>, the implementation, has no dependency on either, so nothing
/// cycles. See the module README for the pattern this is an instance of.
///
/// Every method is undated and ignores retirement: it answers whether something exists at all,
/// because a reference check must not depend on today's date, and a retired type is still a
/// type. A caller wanting "is it currently offered" applies that filter itself using
/// <see cref="DimensionTypeDocument.RetiredOn"/> — <see cref="IDimensionTypeService"/>'s own
/// read methods do exactly that on top of this lookup.
/// </remarks>
public interface IDimensionTypeLookup
{
    /// <summary>The type with this id, or null. Regardless of retirement.</summary>
    Task<DimensionTypeDocument?> GetAsync(string dimensionTypeId, CancellationToken cancellationToken = default);

    /// <summary>The type with this code, or null. Regardless of retirement.</summary>
    Task<DimensionTypeDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The dimension type behind a generated content type, or null if the content type is not
    /// one of ours.
    /// </summary>
    Task<DimensionTypeDocument?> GetByContentTypeAsync(
        string contentTypeName,
        CancellationToken cancellationToken = default);

    /// <summary>Every type in the tenant, including retired ones.</summary>
    Task<IReadOnlyList<DimensionTypeDocument>> ListAsync(CancellationToken cancellationToken = default);
}
