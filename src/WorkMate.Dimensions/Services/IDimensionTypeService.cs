using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The only way the rest of the product touches dimension types. Owns the backing content type
/// each one carries.
/// </summary>
/// <remarks>
/// Tenant-scoped by construction: no method takes a tenant, the shell scope supplies it.
/// Permission is checked here rather than only in a controller, so that the API, the recipe
/// import step and any background job are covered by the same check as the admin screen.
/// </remarks>
public interface IDimensionTypeService
{
    /// <summary>
    /// Creates a dimension type and the content type that backs it: named for the code, with
    /// <c>DimensionRecordPart</c> attached, <c>TitlePart</c> bound to the English name, the
    /// attribute schema's fields added, and the type creatable, listable, securable and not
    /// draftable. Records both the type change and the content-definition change in the audit
    /// trail.
    /// </summary>
    Task<DimensionResult<DimensionTypeDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        bool allowsSelfNesting,
        bool isSystemDefined = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a type's name, self-nesting rule and attribute schema, and brings the backing
    /// content type into line. The code is not changeable: the content type is named for it.
    /// </summary>
    Task<DimensionResult<DimensionTypeDocument>> UpdateAsync(
        string dimensionTypeId,
        BilingualText name,
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        bool allowsSelfNesting,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a type from <paramref name="effectiveDate"/>. The date is explicit and has no
    /// default, because a type retired in June must still resolve for a May report and a silent
    /// "today" is how that goes wrong.
    /// </summary>
    Task<DimensionResult<DimensionTypeDocument>> RetireAsync(
        string dimensionTypeId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);

    /// <summary>The type with this id, or null. <paramref name="asAt"/> defaults to today.</summary>
    Task<DimensionTypeDocument?> GetAsync(
        string dimensionTypeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>The type with this code, or null. <paramref name="asAt"/> defaults to today.</summary>
    Task<DimensionTypeDocument?> GetByCodeAsync(
        string code,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The dimension type behind a generated content type, or null if the content type is not
    /// one of ours. The record handler uses this to decide whether a content item is a dimension
    /// record at all.
    /// </summary>
    Task<DimensionTypeDocument?> GetByContentTypeAsync(
        string contentTypeName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every type, retired ones excluded unless asked for. <paramref name="asAt"/> defaults to
    /// today; there is no undated read path.
    /// </summary>
    Task<IReadOnlyList<DimensionTypeDocument>> ListAsync(
        DateOnly? asAt = null,
        bool includeRetired = false,
        CancellationToken cancellationToken = default);
}
