using WorkMate.Core;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The position-shaped reads: which positions can be chosen, what grades each allows, and how many
/// of each are approved, filled and vacant. Decisions D2, D26 E, D26 F and D26 G.
/// </summary>
/// <remarks>
/// <b>A reader, not a second aggregate.</b> ADR-0022 models a Position as an ordinary dimension
/// type — no new aggregate, no new table, no new service for the <em>record</em> — and this does not
/// change that. Every write still goes through <see cref="IDimensionService"/>: a position is
/// created and placed by <c>AddUnitAsync</c>, reparented by <c>MoveAsync</c>, retired by
/// <c>RetireAsync</c>, and given its grades by <c>SetReferencesAsync</c>. What is here is the
/// composition of three existing batched reads into the two answers the employee form needs, so the
/// form, the Positions list and the position pickers cannot each compose them slightly differently.
///
/// <b>Built in slice F5+F7, called by the employee form in F6.</b> That is deliberate rather than
/// speculative: D26 F and D26 G specify two drop-downs whose contents are exactly these two reads,
/// and building and testing them a slice early is what lets F6 be a screen rather than a screen and
/// an engine.
///
/// <b>Everything is dated and everything is batched.</b> Dated because "the positions somebody could
/// have been given last March" is a real question and a read with no date is how it quietly starts
/// returning today's answer. Batched because the callers are a page of rows and a drop-down of
/// options, and the rule in this module is that neither asks per item.
/// </remarks>
public interface IPositionService
{
    /// <summary>
    /// The positions that may be chosen on <paramref name="asAt"/>: active, searchable, each with
    /// its unit and its establishment. Decision D26 F.
    /// </summary>
    /// <remarks>
    /// <b>Active only, and the retired ones are not merely sorted last.</b> A position that has
    /// closed is not a post anybody can be put into, and offering it greyed out is offering it.
    /// <see cref="PositionOption.IsFull"/> is the other half of the same judgement and goes the
    /// other way: a full position <em>is</em> offered, with a warning, because refusing it would
    /// stop the product recording what an organisation has actually done.
    ///
    /// <b>Searched on the server by code and by name in both languages</b>, for the reason the unit
    /// and employee pickers are: a control that filters a list it already holds can only narrow what
    /// was sent, so on a tenant with four hundred positions the rest are unreachable and nothing
    /// says so.
    /// </remarks>
    /// <param name="searchText">Null or empty offers the first page unfiltered.</param>
    Task<Page<PositionOption>> SearchSelectableAsync(
        string? searchText,
        DateOnly? asAt = null,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The grades <paramref name="positionRecordId"/> allows on <paramref name="asAt"/>, in the
    /// order somebody chose. Decision D26 G.
    /// </summary>
    /// <remarks>
    /// Empty for a position with no grades, which is a legitimate state and the reason the employee
    /// form's grade control is <em>absent-feeling</em> rather than broken there: D26 G's rule is that
    /// a position with levels requires one, and a position with none requires nothing.
    ///
    /// A grade that has been retired drops out without the reference having been removed, because a
    /// reference is undated and its target is not — see <see cref="DimensionReferenceKinds"/>.
    /// </remarks>
    Task<IReadOnlyList<DimensionNodeRef>> GetAllowedGradesAsync(
        string positionRecordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The grades a tenant has, active on <paramref name="asAt"/> — every value of every dimension
    /// whose role is <see cref="DimensionTypeRole.Grade"/>.
    /// </summary>
    /// <remarks>
    /// What a Position's <em>allowed grades</em> picker offers, where
    /// <see cref="GetAllowedGradesAsync"/> is what an <em>employee's</em> grade picker offers. The
    /// two are deliberately different questions: one is "which grades exist" and the other is "which
    /// of them does this post have as levels", and a control that confused them would let somebody
    /// give an employee a grade their position does not carry.
    /// </remarks>
    Task<Page<DimensionNodeRef>> SearchGradesAsync(
        string? searchText,
        DateOnly? asAt = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approved, filled and vacant for each position, keyed by record id. Decision D26 E.
    /// </summary>
    /// <remarks>
    /// Batched over the whole page in a fixed number of queries — one attribute read for the
    /// approved figures and one headcount read for the filled ones — because this draws three
    /// columns of a list.
    ///
    /// A position absent from the result is one that is not a position at all. One that is a
    /// position and has nobody in it is present, with <c>Filled</c> zero: unlike a headcount, where
    /// absent and zero can mean the same thing to a caller, zero here is the number the Vacant
    /// column is computed from.
    /// </remarks>
    Task<IReadOnlyDictionary<string, PositionEstablishment>> GetEstablishmentAsync(
        IReadOnlyList<string> positionRecordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The dimension types playing <paramref name="role"/> in this tenant, by declared role.
    /// </summary>
    /// <remarks>
    /// Usually one. Several on the demo tenant, where each company ships its own — which is exactly
    /// why this returns a list rather than a single type, and why nothing here reads a code or a
    /// name to find them. See <see cref="DimensionTypeRole"/>.
    /// </remarks>
    Task<IReadOnlyList<DimensionTypeDocument>> GetTypesInRoleAsync(
        DimensionTypeRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The axis a position's <em>reporting line</em> is on, or null if the tenant has none.
    /// </summary>
    /// <remarks>
    /// ADR-0028: a position has two parents and they are on two axes — its unit on the organisation
    /// chart, and the position it reports to here. Resolved by code (<c>reporting-line</c>), which is
    /// what <c>base.recipe.json</c> ships; a tenant that has deleted or renamed it gets null and the
    /// screens say the reporting line is not set up rather than drawing an empty chart.
    /// </remarks>
    Task<StructureDocument?> GetReportingLineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The unit each position sits in, keyed by record id. Absent for one that is not placed.
    /// </summary>
    /// <remarks>
    /// <b>The immediate unit, where <see cref="SearchSelectableAsync"/> gives the whole path.</b> A
    /// drop-down option has room for a path and needs one; a box on a reporting chart has room for a
    /// word, and the word that tells two posts called <em>Supervisor</em> apart is the department.
    ///
    /// Read from whichever organisation axis the position is actually on, not from the primary one:
    /// on the demo tenant each company has its own chart and neither is primary, and a read that
    /// asked only the primary would answer nothing for every position in the tenant.
    /// </remarks>
    Task<IReadOnlyDictionary<string, DimensionNodeRef>> GetUnitsAsync(
        IReadOnlyList<string> positionRecordIds,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="structureId"/> is a reporting line rather than an organisation chart.
    /// </summary>
    /// <remarks>
    /// <b>Asked of the axis's shape, not of its code.</b> The base recipe's is coded
    /// <c>reporting-line</c> and the Zenith demo's is <c>zenith-reporting</c>, for the reason every
    /// demo type is prefixed — a structure's levels are types, so sharing an axis would mean sharing
    /// a Position type and putting one company's posts on the other's chart. So the test is that
    /// every level of the axis is a <see cref="DimensionTypeRole.Position"/>, which is true of a
    /// reporting line and false of any chart that also has departments on it.
    ///
    /// What it decides: whether a position's value page opens here, and whether its boxes carry
    /// filled/approved instead of an employee count (D26 E).
    /// </remarks>
    Task<bool> IsReportingLineAsync(
        string structureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any organisation axis in this tenant will accept a position, and which.
    /// </summary>
    /// <remarks>
    /// <b>The answer to "why can I not place this position?".</b> `base.recipe.json` ships the
    /// Position dimension and no organisation chart (ADR-0028), so a brand-new tenant has positions
    /// that can report to one another and cannot sit anywhere in the business until somebody adds
    /// Position to their structure's levels and ticks what may contain it. Without this read the Add
    /// form offers a <em>Place under</em> picker with nothing in it, which reads as a broken screen
    /// rather than as a step not yet taken.
    /// </remarks>
    Task<PositionPlacementReadiness> GetPlacementReadinessAsync(
        string positionDimensionTypeId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One position as a drop-down offers it: what it is, where it sits, and how full it is.
/// Decision D26 F.
/// </summary>
/// <param name="UnitPath">
/// Where it sits in the business, root first, already joined for display. Empty when the position is
/// not placed on any organisation axis — which is a real state on a tenant that has not built its
/// chart yet, and is shown as such rather than hidden.
/// </param>
public sealed record PositionOption(
    DimensionNodeRef Position,
    string UnitPath,
    PositionEstablishment Establishment)
{
    /// <summary>
    /// Whether choosing this would take the position past its approved headcount. Decision D26 F:
    /// it warns, and it is allowed.
    /// </summary>
    public bool IsFull => Establishment.IsFull;
}

/// <summary>
/// How many of a position are approved, how many are filled, and how many are vacant.
/// Decision D26 E.
/// </summary>
/// <param name="Approved">
/// The approved headcount. One when nobody has said otherwise, because the common case is a post
/// held by one person and a default of zero would make every new position read as over-established
/// the moment somebody was put in it.
/// </param>
/// <param name="Filled">
/// Distinct employees holding the position on the date — not assignment rows, because a split
/// allocation is several rows for one person.
/// </param>
/// <remarks>
/// <b>Undated, and that is a stated limitation rather than an oversight</b> (D15). The approved
/// figure is an attribute, and an attribute has no effective range, so <em>"what was the approved
/// headcount last March"</em> is not answerable. <paramref name="Filled"/> <em>is</em> dated, so the
/// pair is "today's establishment against March's staffing" if somebody asks for March — which is
/// why the limitation is written down here and in the module README rather than designed around
/// quietly.
/// </remarks>
public sealed record PositionEstablishment(int Approved, int Filled)
{
    /// <summary>
    /// Approved minus filled. <b>Negative when a position is over-established, and not clamped</b> —
    /// clamping would hide the one condition the number exists to surface.
    /// </summary>
    public int Vacant => Approved - Filled;

    /// <summary>Whether the post is at or past its approved headcount.</summary>
    public bool IsFull => Filled >= Approved;

    /// <summary>What a position nobody has configured looks like: one approved, nobody in it.</summary>
    public static PositionEstablishment Unfilled { get; } = new(DefaultApproved, 0);

    /// <summary>
    /// The approved headcount a position has when nobody has stated one. Decision D26 E.
    /// </summary>
    public const int DefaultApproved = 1;

    /// <summary>
    /// The attribute the approved headcount is stored in, on the shipped Position dimension type.
    /// </summary>
    /// <remarks>
    /// An attribute rather than a field on <c>DimensionRecordPart</c>, because that part is carried
    /// by <em>every</em> generated dimension content type — a Division and a Cost centre would each
    /// grow a headcount field that means nothing. The name is persisted in a tenant's content
    /// definition, so changing it is a data migration and not a refactor.
    /// </remarks>
    public const string ApprovedHeadcountAttribute = "ApprovedHeadcount";
}

/// <summary>
/// Whether a position can be placed in the business yet, and where. Answers the first question a
/// new tenant asks of the Position screen.
/// </summary>
/// <param name="Structures">
/// The organisation axes that declare Position as a level and will accept one somewhere. Empty on a
/// tenant that has not added Position to its chart.
/// </param>
/// <param name="HasSomewhereToGo">
/// Whether any of those axes actually has a unit a position may sit under, or permits one at the
/// top. An axis that declares the level and no containment pairing is a half-finished configuration
/// and is reported as not ready, because the picker would be empty either way.
/// </param>
public sealed record PositionPlacementReadiness(
    IReadOnlyList<StructureDocument> Structures,
    bool HasSomewhereToGo)
{
    /// <summary>Nothing will take a position yet. The state a brand-new tenant is in.</summary>
    public static PositionPlacementReadiness NotReady { get; } = new([], false);
}
