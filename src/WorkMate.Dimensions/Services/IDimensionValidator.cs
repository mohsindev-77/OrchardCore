using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The single validation service. Every write path in the dimension engine calls it, and the
/// designer, the API and every import path get the same answer because they ask the same
/// question of the same code.
/// </summary>
/// <remarks>
/// Architecture section 6: "Validation runs in one service, called by the designer, the API and
/// every import path. The designer shows a violation before the user commits, but the service is
/// the authority and re-checks on write." Both halves matter. A designer that checks and a
/// service that trusts it is a service with no rules at all the moment somebody uses the API.
///
/// Every method returns errors rather than throwing, because a user's edit can break three rules
/// at once and they should see all three. An error names its <see cref="DimensionRule"/> so a
/// caller can branch and a test can assert, and carries a localised sentence separately so no
/// user is ever shown an enum.
/// </remarks>
public interface IDimensionValidator
{
    /// <summary>
    /// Opens a batch so that an import can be validated row by row while still catching
    /// duplicates between its own rows.
    /// </summary>
    /// <remarks>
    /// Architecture section 6 requires an import to validate every row and report all failures
    /// rather than stopping at the first, and to be rejected whole if the result would be
    /// invalid. Both need the validator to remember what it has already seen in this run —
    /// two rows sharing a code are each individually fine and together are not, and nothing in
    /// the database can see the clash because neither row is committed yet.
    ///
    /// A single write passes null and gets batch-free behaviour.
    /// </remarks>
    DimensionValidationBatch BeginBatch();

    /// <summary>Rules for creating or changing a dimension type.</summary>
    /// <param name="dimensionTypeId">Null when creating; the type's id when changing it.</param>
    Task<IReadOnlyList<DimensionError>> ValidateDimensionTypeAsync(
        string? dimensionTypeId,
        string code,
        BilingualText name,
        IReadOnlyList<DimensionAttributeDefinition> attributeSchema,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default);

