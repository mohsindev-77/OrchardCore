using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// A kind of unit: Business Unit, Division, Department, Section, Branch, Cost Centre, Project, or
/// anything a customer invents. Configuration, not content, per decision 3 of the dimension
/// engine architecture.
/// </summary>
/// <remarks>
/// A YesSql document. The properties carry public setters, which the coding standard otherwise
/// reserves for content parts, because YesSql materialises a document by deserialising into it
/// and assigns <see cref="Id"/> and <see cref="Version"/> itself. Mutation still goes through
/// <c>IDimensionTypeService</c>; nothing outside this module loads one of these to change it.
/// </remarks>
public sealed class DimensionTypeDocument
{
    /// <summary>YesSql's document id. Assigned on first save; never meaningful to the business.</summary>
    public long Id { get; set; }

    /// <summary>
    /// YesSql's optimistic concurrency token, per ADR-0005. Every write goes through
    /// <c>SaveAsync(document, checkConcurrency: true)</c>, so two administrators editing the same
    /// type concurrently get a <c>ConcurrencyException</c> rather than a silent overwrite.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// The stable identity other records point at. Generated once and never reused, so that
    /// correcting a <see cref="Code"/> does not orphan every record of this type.
    /// </summary>
    public string DimensionTypeId { get; set; } = string.Empty;

    /// <summary>
    /// The customer-facing code, unique within the tenant. The backing content type is named for
    /// it, so it is validated as an identifier and it is immutable once created.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The name shown to a user, in both platform languages.</summary>
    public BilingualText Name { get; set; } = BilingualText.Empty;

    /// <summary>
    /// True for the types WorkMate ships in a recipe and the customer may not delete. A
    /// system-defined type can still have its name and attribute schema extended.
    /// </summary>
    public bool IsSystemDefined { get; set; }

    /// <summary>
    /// Whether a record of this type may sit directly under another of the same type — a Section
    /// inside a Section. Off by default, because most types are a single level of a hierarchy and
    /// accidental self-nesting is how an organisation chart quietly grows a thirteenth level.
    /// </summary>
    public bool AllowsSelfNesting { get; set; }

    /// <summary>The fields this type's records carry beyond the standard ones.</summary>
    public IReadOnlyList<DimensionAttributeDefinition> AttributeSchema { get; set; } = [];

    /// <summary>
    /// The content type created for this dimension type. Stored rather than derived on every read,
    /// so that changing how a name is derived from a code cannot strand an existing tenant's
    /// records behind a content type nothing looks for any more.
    /// </summary>
    public string ContentTypeName { get; set; } = string.Empty;

    /// <summary>
    /// The date this type stopped being offered, or null while it is current. Retiring is dated
    /// rather than boolean because a type retired in June must still resolve for a May report.
    /// </summary>
    public DateOnly? RetiredOn { get; set; }

    /// <summary>Whether the type is retired as at <paramref name="asAt"/>.</summary>
    public bool IsRetiredOn(DateOnly asAt) => RetiredOn is not null && asAt >= RetiredOn;
}
