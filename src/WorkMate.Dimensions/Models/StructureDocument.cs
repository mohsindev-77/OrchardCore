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

    /// <summary>
    /// The types this axis uses, in reading order. Ordinals are contiguous from zero.
    /// </summary>
    /// <remarks>
    /// Per ADR-0010 this is the axis's <em>vocabulary and display order</em>, not its containment
    /// rule: which types belong to this structure and in what order a reader expects to meet them.
    /// It gives the editor's grid its rows and columns and every type picker its order. What may
    /// sit under what is <see cref="Containment"/>, and nothing else.
    /// </remarks>
    public IReadOnlyList<StructureLevel> Levels { get; set; } = [];

    /// <summary>
    /// The types a record may be a root of this axis as — the top row of the chart.
    /// </summary>
    /// <remarks>
    /// Not a constraint on being parentless. A record of any type may have no parent: that is
    /// either a root or a unit in the unplaced panel, and this list is what decides which. Making
    /// it a constraint would forbid moving a unit off the tree, which is a supported operation.
    /// </remarks>
    public IReadOnlyList<string> RootDimensionTypeIds { get; set; } = [];

    /// <summary>
    /// Every permitted parent-child pairing on this axis. The single authority on containment.
    /// </summary>
    public IReadOnlyList<StructureContainment> Containment { get; set; } = [];

    /// <summary>
    /// The types that may hold employees on this axis. Empty means this axis does not constrain it.
    /// </summary>
    /// <remarks>
    /// The leaf-attachment rule, declared rather than derived, for the reason ADR-0010 made
    /// containment a declared map. It cannot be read off the containment map: Crescent's
    /// <c>Branch → Department</c> edge makes Branch a container, and yet a branch with no
    /// departments holds its own staff. It cannot be read off today's tree either: that would let
    /// an empty Division take staff and would invalidate a staffed Department the moment somebody
    /// put a section under it.
    ///
    /// <b>Empty means unconstrained, not "nothing may hold employees".</b> Every structure written
    /// before this property existed deserialises to empty, and nothing about them changes — the
    /// same rule ADR-0008 sets for a recipe row that states nothing. A tenant that wants the
    /// constraint ticks the types on the structures editor; <c>StructureContainmentDerivation</c>
    /// deliberately does not derive it from a chain, so an upgraded tenant and a chain-described
    /// one are both left alone.
    /// </remarks>
    public IReadOnlyList<string> EmployeeAttachableDimensionTypeIds { get; set; } = [];

    /// <summary>
    /// Whether a record may sit under a parent more than one level above it — a Section directly
    /// under a Business Unit.
    /// </summary>
    /// <remarks>
    /// Superseded by <see cref="Containment"/> and no longer read by the validator. Kept on the
    /// document because it is what the map was derived from, and the editor shows it read-only so
    /// that a customer who had it switched on can see where their extra pairs came from. ADR-0010.
    /// </remarks>
    public bool AllowSkipLevel { get; set; }

    /// <summary>
    /// Whether every placement on this axis must be one <see cref="Containment"/> declares. A
    /// non-strict structure tolerates a pairing it has no rule for, which is what an axis like
    /// Project needs while it is still being shaped.
    /// </summary>
    /// <remarks>
    /// Honoured by the pickers as well as the validator since ADR-0010. Before it, the validator
    /// accepted undeclared placements on a non-strict axis and the picker never offered one — the
    /// forwards and backwards readings of the rule disagreed on exactly the case the flag exists
    /// for.
    /// </remarks>
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
    /// Where <paramref name="dimensionTypeId"/> sits in this axis's reading order, or null if it is
    /// not one of its types. Display only since ADR-0010 — no rule is arithmetic on it any more.
    /// </summary>
    public int? OrdinalOf(string dimensionTypeId) =>
        Levels.FirstOrDefault(level =>
            string.Equals(level.DimensionTypeId, dimensionTypeId, StringComparison.Ordinal))?.Ordinal;

    /// <summary>This axis's types, in reading order. The vocabulary every rule is expressed over.</summary>
    public IReadOnlyList<string> DimensionTypeIds =>
        [.. Levels.OrderBy(level => level.Ordinal).Select(level => level.DimensionTypeId)];

    /// <summary>Whether a record of this type may be a root of this axis.</summary>
    public bool PermitsRoot(string dimensionTypeId) =>
        RootDimensionTypeIds.Contains(dimensionTypeId, StringComparer.Ordinal);

    /// <summary>
    /// Whether this axis declares that a child of one type may sit directly under a parent of
    /// another. The whole containment rule, minus the dimension type's self-nesting veto, which
    /// lives on the type and is applied by <c>ContainmentRules</c>.
    /// </summary>
    public bool Permits(string parentDimensionTypeId, string childDimensionTypeId) =>
        Containment.Any(pair =>
            string.Equals(pair.ParentDimensionTypeId, parentDimensionTypeId, StringComparison.Ordinal) &&
            string.Equals(pair.ChildDimensionTypeId, childDimensionTypeId, StringComparison.Ordinal));

    /// <summary>
    /// Whether a record of this type may hold employees on this axis. True for everything while the
    /// axis declares nothing.
    /// </summary>
    public bool PermitsEmployeesAt(string dimensionTypeId) =>
        EmployeeAttachableDimensionTypeIds.Count == 0 ||
        EmployeeAttachableDimensionTypeIds.Contains(dimensionTypeId, StringComparer.Ordinal);

    /// <summary>The types this axis declares may sit under <paramref name="parentDimensionTypeId"/>.</summary>
    public IReadOnlyList<string> DeclaredChildTypeIdsOf(string parentDimensionTypeId) =>
        [.. Containment
            .Where(pair => string.Equals(pair.ParentDimensionTypeId, parentDimensionTypeId, StringComparison.Ordinal))
            .Select(pair => pair.ChildDimensionTypeId)
            .Distinct(StringComparer.Ordinal)];
}
