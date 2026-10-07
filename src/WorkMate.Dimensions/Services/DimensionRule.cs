namespace WorkMate.Dimensions.Services;

/// <summary>
/// Every rule the dimension engine enforces, one member per row of the table in section 6 of the
/// dimension engine architecture plus the configuration rules that table takes as read.
/// </summary>
/// <remarks>
/// Architecture section 7 requires that "a blocked move says which rule blocked it and which node
/// is at fault". That is what this enum is for: a <see cref="DimensionError"/> names the rule in
/// a form a caller can branch on and a test can assert, and carries the localised sentence
/// separately for the user. A caller must never have to match on message text.
///
/// The members are a stable contract. The graph and assignment rules are declared here now, with
/// the layers that enforce them noted, so that the single validation service has one vocabulary
/// to grow into rather than a second one invented alongside it.
/// </remarks>
public enum DimensionRule
{
    // Configuration rules. Enforced by IDimensionTypeService and IStructureService.

    /// <summary>A code is missing, or is not a valid identifier a content type can be named for.</summary>
    CodeFormat,

    /// <summary>A code is already in use in this tenant, including by a retired record.</summary>
    CodeUniqueness,

    /// <summary>A name is missing in a language the platform requires.</summary>
    NameRequired,

    /// <summary>A dated write arrived with no effective date. Nothing is ever defaulted.</summary>
    EffectiveDateRequired,

    /// <summary>An effective range ends before it starts.</summary>
    EffectiveRangeInvalid,

    /// <summary>An attribute name is missing, malformed, or declared twice on one type.</summary>
    AttributeSchema,

    /// <summary>A structure's levels are empty, name a type that does not exist, or name one twice.</summary>
    StructureLevels,

    /// <summary>A second structure claims to be the primary organisation axis.</summary>
    SinglePrimaryOrganisation,

    /// <summary>A referenced dimension type or structure does not exist.</summary>
    UnknownReference,

    /// <summary>A change was refused because something already depends on what it would alter.</summary>
    ImmutableOnceInUse,

    // Graph rules. Enforced by IDimensionGraphService and IDimensionService; see ADR-0005.

    /// <summary>The parent's type is not a permitted level for the child's type on this structure.</summary>
    ParentTypeNotPermitted,

    /// <summary>
    /// The move would skip a level and the structure does not allow skipping.
    /// </summary>
    /// <remarks>
    /// No longer emitted. ADR-0010 replaced level arithmetic with an explicit containment map, so
    /// a skipped level is simply a pairing the structure does not declare and is reported as
    /// <see cref="ParentTypeNotPermitted"/>. Kept because audit entries written before that change
    /// name it, and a persisted enum member is a contract.
    /// </remarks>
    LevelSkipping,

    /// <summary>
    /// The structure has no rule for this pairing, and is not strict, so it is allowed. Advisory:
    /// the one thing a non-strict axis trades for being able to grow a shape before it is named.
    /// </summary>
    ParentTypeNotDeclared,

    /// <summary>The move would make a node its own ancestor.</summary>
    Cycle,

    /// <summary>The parent and child share a type and that type does not declare self-nesting.</summary>
    SelfNesting,

    /// <summary>The record is referenced by live data and cannot be deleted; the blockers are listed.</summary>
    DeletionBlocked,

    /// <summary>A merge was asked to fold a node into itself or into one of its own descendants.</summary>
    MergeTarget,

    /// <summary>There is no move recorded on the date a cancel operation named.</summary>
    MoveNotFound,

    /// <summary>
    /// The parent was retired before the date a child is to be placed under it. Unlike
    /// <see cref="ParentNotEffectiveWhenChildIs"/>, this is never a dating mistake worth only a
    /// warning: it would leave a live unit under a closed one with no end date.
    /// </summary>
    ParentRetired,

    /// <summary>
    /// A retirement would leave units with no parent and nobody has said what should happen to
    /// them. The counterpart of <see cref="ParentRetired"/> seen from the other end: that rule
    /// stops a child being placed under a closed parent, this one stops a parent closing over a
    /// live child by accident.
    /// </summary>
    ChildrenNeedDisposition,

    // Assignment rules. Enforced by IEmployeeAssignmentService.

    /// <summary>Two effective ranges overlap for one employee on one structure.</summary>
    OverlappingAssignment,

    /// <summary>Allocations for one employee on one structure do not total 100 percent on a date.</summary>
    AllocationTotal,

    /// <summary>An employee has no primary assignment on a date, or more than one.</summary>
    SinglePrimaryAssignment,

    // Advisory. Reported but never blocking, per architecture section 6.

    /// <summary>The parent is not effective for the whole period the child is. Warned, not blocked,
    /// because customers legitimately pre-build future structures.</summary>
    ParentNotEffectiveWhenChildIs,
}
