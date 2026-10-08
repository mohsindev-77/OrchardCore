using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.ViewModels;

/// <summary>One node of the organisation designer's tree, or one row of its unplaced panel.</summary>
/// <remarks>
/// The same shape serves the chart and the list: both views render the same nested markup and
/// differ only in the stylesheet applied to it, so that switching views is a class swap rather
/// than a second rendering path that can drift from the first.
/// </remarks>
public sealed class DesignerNodeViewModel
{
    public string RecordId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string DimensionTypeId { get; set; } = string.Empty;

    public string DimensionTypeNameEn { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    /// <summary>
    /// How many employees sit at this unit on the date being shown, or null when nobody does.
    /// </summary>
    /// <remarks>
    /// Null rather than zero so the card can leave the line out entirely: on a tenant with no
    /// employee records yet — which is every tenant until <c>WorkMate.Records</c> ships — a card
    /// reading "0 employees" on every unit is noise, not information.
    /// </remarks>
    public int? EmployeeCount { get; set; }

    /// <summary>
    /// The headcount as a sentence — "1 employee", "3 employees" — or null when nobody is here.
    /// </summary>
    /// <remarks>
    /// <b>Rendered on the server, including for cards the browser fetches.</b> The script used to
    /// build this from a format string on the surface, which meant one form for every count and a
    /// card reading "1 employees".
    ///
    /// Fixing that in the script was not an option worth taking. English needs two forms and Arabic
    /// needs six, chosen by a rule involving n mod 100 — so a client-side fix is either English-only
    /// or a reimplementation of ICU plural rules in the page. The server already has the localiser
    /// and the culture; it just was not the one doing the formatting. Now it is, and the script sets
    /// the text it is given.
    /// </remarks>
    public string? EmployeeCountLabel { get; set; }

    /// <summary>
    /// The unit's head on the date being shown, or null when the post is vacant.
    /// </summary>
    /// <remarks>
    /// <b>Null means vacant, and the card says so.</b> It used to mean "not built yet" and the card
    /// showed a dash, because no employee could exist; the 5 October backlog note was written about
    /// exactly that distinction, and once head appointments ship a dash would be a claim about the
    /// organisation that nothing had checked.
    ///
    /// Read through <c>IEmployeeAssignmentService.GetHeadsAsync</c> — the same source prompt 5's
    /// approval routing reads — so what the chart shows and what an approval routes to cannot
    /// disagree. A head appointment is its own dated record and not an assignment: ADR-0012.
    /// </remarks>
    public string? HeadDisplayName { get; set; }

    /// <summary>
    /// The head line as the card shows it — "Head: Vacant", or "Head: " and a name.
    /// </summary>
    /// <remarks>
    /// Built on the server for the same reason <see cref="EmployeeCountLabel"/> is: the card is
    /// drawn twice, once in Razor and once in the browser from a cloned template, and a card that
    /// composed its own sentence would need the localiser in both places. One string, computed
    /// where the culture is, read by both.
    ///
    /// <b>Null renders as nothing, and that is the point.</b> The template is rendered from a model
    /// with no record in it, so every text field on it is blank. The head line used to be composed
    /// in the view, which meant the empty template said "Head: Vacant" — a statement about an
    /// organisation, sitting in a card that had not been filled in yet. The script then forgot to
    /// overwrite it, and every fetched card reported every post vacant however many were filled.
    /// A blank placeholder would have been visibly unfinished; a plausible one was not.
    /// </remarks>
    public string? HeadLabel { get; set; }

    /// <summary>
    /// How many units sit directly under this one on the date being shown.
    /// </summary>
    /// <remarks>
    /// Carried on every node so the expand control is the same control everywhere: a unit with
    /// children gets one showing the count, a unit without gets none at all. Deciding that only
    /// after fetching made two cards on the same chart look different from each other.
    /// </remarks>
    public int ChildCount { get; set; }

    /// <summary>
    /// The structure itself, drawn as the chart's top card with the roots beneath it.
    /// </summary>
    /// <remarks>
    /// Not a dimension record and never editable: it carries the structure's name and code so the
    /// chart reads as one organisation rather than as a row of unconnected roots, and so the
    /// connectors above the roots lead somewhere. Later slices hang no action menu off it.
    /// </remarks>
    public bool IsStructure { get; set; }

    /// <summary>
    /// Children rendered by the server rather than fetched. Only the structure card has any: every
    /// unit's children are still loaded on demand when its branch is first expanded.
    /// </summary>
    public List<DesignerNodeViewModel> Children { get; set; } = [];

    /// <summary>
    /// Whether this card carries an action menu. False for every card when the viewer holds no
    /// edit permission, so the menu is never rendered rather than rendered and hidden.
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// Whether this card offers move and cancel-move — specification section 4's
    /// <c>MoveDimensionRecords</c>. Separate from <see cref="CanEdit"/> because reparenting changes
    /// what every historical report under the unit resolves to, which creating one does not.
    /// </summary>
    public bool CanMove { get; set; }

    /// <summary>
    /// Whether this card offers merge — <c>MergeDimensionRecords</c>, its own permission for the
    /// same reason.
    /// </summary>
    public bool CanMerge { get; set; }

    /// <summary>
    /// Whether this card offers to appoint or change the unit's head — <c>AssignEmployees</c>.
    /// </summary>
    /// <remarks>
    /// The same permission that places somebody, not an eighth of its own: appointing a head and
    /// placing an employee are the same kind of act, and splitting them would leave a customer
    /// granting two permissions to achieve one thing.
    /// </remarks>
    public bool CanAssignEmployees { get; set; }

    /// <summary>The structure the card belongs to, which every action link needs.</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>
    /// The date the tree is being shown as at, ISO-8601, so an action returns to the same view.
    /// </summary>
    public string AsAtIso { get; set; } = string.Empty;

    /// <summary>
    /// The name of the parent whose retirement left this unit with nowhere to sit, when that is
    /// why it has none. Null for a unit that was simply never placed.
    /// </summary>
    /// <remarks>
    /// The unplaced panel reads the same from the closure either way — no parent is no parent —
    /// and the two mean entirely different things to the person looking at it. One arrived from an
    /// import and has never been put anywhere; the other was somewhere until its parent closed
    /// underneath it, which somebody decided and which this says out loud.
    /// </remarks>
    public string? OrphanedFromParentName { get; set; }

    /// <inheritdoc cref="OrphanedFromParentName"/>
    public string? OrphanedFromParentNameAr { get; set; }

    /// <summary>
    /// The date that parent retired, as a date rather than as the wire string it travelled as.
    /// </summary>
    /// <remarks>
    /// The badge is a sentence a person reads, so the date in it belongs in their own culture's
    /// format — which is the opposite of the rule for a value on its way to or from the server,
    /// where ISO-8601 is the only reading that means the same day to everyone. Keeping the date
    /// typed here is what lets the view decide that, rather than inheriting a format chosen for a
    /// query string.
    /// </remarks>
    public DateOnly? OrphanedOn { get; set; }

    /// <summary>Whether this unit lost its parent to a retirement rather than never having had one.</summary>
    public bool IsOrphanedByParentRetirement => OrphanedOn is not null;

    /// <summary>
    /// The date somebody deliberately took this unit off the tree, when that is why it has no
    /// parent.
    /// </summary>
    /// <remarks>
    /// The third of the three reasons, readable only since stage D2 gave leaving the tree a dated
    /// entry. Before that the panel told a unit moved off the chart last week that it had never
    /// been placed here, which was not true of it.
    /// </remarks>
    public DateOnly? RemovedFromTreeOn { get; set; }

    /// <inheritdoc cref="RemovedFromTreeOn"/>
    public bool WasRemovedFromTree => RemovedFromTreeOn is not null && !IsOrphanedByParentRetirement;

    /// <summary>
    /// Whether this card is being drawn in the unplaced panel rather than in the tree. Changes
    /// what its action menu offers — a unit with no parent is placed, not moved — and nothing else.
    /// </summary>
    public bool IsUnplaced { get; set; }

    public static DesignerNodeViewModel Of(
        DimensionNodeRef node,
        IReadOnlyDictionary<string, DimensionTypeDocument> typesById,
        IReadOnlyDictionary<string, int>? employeeCounts = null,
        IReadOnlyDictionary<string, int>? childCounts = null,
        string structureId = "",
        string asAtIso = "",
        bool canEdit = false,
        bool canMove = false,
        bool canMerge = false,
        string? headDisplayName = null,
        bool canAssignEmployees = false) => new()
    {
        HeadDisplayName = headDisplayName,
        CanAssignEmployees = canAssignEmployees,
        StructureId = structureId,
        AsAtIso = asAtIso,
        CanEdit = canEdit,
        CanMove = canMove,
        CanMerge = canMerge,
        ChildCount = childCounts is not null && childCounts.TryGetValue(node.RecordId, out var children) ? children : 0,
        RecordId = node.RecordId,
        Code = node.Code,
        NameEn = node.NameEn,
        NameAr = node.NameAr,
        DimensionTypeId = node.DimensionTypeId,
        DimensionTypeNameEn = typesById.TryGetValue(node.DimensionTypeId, out var type)
            ? type.Name.En
            : node.DimensionTypeId,
        IsActive = node.IsActive,
        EmployeeCount = employeeCounts is not null && employeeCounts.TryGetValue(node.RecordId, out var count)
            ? count
            : null,
    };
}

/// <summary>One search hit, with the path the designer's tree must expand to reveal it.</summary>
/// <param name="AncestorRecordIds">The hit's ancestors, root first, excluding the hit itself.</param>
public sealed record DesignerSearchHitViewModel(
    string RecordId,
    string Code,
    string NameEn,
    string NameAr,
    string DimensionTypeNameEn,
    IReadOnlyList<string> AncestorRecordIds);