    /// <summary>Rules for creating or changing a structure.</summary>
    /// <param name="structureId">Null when creating; the structure's id when changing it.</param>
    /// <param name="levelDimensionTypeIds">The axis's vocabulary, in reading order.</param>
    /// <param name="shape">
    /// Its root types and containment map, which must name only types from that vocabulary and
    /// must leave at least one way in. ADR-0010.
    /// </param>
    Task<IReadOnlyList<DimensionError>> ValidateStructureAsync(
        string? structureId,
        string code,
        BilingualText name,
        IReadOnlyList<string> levelDimensionTypeIds,
        StructureShape shape,
        bool isPrimaryOrganisation,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rules for creating or changing a dimension record: code format, code uniqueness across
    /// the tenant including retired records and including earlier rows of the same batch, a
    /// name in both languages, and an effective range that makes sense.
    /// </summary>
    /// <param name="recordId">Null when creating; the record's content item id when changing it.</param>
    Task<IReadOnlyList<DimensionError>> ValidateRecordAsync(
        string? recordId,
        string dimensionTypeId,
        string code,
        BilingualText name,
        EffectiveRange effectiveRange,
        DimensionValidationBatch? batch = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rules for putting a record under a parent on an axis: permitted level, level skipping,
    /// self-nesting, cycles across every date, and the advisory warning when the parent is not
    /// yet effective.
    /// </summary>
    /// <param name="newParentId">The proposed parent, or null to make the record a root.</param>
    Task<IReadOnlyList<DimensionError>> ValidatePlacementAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rules for an employee's concurrent placements on one axis: no overlap, allocations
    /// totalling 100 percent, exactly one primary, and that each unit is one this axis says may
    /// hold employees.
    /// </summary>
    /// <remarks>
    /// The last of those is the leaf-attachment rule, and it comes in two halves that are
    /// deliberately not the same strength. <see cref="DimensionRule.UnitDoesNotHoldEmployees"/>
    /// blocks, because an axis that has declared which types hold people has said so on purpose.
    /// <see cref="DimensionRule.EmployeeAtContainerUnit"/> only warns, because a unit with children
    /// may still legitimately have somebody at it and refusing that would be wrong far more often
    /// than it would be right.
    /// </remarks>
    Task<IReadOnlyList<DimensionError>> ValidateAssignmentAsync(
        string employeeId,
        string structureId,
        IReadOnlyList<AssignmentSplitEntry> split,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rules for appointing somebody to lead a unit: at most one head per unit per date, and a
    /// head who is a real employee and has not already left.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> a rule that the head is assigned to the unit. ADR-0012: a head
    /// need not be a member of what they lead, and the whole reason an appointment is its own
    /// record rather than a flag on an assignment is that requiring one would mean inventing an
    /// allocation for somebody who has none there.
    ///
    /// Employee existence is answered through <see cref="IEmployeeLookup"/>, which
    /// <c>WorkMate.Records</c> implements. On a tenant where that module is disabled there is no
    /// way to check, and this reports exactly that — <see cref="DimensionRule.HeadNotEligible"/>
    /// naming the missing module — rather than passing an unverifiable appointment.
    /// </remarks>
    /// <param name="range">The term being claimed, open ended when its end is null.</param>
    Task<IReadOnlyList<DimensionError>> ValidateHeadAppointmentAsync(
        string structureId,
        string recordId,
        string employeeId,
        EffectiveRange range,
        CancellationToken cancellationToken = default);

    /// <summary>Rules for folding one record into another.</summary>
    Task<IReadOnlyList<DimensionError>> ValidateMergeAsync(
        string structureId,
        string sourceRecordId,
        string targetRecordId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What may be done with a record that someone wants rid of: hard delete, retire, or
    /// nothing, with the specific blockers named.
    /// </summary>
    /// <remarks>
    /// Architecture section 6 is emphatic that a blocked deletion lists its blockers so the
    /// user can act, and is "never a generic failure message".
    /// </remarks>
    Task<DeletionAssessment> AssessDeletionAsync(
        string recordId,
        CancellationToken cancellationToken = default);
}

/// <summary>One line of a split allocation, as the validator and the assignment service pass it.</summary>
public sealed record AssignmentSplitEntry(string RecordId, decimal AllocationPercent, bool IsPrimary);

/// <summary>
/// What the validator has already seen in the current run.
/// </summary>
/// <remarks>
/// Deliberately a plain object the caller holds rather than state on the service: a batch has a
/// beginning and an end that only the caller knows, and a validator that accumulated state
/// invisibly would leak one import's rows into the next.
/// </remarks>
public sealed class DimensionValidationBatch
{
    private readonly HashSet<string> _recordCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _typeCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _structureCodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this code has already been used by an earlier row of this batch.</summary>
    internal bool RecordCodeSeen(string code) => _recordCodes.Contains(code);

    internal bool TypeCodeSeen(string code) => _typeCodes.Contains(code);

    internal bool StructureCodeSeen(string code) => _structureCodes.Contains(code);

    internal void RememberRecordCode(string code) => _recordCodes.Add(code);

    internal void RememberTypeCode(string code) => _typeCodes.Add(code);

    internal void RememberStructureCode(string code) => _structureCodes.Add(code);
}

/// <summary>What may be done with a record, and why not, if not.</summary>
/// <param name="Outcome">Clean, retire only, or blocked.</param>
/// <param name="Blockers">
/// Every reason, named specifically. Empty when the outcome is clean.
/// </param>
public sealed record DeletionAssessment(DeletionOutcome Outcome, IReadOnlyList<DimensionError> Blockers);

/// <summary>The three outcomes architecture section 6 defines for a deletion.</summary>
public enum DeletionOutcome
{
    /// <summary>Nothing references the record. A hard delete is permitted.</summary>
    Clean,

    /// <summary>
    /// Only history references it. The record is closed with an end date, disappears from
    /// pickers, and continues to resolve for historical queries.
    /// </summary>
    RetireOnly,

    /// <summary>Live data references it. Blocked, with the blockers listed.</summary>
    Blocked,
}

/// <summary>
/// Lets a module outside the dimension engine say why a record cannot be deleted.
/// </summary>
/// <remarks>
/// Architecture section 6 lists the checks in order: current assignments, historical
/// assignments, posted payroll, open workflow instances, and references from other
/// configuration such as approval scopes. Only the first two are answerable here — payroll,
/// workflows and approval scopes are modules that do not exist yet.
///
/// Rather than pretend the check is complete, each of those modules implements this and
/// registers it. Until they do, <c>AssessDeletionAsync</c> answers for assignments alone, and
/// the module README says so, because a deletion check that silently covers less than it claims
/// is how a referenced record gets deleted.
/// </remarks>
public interface IDimensionDeletionBlockerProvider
{
    /// <summary>
    /// Everything this module knows that stands in the way of deleting the record, or an empty
    /// list. A provider reports what it sees and says whether it is live or historical; the
    /// validator decides which of the three outcomes that adds up to.
    /// </summary>
    Task<IReadOnlyList<DeletionBlocker>> GetBlockersAsync(
        string recordId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One reason a record cannot simply be deleted.
/// </summary>
/// <param name="IsHistoricalOnly">
/// True when the reference is only in history — the record is still needed to resolve a past
/// period, but nothing live points at it. That is the difference between retiring the record
/// and refusing outright.
/// </param>
public sealed record DeletionBlocker(DimensionError Error, bool IsHistoricalOnly);
