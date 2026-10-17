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
    /// <remarks>
    /// No longer emitted, for the same reason as <see cref="LevelSkipping"/> and by the same ADR's
    /// addendum: the dimension type's veto over nesting inside itself is gone, so parent and child
    /// sharing a type is just a pairing the structure declares or does not, reported as
    /// <see cref="ParentTypeNotPermitted"/> or <see cref="ParentTypeNotDeclared"/>. Kept because
    /// audit entries written before that change name it, and a persisted enum member is a contract.
    /// </remarks>
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

    /// <summary>
    /// The unit's dimension type is not one this structure says may hold employees.
    /// </summary>
    /// <remarks>
    /// The leaf-attachment rule. Declared per structure as
    /// <c>StructureDocument.EmployeeAttachableDimensionTypeIds</c>, not derived, for the reason
    /// ADR-0010 made containment a declared map: *"may a Department hold employees"* is an
    /// axis-specific fact. A Branch with no departments holds its own staff and a Branch with
    /// departments does not, so nothing about the type decides it and nothing about today's shape
    /// of the tree may decide it either — a rule that read the children would let an empty Division
    /// take staff and would invalidate a staffed Department the moment somebody added a section
    /// under it.
    ///
    /// A structure that declares nothing is unconstrained, which is every structure written before
    /// this rule existed.
    /// </remarks>
    UnitDoesNotHoldEmployees,

    // Assignment and head rules, continued.

    /// <summary>Two employees are recorded as heading one unit on one date.</summary>
    SingleHeadPerUnit,

    /// <summary>
    /// A head appointment names somebody who is not an employee of this tenant, or who had already
    /// left before the term it claims.
    /// </summary>
    HeadNotEligible,

    // Advisory. Reported but never blocking, per architecture section 6.

    /// <summary>
    /// The employee is being placed at a unit that has units under it on that date.
    /// </summary>
    /// <remarks>
    /// Advisory, not blocking, and the two halves of that matter equally. Blocking would refuse a
    /// department that has three sections and a departmental secretary, which is an ordinary shape.
    /// Saying nothing would hide the thing the leaf-attachment rule exists to prevent: somebody
    /// placed at a container is counted once by the container's own headcount and again by every
    /// roll-up beneath it, and the two numbers stay plausible while disagreeing.
    /// </remarks>
    EmployeeAtContainerUnit,

    /// <summary>The parent is not effective for the whole period the child is. Warned, not blocked,
    /// because customers legitimately pre-build future structures.</summary>
    ParentNotEffectiveWhenChildIs,

    /// <summary>
    /// A field's value breaks a rule about its shape that is not about a code — today, an alias
    /// longer than its column.
    /// </summary>
    /// <remarks>
    /// <b>Appended rather than slotted in beside <see cref="CodeFormat"/>.</b> This enum is
    /// serialised into audit entries, and <c>System.Text.Json</c> writes an enum as its numeric
    /// value unless it is told otherwise — so inserting a member anywhere but the end renumbers
    /// every member after it and silently changes what every entry already written means. The
    /// grouping comments above are for a reader; the order is a contract.
    /// </remarks>
    InvalidValue,

    /// <summary>
    /// A reference points at something this kind of reference may not point at, or is held by a
    /// record that may not hold one. Decision D26 C — a Position's allowed grades.
    /// </summary>
    /// <remarks>
    /// A reference gets none of the containment map's protection, because it is not a parent: no
    /// structure declares it, nothing in the closure constrains it, and the pickers that offer it
    /// are the only other thing standing between a position and a reference to a department. So the
    /// rule is stated here and enforced in the validator, which is what makes the form, the recipe
    /// and F6's employee screen refuse it in the same words.
    ///
    /// Also carries the employee rule of D26 G: a grade that is not one of the chosen position's
    /// allowed grades. It is the same statement read from the other end — <em>this pointer is not
    /// one this thing may hold</em> — and giving it a second member would mean two rules a caller
    /// has to branch on to mean one thing.
    ///
    /// <b>Appended, never inserted</b>, for the reason <see cref="InvalidValue"/> gives: this enum
    /// is serialised into audit entries by number.
    /// </remarks>
    ReferenceNotPermitted,

    /// <summary>
    /// A position was written without saying what it reports to, on a tenant that has a reporting
    /// line. Slice F6b, decision D31.
    /// </summary>
    /// <remarks>
    /// <b>Silence is not an answer, and it used to be read as one.</b> A position saved with
    /// <em>Reports to</em> untouched was taken to mean "top of the reporting line" — a claim nobody
    /// had made — and in fact wrote no reporting-line row at all, so the post was on that axis
    /// nowhere and every chart drawn from it was quietly short a box.
    ///
    /// <b>"Nobody" remains a real and legitimate answer</b>, and needs no new storage: it is a
    /// placement row with a null parent, exactly what the CEO has in the Zenith recipe today. What
    /// this rule refuses is the <em>absence of a statement</em>, which is why the recipe is
    /// unaffected — all eight of its positions state their reporting line, the CEO's as
    /// <c>"parentCode": null</c>.
    ///
    /// <b>Appended, never inserted</b>, for the reason <see cref="InvalidValue"/> gives: this enum
    /// is serialised into audit entries by number.
    /// </remarks>
    ReportsToRequired,

    /// <summary>
    /// A position was put at the top of a reporting line that already has somebody there. Advisory.
    /// Slice F6b, decision D31.
    /// </summary>
    /// <remarks>
    /// <b>Worth saying and not worth refusing.</b> One top is the ordinary shape and two is usually
    /// a mistake — but an organisation mid-handover, or one with joint chief executives, is
    /// entitled to record what it actually has, and a rule that refused would be the engine
    /// overruling a customer about their own company. So it names the post already there and lets
    /// the save through, which is the same judgement
    /// <see cref="EmployeeAtContainerUnit"/> makes.
    ///
    /// Its own rule rather than a flag on <see cref="ReportsToRequired"/>, because
    /// <c>DimensionError.IsAdvisory</c> is derived from the rule on purpose: whether something
    /// blocks is a property of the rule and must not be settable differently in two places.
    ///
    /// <b>Appended, never inserted</b>, for the reason <see cref="InvalidValue"/> gives.
    /// </remarks>
    SecondTopOfReportingLine,

    /// <summary>
    /// An employee was placed on a structure that is not somewhere people go at all — a reporting
    /// line, or a grade ladder. Decision D30, "where the rule is applied".
    /// </summary>
    /// <remarks>
    /// <b>Distinct from <see cref="UnitDoesNotHoldEmployees"/>, which is about the node.</b> That
    /// rule answers "may a Department hold staff on this axis" and presumes the axis is a place.
    /// This one answers whether the axis is a place at all, and the difference is not pedantic: on
    /// a reporting line every node IS a position, so the node-level rules all said yes and the
    /// write would have gone through. ADR-0028 makes the reporting line a position's second parent
    /// axis — what a post answers to — and D26 C makes a grade a reference carried on the
    /// assignment row (ADR-0026) rather than a node anybody is placed at.
    ///
    /// <b>Blocking, not advisory.</b> A placement here is not an unusual shape somebody might
    /// legitimately mean — the way a departmental secretary under a container unit is. It would
    /// roll employees up under grades, or under posts on an axis no headcount reads, and both
    /// numbers would stay plausible while being wrong.
    ///
    /// <b>Nothing already written is touched.</b> ADR-0029: this runs on the write.
    ///
    /// <b>Appended, never inserted</b>, for the reason <see cref="InvalidValue"/> gives.
    /// </remarks>
    StructureDoesNotHoldEmployees,
}
