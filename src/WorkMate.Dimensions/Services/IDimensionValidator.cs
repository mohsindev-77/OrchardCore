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
    /// The same placement rules, asked about a unit that does not exist yet.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> <see cref="ValidatePlacementAsync"/> needs a record id, so a caller
    /// creating a unit and placing it in one go could only ask after it had created something.
    /// <c>IDimensionService.AddUnitAsync</c> did exactly that, and when the placement was refused it
    /// had to delete the unit again — a compensating write on the failure path of a method whose
    /// whole job is to avoid half-finished units. Asking the question first is the fix; this is the
    /// question.
    ///
    /// <b>What it does not check.</b> Cycles. A unit with no id is below nothing and above nothing,
    /// so no parent can be its descendant and the check is vacuous rather than skipped. Everything
    /// else — the parent exists, the containment map permits this type under that one, the parent is
    /// not retired by the effective date, the advisory when it is not effective yet — is the same
    /// code and the same sentences as a move, so the designer, the per-type create screen and the
    /// recipe step all refuse in the same words.
    ///
    /// The record's own rules (code format, uniqueness, names, dates) are
    /// <see cref="ValidateRecordAsync"/>'s; a creating caller asks both.
    /// </remarks>
    /// <param name="dimensionTypeId">The type the unit is about to be created as.</param>
    /// <param name="code">Its code, used only to name it in the messages.</param>
    /// <param name="parentRecordId">The proposed parent, or null to create it as a root.</param>
    Task<IReadOnlyList<DimensionError>> ValidateNewPlacementAsync(
        string structureId,
        string dimensionTypeId,
        string code,
        string? parentRecordId,
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

    /// <summary>
    /// Rules for a record's references of one kind — today, a Position's allowed grades (D26 C).
    /// </summary>
    /// <remarks>
    /// <b>Here rather than on the screen</b>, so the Add/Edit form, the <c>dimension-records</c>
    /// recipe step and slice F6's employee form are all refused in the same words from the same
    /// code. Specification rule 5 and the lesson ADR-0010 was written for.
    ///
    /// Three rules, and each exists because a reference is not a parent and so gets none of the
    /// containment map's protection for free:
    ///
    /// <list type="bullet">
    /// <item><description>the referring record's type must be one this kind belongs to — only a
    /// Position has allowed grades;</description></item>
    /// <item><description>every target must exist;</description></item>
    /// <item><description>every target's type must be one this kind points at — an allowed grade
    /// must be a <see cref="DimensionTypeRole.Grade"/>, which is why that role is declared on the
    /// type rather than guessed from its code or its name.</description></item>
    /// </list>
    ///
    /// <b>A retired target is not refused.</b> Stating the set a position already holds, on a
    /// screen that is about something else, must not fail because one of its grades was retired
    /// last year — the reference is undated and the read filters by date, which is the division
    /// <c>DimensionReferenceKinds</c> sets out.
    /// </remarks>
    Task<IReadOnlyList<DimensionError>> ValidateReferencesAsync(
        string recordId,
        string referenceKind,
        IReadOnlyList<string> targetRecordIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="gradeRecordId"/> is one of the grades
    /// <paramref name="positionRecordId"/> allows, as at a date. Decision D26 G.
    /// </summary>
    /// <remarks>
    /// <b>Built here in slice F5+F7 and called by the employee form in F6.</b> The rule is the
    /// product's, not a screen's: an employee's grade must be one of their position's levels, and a
    /// grade control that is merely <em>populated</em> from the position's set is a control, not a
    /// rule — nothing stops a recipe, an import or a replayed form post from naming another.
    ///
    /// An empty <paramref name="gradeRecordId"/> is refused when the position allows any grades and
    /// accepted when it allows none, because a position with no grades has no level to choose and
    /// demanding one would make it unusable.
    /// </remarks>
    Task<IReadOnlyList<DimensionError>> ValidateEmployeeGradeAsync(
        string positionRecordId,
        string? gradeRecordId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a position says it reports to, and whether that is an answer at all. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>The rule is "say something", not "name somebody".</b> A position may legitimately report
    /// to nobody — the CEO does — and that is expressed, as it always has been, by a placement row
    /// on the reporting-line axis with a null parent. What this refuses is the third state the
    /// screen used to allow: no statement at all, which was being read as "top of the line" while
    /// actually writing no reporting-line row, leaving the post off that axis entirely.
    ///
    /// <b>Stated here so the screen, the recipe and any later import refuse it in the same
    /// words</b> — CLAUDE.md rule 5, and the same shape as
    /// <see cref="ValidateEmployeeGradeAsync"/>: the control that offers the choice is a control,
    /// and only a rule is a rule.
    ///
    /// <b>The recipe is unaffected</b>, which is worth stating because it is the test of whether
    /// this rule is the right one: every position in the Zenith positions recipe already states
    /// its reporting line, the CEO's as <c>"parentCode": null</c>. A rule that broke it would be
    /// a rule about the screen rather than about positions.
    ///
    /// <b>Silent on a tenant with no reporting line.</b> <paramref name="reportingStructureId"/>
    /// null means the axis does not exist — a tenant that has not run the base recipe, or has
    /// renamed it away — and demanding an answer to a question the tenant cannot be asked would
    /// make positions uncreatable there.
    /// </remarks>
    /// <param name="intent">
    /// What the caller said: <see cref="ReportsToIntent.Unstated"/> is the one this refuses.
    /// </param>
    /// <returns>
    /// Errors, which are blocking, and advisories, which are not. A second position at the top of
    /// the same line is an advisory: it is unusual and worth naming, and an organisation that has
    /// two chief executives is entitled to record that it does.
    /// </returns>
    Task<IReadOnlyList<DimensionError>> ValidateReportsToAsync(
        string? reportingStructureId,
        ReportsToIntent intent,
        string? reportsToRecordId,
        DateOnly asAt,
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
/// <param name="GradeRecordId">
/// The grade held at this node, when the node is a position. Optional, and null is what every
/// caller written before slice F6 states — ADR-0026.
/// </param>
public sealed record AssignmentSplitEntry(
    string RecordId,
    decimal AllocationPercent,
    bool IsPrimary,
    string? GradeRecordId = null);

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

/// <summary>
/// What a caller said about a position's place on the reporting line. Decision D31.
/// </summary>
/// <remarks>
/// <b>Three states, where the model has two.</b> Storage has only "has a parent" and "has none",
/// and that is deliberately unchanged — "nobody" is still a placement row with a null parent. The
/// third state exists only at the point of the write, and only so that silence can be told from an
/// answer and refused. Nothing persists it.
/// </remarks>
public enum ReportsToIntent
{
    /// <summary>
    /// The caller said nothing. Refused where a reporting line exists — this is the whole point of
    /// the enum.
    /// </summary>
    Unstated,

    /// <summary>
    /// Explicitly the top of the reporting line: a placement row with a null parent, as the CEO
    /// has today.
    /// </summary>
    Top,

    /// <summary>Explicitly under another position, named by <c>reportsToRecordId</c>.</summary>
    UnderPosition,
}

/// <summary>
/// The value the <em>Reports to</em> control posts when somebody chooses "nobody". Decision D31.
/// </summary>
/// <remarks>
/// <b>A form value, never a stored one.</b> Storage still has only "has a parent" and "has none";
/// this exists so an unanswered control and a deliberate "top of the line" post different things
/// and can be told apart on the server. It is read once, at the controller, and turned into a
/// <see cref="ReportsToIntent"/>.
///
/// Named <c>ReportsToChoice</c> rather than <c>ReportsTo</c> because <c>WorkMate.Records</c> has a
/// <c>ReportsTo</c> of its own — the derived answer shown on an employee — and a view that imported
/// both namespaces would have to disambiguate a name that means two different things.
/// </remarks>
public static class ReportsToChoice
{
    /// <summary>Explicitly the top of the reporting line.</summary>
    public const string Nobody = "__nobody__";

    /// <summary>What the posted control value means. Empty is silence, which D31 refuses.</summary>
    public static ReportsToIntent IntentOf(string? posted) => posted switch
    {
        null or "" => ReportsToIntent.Unstated,
        Nobody => ReportsToIntent.Top,
        _ => ReportsToIntent.UnderPosition,
    };
}
