using WorkMate.Core;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// A named axis with an ordered list of dimension types as its levels. Organisation, Location and
/// Cost are each one of these.
/// </summary>
/// <remarks>
/// A YesSql document; see the remark on <see cref="DimensionTypeDocument"/> for why the setters
/// are public.
///
/// Scoping links to a structure is decision 4 of the dimension engine architecture, and it is
/// what lets one record sit on several axes at once. A single parent field on the record would
/// collapse every axis into one tree.
/// </remarks>
public sealed class StructureDocument
{
    /// <summary>YesSql's document id. Assigned on first save; never meaningful to the business.</summary>
    public long Id { get; set; }

    /// <summary>YesSql's optimistic concurrency token, per ADR-0005.</summary>
    public long Version { get; set; }

    /// <summary>The stable identity links, closure rows and assignments are scoped by.</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>The customer-facing code, unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The name shown to a user, in both platform languages.</summary>
    /// <remarks>
    /// Defaults to a fresh instance, never to <see cref="BilingualText.Empty"/> — see the remark
    /// on <c>DimensionTypeDocument.Name</c>, which had the same defect, and ADR-0007.
    /// </remarks>
    public BilingualText Name { get; set; } = new(string.Empty, string.Empty);

    /// <summary>The levels, root first. Ordinals are contiguous from zero.</summary>
    public IReadOnlyList<StructureLevel> Levels { get; set; } = [];

    /// <summary>
    /// Whether a record may sit under a parent more than one level above it — a Section directly
    /// under a Business Unit. Architecture section 6 makes this a structure setting rather than a
    /// per-node exception on purpose: an exception granted per node is one nobody can audit.
    /// </summary>
    public bool AllowSkipLevel { get; set; }

    /// <summary>
    /// Whether every record on this axis must sit at a declared level. A non-strict structure
    /// tolerates a record whose type is not in <see cref="Levels"/> at all, which is what an axis
    /// like Project needs while it is still being shaped.
    /// </summary>
    public bool IsStrict { get; set; } = true;

    /// <summary>
    /// Whether this is the primary organisation axis: the default scope for approvals and for
    /// data visibility. At most one structure in a tenant carries it.
    /// </summary>
    public bool IsPrimaryOrganisation { get; set; }

    /// <summary>The type permitted at <paramref name="ordinal"/>, or null if there is no such level.</summary>
    public string? DimensionTypeIdAt(int ordinal) =>
        Levels.FirstOrDefault(level => level.Ordinal == ordinal)?.DimensionTypeId;

    /// <summary>
    /// Where <paramref name="dimensionTypeId"/> sits on this axis, or null if it is not a level of
    /// it. The level rules in architecture section 6 are all expressed as arithmetic on this.
    /// </summary>
    public int? OrdinalOf(string dimensionTypeId) =>
        Levels.FirstOrDefault(level =>
            string.Equals(level.DimensionTypeId, dimensionTypeId, StringComparison.Ordinal))?.Ordinal;
}
