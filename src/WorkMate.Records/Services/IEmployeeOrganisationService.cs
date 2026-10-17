using WorkMate.Dimensions.Services;

namespace WorkMate.Records.Services;

/// <summary>
/// Where an employee sits in the organisation, derived from the position they hold. Decision D30.
/// </summary>
/// <remarks>
/// <b>One read behind four screens.</b> The employee's Create form, their Edit page, the Placement
/// screen and the Change-position form all show the same block — Position, Grade, the organisation
/// chain and <em>Reports to</em> — and every one of them would otherwise compose it from
/// <c>IPositionService</c>, <c>IEmployeeAssignmentService</c> and <c>IDimensionGraphService</c>
/// slightly differently. That is the mistake <c>IPositionService</c>'s own doc comment describes for
/// the position side, arriving on the employee side.
///
/// <b>Nothing here is stored.</b> D30: the organisation chain is computed from the position's
/// ancestors as at a date and never written on the employee. <c>EmployeeFieldNames</c> has forbidden
/// a department field on the record since prompt 4 and now forbids a position one too — this is the
/// read that makes that affordable.
///
/// <b>A reader, and it writes nothing.</b> The write path is <c>IEmployeeAssignmentService</c>'s
/// <c>PlaceAsync</c> / <c>ReallocateAsync</c>, which is what F6a built and what ADR-0026 put the
/// grade on. A second write path is how a screen and an import come to disagree.
/// </remarks>
public interface IEmployeeOrganisationService
{
    /// <summary>
    /// Where this employee sits on <paramref name="asAt"/>, or null when they are on no axis at all.
    /// </summary>
    Task<EmployeeOrganisation?> GetAsync(
        string employeeId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What holding <paramref name="positionRecordId"/> would mean: the chain and the reporting
    /// line, for a position being <em>considered</em> rather than held.
    /// </summary>
    /// <remarks>
    /// What the Create and Change-position forms preview as somebody picks. The same composition
    /// <see cref="GetAsync"/> makes, from the position rather than from an assignment, so the
    /// preview and the result cannot disagree.
    /// </remarks>
    Task<PositionOrganisation> PreviewAsync(
        string positionRecordId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Who manages this employee on <paramref name="asAt"/>, and on what footing. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// <b>One read path, because a derived answer that is also stored goes stale.</b> The line
    /// manager used to be written at create and at placement in every case, including the cases
    /// the reporting line already answered — so when the holder of the post above changed, or a
    /// vacant post above was filled, the employee kept the old person forever and every screen
    /// showed it. The fix is not a better write: it is to stop storing what can be derived, and to
    /// derive it here, on every read, for every caller.
    ///
    /// <b>What is stored is only what somebody chose</b> — a pick among several holders, a pick
    /// among several acting holders, or an override. One holder and one acting holder are stored
    /// nowhere, because the chart already says who they are.
    ///
    /// <b>Every caller comes through here.</b> The Create form, the employee page, the Placement
    /// screen, the lists and any other module asking "who is X's line manager" get the same
    /// answer, because a second reading of that question is how two screens come to disagree about
    /// one person's manager.
    ///
    /// <b>An employee with no position is returned untouched</b>, with whatever is stored and
    /// <see cref="LineManagerHow.None"/> — or <see cref="LineManagerHow.Stored"/> where there is
    /// one. There is no reporting line to derive from, nothing is an exception to it, and that is
    /// the state every pre-F6 employee and every no-positions tenant is in.
    /// </remarks>
    Task<LineManagerAnswer> GetLineManagerAsync(
        string employeeId,
        DateOnly asAt,
        CancellationToken cancellationToken = default);
}

/// <summary>On what footing somebody is an employee's line manager. D31 addendum 2.</summary>
public enum LineManagerHow
{
    /// <summary>Nobody manages them, and nothing is stored.</summary>
    None,

    /// <summary>
    /// A stored manager on an employee who holds no position: no reporting line to derive from.
    /// </summary>
    /// <remarks>
    /// Every pre-F6 employee and every tenant with no positions. Shown plainly, with no label —
    /// it is not an exception to a line that does not exist.
    /// </remarks>
    Stored,

    /// <summary>The sole holder of the post above. Derived live, stored nowhere.</summary>
    Derived,

    /// <summary>
    /// The sole holder of the nearest held post above a vacant one. Derived live, stored nowhere.
    /// </summary>
    Acting,

    /// <summary>One of several holders of the post above, picked by somebody and stored.</summary>
    Chosen,

    /// <summary>
    /// One of several holders of the acting post, picked while the post above was vacant.
    /// </summary>
    /// <remarks>
    /// Valid only while that post stays vacant. Once it has a holder the choice is about a
    /// situation that has ended, so the live derivation takes over and the stored value is cleared
    /// on the next save rather than being obeyed.
    /// </remarks>
    ChosenActing,

    /// <summary>Set deliberately against the reporting line — a dotted line, a matrix.</summary>
    Override,

    /// <summary>
    /// A stored choice whose person no longer holds the post it was made from. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// <b>Never silently replaced.</b> Falling back to another holder would be the product
    /// choosing a line manager nobody stated — exactly what D28 A refuses — and falling back
    /// silently would hide that the record needs attention. So the stored person is still shown,
    /// named, with what changed, and a choice is asked for.
    /// </remarks>
    NeedsChoice,
}

/// <summary>
/// Who manages an employee, on what footing, and why. D31 addendum 2.
/// </summary>
/// <param name="EmployeeId">The manager's id, or null where there is none.</param>
/// <param name="Name">Their name, resolved, or empty.</param>
/// <param name="PositionName">
/// The post the answer comes from: the post above, or the one an acting manager actually holds.
/// Empty where nothing was derived.
/// </param>
/// <param name="Reason">
/// What a screen needs to say beyond the name — the post somebody no longer holds, for
/// <see cref="LineManagerHow.NeedsChoice"/>. Empty otherwise.
/// </param>
/// <param name="Candidates">
/// Who may be chosen, where a choice is open: the holders of the post above, or of the acting post.
/// Empty where there is nothing to choose.
/// </param>
public sealed record LineManagerAnswer(
    LineManagerHow How,
    string? EmployeeId,
    string Name,
    string PositionName,
    string Reason,
    IReadOnlyList<PositionHolderRef> Candidates)
{
    /// <summary>Nobody, from nothing stored and nothing derived.</summary>
    public static readonly LineManagerAnswer Nobody =
        new(LineManagerHow.None, null, string.Empty, string.Empty, string.Empty, []);

    /// <summary>Whether this answer is one the employee's record holds rather than derives.</summary>
    /// <remarks>
    /// The four that are written down. Everything else is read off the chart every time, which is
    /// the whole point of the addendum: a derived answer that is also stored goes stale.
    /// </remarks>
    public bool IsStored => How is LineManagerHow.Chosen
        or LineManagerHow.ChosenActing
        or LineManagerHow.Override
        or LineManagerHow.Stored;
}

/// <summary>
/// One step of the organisation chain: a unit, and what the tenant calls that kind of unit.
/// </summary>
/// <param name="DimensionName">
/// The tenant's own name for this level — <em>Division</em>, <em>Department</em>, <em>Region</em>.
/// <b>Never a hard-coded word</b>: decision D28 D, because a chart with Regions and Branches is as
/// ordinary as one with Divisions and Departments, and FRS Rule 6 lets a customer rename either.
/// </param>
public sealed record OrganisationStep(string DimensionName, string RecordId, string Name);

/// <summary>How the post above this one on the reporting line is answered. Decision D28 A.</summary>
public enum ReportsToKind
{
    /// <summary>No parent position: the top of the reporting line.</summary>
    Top,

    /// <summary>There is a parent post and nobody holds it.</summary>
    Vacant,

    /// <summary>Exactly one holder, who is named and is offered as the line manager.</summary>
    OneHolder,

    /// <summary>
    /// Several holders, which is the ordinary case under D26 D — <em>HR Manager</em> has three.
    /// </summary>
    /// <remarks>
    /// <b>Nothing picks one.</b> Any rule that chose — longest-serving, highest grade, first
    /// alphabetically — would invent a line manager the customer never stated, and would be wrong
    /// silently. The post is named with its count, and the line-manager picker offers its holders
    /// first. D28 A.
    /// </remarks>
    SeveralHolders,
}

/// <summary>
/// Who a post answers to, and who if anybody holds that post. Decision D28 A.
/// </summary>
/// <param name="Holders">
/// The holders of the parent position, in the order the picker should offer them. Empty unless
/// somebody holds it.
/// </param>
public sealed record ReportsTo(
    ReportsToKind Kind,
    string? PositionRecordId,
    string PositionName,
    IReadOnlyList<PositionHolderRef> Holders,
    ActingAbove? Acting = null)
{
    /// <summary>The top of the line, which is a real answer rather than an absence.</summary>
    public static readonly ReportsTo Top = new(ReportsToKind.Top, null, string.Empty, []);

    /// <summary>
    /// The person to offer as a default line manager, or null when the product must not guess.
    /// </summary>
    /// <remarks>
    /// Exactly one holder is a default and nothing else is. D28 A: with several, the picker lists
    /// them first and the operator chooses; with none, the field is left optional.
    /// </remarks>
    public PositionHolderRef? DefaultLineManager =>
        Kind == ReportsToKind.OneHolder ? Holders[0] : null;
}

/// <summary>One holder of a position, named. The employee side of <c>PositionHolder</c>.</summary>
public sealed record PositionHolderRef(string EmployeeId, string Name, string GradeName);

/// <summary>
/// The nearest post <em>above a vacant one</em> that somebody actually holds. D31 addendum.
/// </summary>
/// <remarks>
/// <b>What a vacancy means in practice.</b> A post whose parent is empty does not thereby answer to
/// nobody — it answers to whoever is above the gap, which is how an organisation works while a
/// manager's chair is empty. Saying "nobody" there was not a simplification but a false statement:
/// Software Engineer reports to Manager Technology, and with that post vacant the engineer answers
/// to the Director above it, not to no one.
///
/// <b>Derived, never stored.</b> Walking up is a read over the reporting line, recomputed on every
/// render like the rest of the block. What gets stored when somebody saves is the same thing as
/// always — the chosen person in <c>LineManagerEmployeeId</c> — so an acting manager is a person on
/// the record, not a second kind of fact the model has to carry.
///
/// <b>It stops at the first post with holders</b>, however far up that is, and is absent when
/// nothing above is held all the way to the top: a chain of empty posts has no one to act, and
/// saying so is the honest answer.
/// </remarks>
/// <param name="PositionName">The post the acting manager actually holds, for the "acting" label.</param>
public sealed record ActingAbove(string PositionName, IReadOnlyList<PositionHolderRef> Holders);

/// <summary>
/// What holding a position means: where it sits, and who it answers to.
/// </summary>
/// <param name="Chain">
/// The units above the position, <b>root first</b> — <em>Zenith › Corporate › HR</em>. Empty when
/// the position is not placed on any organisation axis, which is a real state on a tenant that has
/// not finished its chart and is shown as such rather than hidden.
/// </param>
public sealed record PositionOrganisation(
    string PositionRecordId,
    string PositionName,
    IReadOnlyList<OrganisationStep> Chain,
    ReportsTo ReportsTo,
    PositionEstablishment? Establishment)
{
    /// <summary>Whether the post is at or over its approved headcount. D26 F: a warning, never a block.</summary>
    public bool IsFull => Establishment?.IsFull == true;
}

/// <summary>
/// Where one employee sits on one date, derived from what they hold.
/// </summary>
/// <param name="Position">
/// What they hold and what it means. <b>Null when they hold no position</b> — which on a tenant
/// mid-migration is every employee placed directly in a unit before F6 (ADR-0029), and is the state
/// <paramref name="LegacyUnitPath"/> describes.
/// </param>
/// <param name="LegacyUnitPath">
/// Where they sit on an axis that has positions, without holding one. The <em>"No position yet"</em>
/// state D28 C requires be made legible rather than hidden. Empty otherwise.
/// </param>
public sealed record EmployeeOrganisation(
    string EmployeeId,
    string StructureId,
    PositionOrganisation? Position,
    string? GradeRecordId,
    string GradeName,
    DateOnly? Since,
    string LegacyUnitPath)
{
    /// <summary>
    /// True for somebody on a position-based axis who does not hold a position. ADR-0029's
    /// mid-migration state, and the one the <em>Choose position</em> action acts on.
    /// </summary>
    public bool HasNoPositionYet => Position is null && !string.IsNullOrEmpty(LegacyUnitPath);
}
