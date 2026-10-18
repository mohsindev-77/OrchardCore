using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <inheritdoc />
/// <remarks>
/// <b>A composition of reads that already exist, and deliberately nothing more.</b>
/// <c>GetAllEffectiveAsync</c> for what they hold, <c>GetAncestorPathsAsync</c> for the chain,
/// <c>GetHoldersAsync</c> for the post above, and <c>IEmployeeLookup</c> for the names. What this
/// adds is one place they are put together, so four screens cannot answer "where does this person
/// sit" four slightly different ways.
///
/// <b>Batched where a loop would be per-row, and not otherwise.</b> This answers about one employee
/// or one position, so the reads it makes are bounded by the number of axes a tenant has — one on a
/// real tenant, one per company on the demo one. The screens that ask it for a <em>page</em> of
/// employees are F6c's, and they will need a batched variant rather than this in a loop.
/// </remarks>
internal sealed class EmployeeOrganisationService : IEmployeeOrganisationService
{
    private readonly IPositionService _positions;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IDimensionService _dimensions;
    private readonly IDimensionTypeService _types;
    private readonly IDimensionGraphService _graph;
    private readonly IEmployeeLookup _employees;

    /// <summary>The employee record itself, for what it stores about its line manager. D31 addendum 2.</summary>
    private readonly IEmployeeService _records;

    public EmployeeOrganisationService(
        IPositionService positions,
        IEmployeeAssignmentService assignments,
        IDimensionService dimensions,
        IDimensionTypeService types,
        IDimensionGraphService graph,
        IEmployeeLookup employees,
        IEmployeeService records)
    {
        _positions = positions;
        _assignments = assignments;
        _dimensions = dimensions;
        _types = types;
        _graph = graph;
        _employees = employees;
        _records = records;
    }

    public async Task<EmployeeOrganisation?> GetAsync(
        string employeeId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        foreach (var axis in await _positions.GetEmployeeAxesAsync(cancellationToken))
        {
            var rows = await _assignments.GetAllEffectiveAsync(
                employeeId, axis.StructureId, asAt, cancellationToken);

            // The main placement is the one that answers "where does this person work". A split is
            // several rows and exactly one of them is primary, which is what keeps a matrix case
            // unambiguous — architecture §2.5.
            var main = rows.FirstOrDefault(row => row.IsPrimary) ?? (rows.Count > 0 ? rows[0] : null);

            if (main is null)
            {
                continue;
            }

            var node = await _dimensions.GetAsync(main.RecordId, asAt, cancellationToken);

            if (node is null)
            {
                continue;
            }

            var isPosition = await IsAPositionAsync(node.DimensionTypeId, cancellationToken);

            if (!isPosition)
            {
                // Placed at a unit. On a position-based axis that is ADR-0029's mid-migration
                // state — kept, valid, and labelled "No position yet" rather than hidden. On an
                // axis with no positions it is simply how that tenant works, and D30 does not
                // apply to it.
                var path = await PathAsync(axis.StructureId, main.RecordId, asAt, cancellationToken);

                return new EmployeeOrganisation(
                    employeeId,
                    axis.StructureId,
                    Position: null,
                    GradeRecordId: null,
                    GradeName: string.Empty,
                    main.Range.From,
                    axis.IsPositionBased
                        ? string.Join(
                            BilingualDisplay.PathSeparator,
                            path.Select(step => step.Name).Append(
                                BilingualDisplay.Name(node.NameEn, node.NameAr)))
                        : string.Empty);
            }

            var grade = main.GradeRecordId is null
                ? null
                // Undated, so a grade later removed from the position still names itself on a row
                // that carries it — D26 G, and the same read GetHoldersAsync makes.
                : await _dimensions.GetAsync(main.GradeRecordId, DateOnly.MaxValue, cancellationToken);

            return new EmployeeOrganisation(
                employeeId,
                axis.StructureId,
                await PreviewAsync(main.RecordId, asAt, cancellationToken),
                main.GradeRecordId,
                grade is null ? string.Empty : BilingualDisplay.Name(grade.NameEn, grade.NameAr),
                main.Range.From,
                LegacyUnitPath: string.Empty);
        }

        return null;
    }

    public async Task<PositionOrganisation> PreviewAsync(
        string positionRecordId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        var position = await _dimensions.GetAsync(positionRecordId, asAt, cancellationToken);

        var name = position is null
            ? string.Empty
            : BilingualDisplay.Name(position.NameEn, position.NameAr);

        var chain = new List<OrganisationStep>();

        foreach (var axis in await _positions.GetEmployeeAxesAsync(cancellationToken))
        {
            var path = await PathAsync(axis.StructureId, positionRecordId, asAt, cancellationToken);

            if (path.Count > 0)
            {
                chain = [.. path];

                break;
            }
        }

        var establishment = (await _positions.GetEstablishmentAsync(
            [positionRecordId], asAt, cancellationToken)).GetValueOrDefault(positionRecordId);

        return new PositionOrganisation(
            positionRecordId,
            name,
            chain,
            await ReportsToAsync(positionRecordId, asAt, cancellationToken),
            establishment);
    }

    /// <summary>
    /// The post above this one on the reporting line, and who holds it. Decision D28 A.
    /// </summary>
    /// <remarks>
    /// Four answers and no guessing. One holder is a default line manager; several are offered in
    /// the picker and none is chosen for the operator; vacant names the post so somebody can act on
    /// it; and no parent is the top of the line, which is a fact rather than a gap.
    /// </remarks>
    private async Task<ReportsTo> ReportsToAsync(
        string positionRecordId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        // EVERY reporting axis, not the one base.recipe.json ships by code. A demo tenant has
        // Zenith's zenith-reporting as well, prefixed for the reason every demo type is
        // (ADR-0028) — and asking only base's empty axis answered "top of the reporting line" for
        // every post in the company, which is the plausible-and-wrong shape architecture §4 warns
        // about. The same loop GetUnitsAsync makes for the organisation side.
        IReadOnlyList<DimensionNodeRef> ancestors = [];

        foreach (var reporting in await _positions.GetReportingAxesAsync(cancellationToken))
        {
            var found = (await _graph.GetAncestorPathsAsync(
                reporting.StructureId, [positionRecordId], asAt, cancellationToken))
                .GetValueOrDefault(positionRecordId) ?? [];

            if (found.Count > 0)
            {
                ancestors = found;

                break;
            }
        }

        if (ancestors.Count == 0)
        {
            return ReportsTo.Top;
        }

        // Nearest last: GetAncestorPathsAsync returns root first, so the post this one reports to
        // is the end of the list.
        var parent = ancestors[^1];
        var parentName = BilingualDisplay.Name(parent.NameEn, parent.NameAr);

        var holders = await _positions.GetHoldersAsync(parent.RecordId, asAt, cancellationToken: cancellationToken);

        if (holders.Items.Count == 0)
        {
            // THE POST IS VACANT, WHICH IS NOT THE SAME AS ANSWERING TO NOBODY. D31 addendum:
            // while a manager's chair is empty the people under it answer to whoever is above the
            // gap, so the walk continues upwards and names them as acting. Only a chain of empty
            // posts all the way to the top really leaves nobody.
            var acting = await ActingAboveAsync(ancestors, asAt, cancellationToken);

            return new ReportsTo(
                ReportsToKind.Vacant,
                parent.RecordId,
                parentName,
                [],
                acting,

                // Only when the walk found nobody. Somebody IS acting means there is nothing
                // outstanding to name; a chain of empty chairs is a list of posts to go and fill,
                // and "No one above holds a post yet" never said which. Slice F6c.
                acting is null ? NamesOf(ancestors) : []);
        }

        return new ReportsTo(
            holders.Total == 1 ? ReportsToKind.OneHolder : ReportsToKind.SeveralHolders,
            parent.RecordId,
            parentName,
            await NameHoldersAsync(holders.Items, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<LineManagerAnswer> GetLineManagerAsync(
        string employeeId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        var stored = await _records.GetAsync(employeeId, cancellationToken);
        var organisation = await GetAsync(employeeId, asAt, cancellationToken);

        return await AnswerAsync(
            stored?.LineManagerEmployeeId,
            stored?.LineManagerKind ?? StoredLineManagerKind.None,
            organisation?.Position?.ReportsTo,
            cancellationToken);
    }

    /// <summary>
    /// The answer itself, from facts a caller has already resolved. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// <b>Split out so there is still exactly one of these rules, now that two callers resolve the
    /// facts differently.</b> <see cref="GetLineManagerAsync"/> reads one employee's record and one
    /// employee's position; <see cref="GetLinesAsync"/> reads a page of both at once. If each then
    /// applied the staleness rules itself, the employee's own page and the list she appears on
    /// would be two readings of "who manages this person" — which is the defect the addendum
    /// exists to close, arriving by a different door.
    /// </remarks>
    /// <param name="reportsTo">
    /// The post above the position this employee holds, or <c>null</c> when they hold none.
    /// </param>
    private async Task<LineManagerAnswer> AnswerAsync(
        string? storedId,
        StoredLineManagerKind kind,
        ReportsTo? reportsTo,
        CancellationToken cancellationToken)
    {
        if (reportsTo is null)
        {
            // NO POSITION, SO NOTHING TO DERIVE FROM. Every pre-F6 employee and every tenant with
            // no positions is here: the stored manager is the only answer there is, and it is not
            // an exception to a reporting line that does not exist.
            return string.IsNullOrEmpty(storedId)
                ? LineManagerAnswer.Nobody()
                : await StoredAnswerAsync(
                    LineManagerHow.Stored, storedId, string.Empty, string.Empty, [], cancellationToken);
        }

        // Whose holders this is about: the post above, or — while that post is vacant — the
        // nearest held post above the gap, whose holders are acting.
        var acting = reportsTo.Kind == ReportsToKind.Vacant ? reportsTo.Acting : null;
        var candidates = acting?.Holders ?? reportsTo.Holders;
        var postName = acting?.PositionName ?? reportsTo.PositionName;

        // AN ACTING CHOICE WHOSE SITUATION HAS ENDED. It was made while the post above was vacant
        // and is valid only while it stays vacant; the post is held again, so the stand-in stands
        // down. Nothing is wrong and nobody needs telling — the real manager is back — so the
        // stored value is simply not obeyed and the live derivation below answers instead. The
        // next save clears it; a read must not write, or every list view becomes a write path.
        //
        // This is why the kind is stored rather than inferred: a stale acting choice and a stale
        // deliberate choice both read as "not a holder of the post above", and the product must
        // ignore the first quietly and ask about the second loudly.
        if (kind == StoredLineManagerKind.ChosenActing && acting is null)
        {
            storedId = null;
        }

        // An override is the one thing the line cannot tell us, so it is the one thing stored as a
        // kind. It outranks everything below: a dotted line is deliberate and survives the post
        // above changing hands, being filled, or emptying.
        if (kind == StoredLineManagerKind.Override && !string.IsNullOrEmpty(storedId))
        {
            return await StoredAnswerAsync(
                LineManagerHow.Override, storedId, postName, string.Empty, candidates, cancellationToken);
        }

        if (!string.IsNullOrEmpty(storedId))
        {
            var stillAHolder = candidates.Any(holder => string.Equals(
                holder.EmployeeId, storedId, StringComparison.Ordinal));

            if (stillAHolder)
            {
                // A choice that still stands. Acting choices are marked so the screen can say the
                // post above is still vacant, and so a later save knows to drop it once it is not.
                return await StoredAnswerAsync(
                    acting is null ? LineManagerHow.Chosen : LineManagerHow.ChosenActing,
                    storedId,
                    postName,
                    string.Empty,
                    candidates,
                    cancellationToken);
            }

            // A CHOICE ABOUT A SITUATION THAT HAS ENDED. Two shapes, and they are not the same.
            //
            // The post above was vacant and now is not: the acting arrangement is over, so the
            // choice is simply obeyed no longer and the live derivation takes over. Nothing is
            // wrong and nobody needs telling — the real manager is back.
            //
            // Otherwise the person was picked from this post's holders and has left it, which
            // nothing can answer for: falling back to another holder would be the product choosing
            // a line manager nobody stated. The record is shown as needing a new one, by name.
            if (acting is null && reportsTo.Kind == ReportsToKind.Vacant)
            {
                // Vacant with nobody above to act, and a stored person who holds neither. Treated
                // as needing a choice for the same reason: it was somebody's answer and is not now.
                return await NeedsChoiceAsync(
                    storedId, reportsTo.PositionName, candidates, cancellationToken);
            }

            if (reportsTo.Kind is ReportsToKind.OneHolder or ReportsToKind.SeveralHolders ||
                acting is not null)
            {
                return await NeedsChoiceAsync(storedId, postName, candidates, cancellationToken);
            }
        }

        // Nothing stored, or a stored acting choice whose situation has ended: derive live.
        return reportsTo.Kind switch
        {
            ReportsToKind.Top => LineManagerAnswer.Nobody(),

            // Vacant with nobody above holding anything: the posts exist and are empty.
            ReportsToKind.Vacant when acting is null => LineManagerAnswer.Nobody() with
            {
                PositionName = reportsTo.PositionName,

                // Slice F6c: which chairs are empty, not merely that they are. The read-only
                // screens say the same sentence the forms do because they read the same answer.
                VacantAbove = reportsTo.VacantAbove ?? [],
            },

            _ => candidates.Count == 1
                ? new LineManagerAnswer(
                    acting is null ? LineManagerHow.Derived : LineManagerHow.Acting,
                    candidates[0].EmployeeId,
                    candidates[0].Name,
                    postName,
                    string.Empty,
                    candidates)

                // D28 A: several hold it and nothing picks between them. Nobody has chosen yet.
                : new LineManagerAnswer(
                    LineManagerHow.NeedsChoice,
                    null,
                    string.Empty,
                    postName,
                    string.Empty,
                    candidates),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, EmployeeOrganisationLine>> GetLinesAsync(
        IReadOnlyList<string> employeeIds,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        var lines = new Dictionary<string, EmployeeOrganisationLine>(StringComparer.Ordinal);
        var outstanding = employeeIds.Distinct(StringComparer.Ordinal).ToList();

        if (outstanding.Count == 0)
        {
            return lines;
        }

        // WHO HAS A STORED LINE MANAGER AT ALL — one query, from the index. The record's content
        // item is then loaded only for those few, because the KIND of answer it is lives there and
        // not on the index (D31 addendum 2). Asking every row's item would be the N+1 this whole
        // method exists to avoid; asking none of them would make the list disagree with the
        // employee's own page about an override.
        var storedIds = (await _records.GetManyAsync(outstanding, cancellationToken))
            .Where(entry => !string.IsNullOrEmpty(entry.Value.LineManagerEmployeeId))
            .ToDictionary(entry => entry.Key, entry => entry.Value.LineManagerEmployeeId, StringComparer.Ordinal);

        foreach (var axis in await _positions.GetEmployeeAxesAsync(cancellationToken))
        {
            if (outstanding.Count == 0)
            {
                break;
            }

            // ONE QUERY FOR EVERYBODY'S PLACEMENTS ON THIS AXIS. The per-employee read this
            // replaces is GetAllEffectiveAsync, which GetAsync still makes because it answers
            // about one person.
            var placements = await _assignments.GetPlacementsAsync(
                axis.StructureId, outstanding, asAt, cancellationToken);

            if (placements.Count == 0)
            {
                continue;
            }

            // The main placement is the one that answers "where does this person work" — exactly
            // the rule GetAsync applies, because a list and a page disagreeing about which of a
            // split is the real one is the defect EmployeePlacementSummary was introduced for.
            var mains = new Dictionary<string, EmployeeAssignment>(StringComparer.Ordinal);

            foreach (var (employeeId, rows) in placements)
            {
                var main = rows.FirstOrDefault(row => row.IsPrimary) ?? (rows.Count > 0 ? rows[0] : null);

                if (main is not null)
                {
                    mains[employeeId] = main;
                }
            }

            var nodes = await NodesAsync(
                [.. mains.Values.Select(main => main.RecordId)], asAt, cancellationToken);

            var grades = await GradesAsync(
                [.. mains.Values.Select(main => main.GradeRecordId).Where(id => id is not null)!],
                cancellationToken);

            // Bounded by DISTINCT POSITIONS, which is the whole point: fifty people in one
            // department cost what one of them costs.
            var positionIds = nodes
                .Where(node => node.Value.IsPosition)
                .Select(node => node.Key)
                .ToList();

            var chains = await PathsAsync(axis.StructureId, [.. nodes.Keys], asAt, cancellationToken);
            var reportsTo = new Dictionary<string, ReportsTo>(StringComparer.Ordinal);

            foreach (var positionId in positionIds)
            {
                reportsTo[positionId] = await ReportsToAsync(positionId, asAt, cancellationToken);
            }

            foreach (var (employeeId, main) in mains)
            {
                if (!nodes.TryGetValue(main.RecordId, out var node))
                {
                    continue;
                }

                var chain = chains.GetValueOrDefault(main.RecordId) ?? [];
                var above = node.IsPosition ? reportsTo.GetValueOrDefault(main.RecordId) : null;

                // The one read still made per row, and only for somebody who HAS a stored line
                // manager: what kind of answer it is lives on the content item, not on the index.
                var stored = storedIds.ContainsKey(employeeId)
                    ? await _records.GetAsync(employeeId, cancellationToken)
                    : null;

                lines[employeeId] = new EmployeeOrganisationLine(
                    employeeId,
                    axis.StructureId,
                    node.IsPosition ? main.RecordId : null,
                    node.IsPosition ? node.Name : string.Empty,
                    main.GradeRecordId is { } gradeId
                        ? grades.GetValueOrDefault(gradeId, string.Empty)
                        : string.Empty,
                    node.IsPosition ? chain : [],
                    node.IsPosition || !axis.IsPositionBased
                        ? string.Empty

                        // ADR-0029's mid-migration state, named rather than hidden: the whole path
                        // including the unit itself, because "No position yet — Civil" is what the
                        // reader has to act on.
                        : string.Join(
                            BilingualDisplay.PathSeparator,
                            chain.Select(step => step.Name).Append(node.Name)),
                    await AnswerAsync(
                        stored?.LineManagerEmployeeId,
                        stored?.LineManagerKind ?? StoredLineManagerKind.None,
                        above,
                        cancellationToken));
            }

            outstanding = [.. outstanding.Where(id => !lines.ContainsKey(id))];
        }

        return lines;
    }

    /// <summary>Each distinct node, named, and whether it is a position. One read per node.</summary>
    private async Task<Dictionary<string, (string Name, bool IsPosition)>> NodesAsync(
        IReadOnlyList<string> recordIds,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var nodes = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);

        foreach (var recordId in recordIds.Distinct(StringComparer.Ordinal))
        {
            var node = await _dimensions.GetAsync(recordId, asAt, cancellationToken);

            if (node is null)
            {
                continue;
            }

            nodes[recordId] = (
                BilingualDisplay.Name(node.NameEn, node.NameAr),
                await IsAPositionAsync(node.DimensionTypeId, cancellationToken));
        }

        return nodes;
    }

    /// <summary>Each distinct grade, named. Undated, for the reason <see cref="GetAsync"/> gives.</summary>
    private async Task<Dictionary<string, string>> GradesAsync(
        IReadOnlyList<string> gradeRecordIds,
        CancellationToken cancellationToken)
    {
        var grades = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var gradeId in gradeRecordIds.Distinct(StringComparer.Ordinal))
        {
            // Undated, so a grade later removed from the position still names itself on a row that
            // carries it — D26 G.
            if (await _dimensions.GetAsync(gradeId, DateOnly.MaxValue, cancellationToken) is { } grade)
            {
                grades[gradeId] = BilingualDisplay.Name(grade.NameEn, grade.NameAr);
            }
        }

        return grades;
    }

    /// <summary>
    /// <see cref="PathAsync"/> for a whole page: one ancestor query, one read per distinct level.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<OrganisationStep>>> PathsAsync(
        string structureId,
        IReadOnlyList<string> recordIds,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var ancestors = await _graph.GetAncestorPathsAsync(
            structureId, [.. recordIds.Distinct(StringComparer.Ordinal)], asAt, cancellationToken);

        // The tenant's OWN name for each level — D28 D — read once per distinct dimension type
        // rather than once per step of every path. A chart has a handful of levels and a page has
        // hundreds of steps.
        var levelNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new Dictionary<string, IReadOnlyList<OrganisationStep>>(StringComparer.Ordinal);

        foreach (var (recordId, chain) in ancestors)
        {
            var steps = new List<OrganisationStep>();

            foreach (var ancestor in chain)
            {
                if (!levelNames.TryGetValue(ancestor.DimensionTypeId, out var levelName))
                {
                    var type = await _types.GetAsync(ancestor.DimensionTypeId, asAt, cancellationToken);

                    levelName = type is null ? string.Empty : BilingualText.Display(type.Name.En, type.Name.Ar);
                    levelNames[ancestor.DimensionTypeId] = levelName;
                }

                steps.Add(new OrganisationStep(
                    levelName,
                    ancestor.RecordId,
                    BilingualDisplay.Name(ancestor.NameEn, ancestor.NameAr)));
            }

            paths[recordId] = steps;
        }

        return paths;
    }

    /// <summary>An answer built around a person the record holds, with their name resolved.</summary>
    private async Task<LineManagerAnswer> StoredAnswerAsync(
        LineManagerHow how,
        string employeeId,
        string positionName,
        string reason,
        IReadOnlyList<PositionHolderRef> candidates,
        CancellationToken cancellationToken) =>
        new(how,
            employeeId,
            await NameOfAsync(employeeId, cancellationToken),
            positionName,
            reason,
            candidates);

    /// <summary>
    /// A stored choice whose person has left the post it was made from. D31 addendum 2.
    /// </summary>
    /// <remarks>
    /// The person is still named, because "somebody has to look at this" is more useful than a
    /// blank, and because silently swapping them for another holder would be the product inventing
    /// a line manager — the thing D28 A exists to refuse.
    /// </remarks>
    private async Task<LineManagerAnswer> NeedsChoiceAsync(
        string employeeId,
        string positionName,
        IReadOnlyList<PositionHolderRef> candidates,
        CancellationToken cancellationToken)
    {
        var name = await NameOfAsync(employeeId, cancellationToken);

        return new LineManagerAnswer(
            LineManagerHow.NeedsChoice,
            employeeId,
            name,
            positionName,
            name,
            candidates);
    }

    private async Task<string> NameOfAsync(string employeeId, CancellationToken cancellationToken) =>
        await _employees.GetAsync(employeeId, cancellationToken) is { } person
            ? person.ForCulture()
            : employeeId;

    /// <summary>
    /// The nearest post above a vacant one that somebody holds, or null. D31 addendum.
    /// </summary>
    /// <remarks>
    /// <paramref name="ancestors"/> is root first, so this walks it from the second-nearest
    /// outwards — the nearest is the vacant post itself, already asked about — and stops at the
    /// first post with anybody on it.
    ///
    /// <b>One read per level, and the loop is short by construction.</b> A reporting line is as
    /// deep as an organisation has layers of management; the demo's is four. It stops at the first
    /// hit, so the common case of a gap one level deep costs one read.
    /// </remarks>
    private async Task<ActingAbove?> ActingAboveAsync(
        IReadOnlyList<DimensionNodeRef> ancestors,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        for (var index = ancestors.Count - 2; index >= 0; index--)
        {
            var above = ancestors[index];

            var holders = await _positions.GetHoldersAsync(
                above.RecordId, asAt, cancellationToken: cancellationToken);

            if (holders.Items.Count == 0)
            {
                continue;
            }

            return new ActingAbove(
                BilingualDisplay.Name(above.NameEn, above.NameAr),
                await NameHoldersAsync(holders.Items, cancellationToken));
        }

        // Nothing above is held, all the way to the top. A real state on a tenant whose chart is
        // drawn and whose posts are not yet filled, and said as such rather than as "nobody".
        return null;
    }

    /// <summary>
    /// The posts above a vacant one, nearest first, named. Slice F6c.
    /// </summary>
    /// <remarks>
    /// <b>Nearest first, which reverses the ancestor order</b> — <c>GetAncestorPathsAsync</c>
    /// returns root first — because a reader is told about the empty chair closest to them before
    /// the one at the top. The post directly above is included: it is the vacancy the sentence is
    /// about, and leaving it out would name every empty post except the one that started it.
    /// </remarks>
    private static IReadOnlyList<string> NamesOf(IReadOnlyList<DimensionNodeRef> ancestors) =>
    [
        .. ancestors
            .Reverse()
            .Select(above => BilingualDisplay.Name(above.NameEn, above.NameAr)),
    ];

    /// <summary>Puts names and grades on a set of holders, in one read.</summary>
    private async Task<IReadOnlyList<PositionHolderRef>> NameHoldersAsync(
        IReadOnlyList<PositionHolder> holders,
        CancellationToken cancellationToken)
    {
        var people = await _employees.GetManyAsync(
            [.. holders.Select(holder => holder.EmployeeId)], cancellationToken);

        return
        [
            .. holders.Select(holder => new PositionHolderRef(
                holder.EmployeeId,
                people.TryGetValue(holder.EmployeeId, out var person)
                    ? person.ForCulture()
                    : holder.EmployeeId,
                holder.Grade is { } grade
                    ? BilingualDisplay.Name(grade.NameEn, grade.NameAr)
                    : string.Empty)),
        ];
    }

    /// <summary>The units above a node on one axis, root first, each labelled with its dimension.</summary>
    private async Task<IReadOnlyList<OrganisationStep>> PathAsync(
        string structureId,
        string recordId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var ancestors = (await _graph.GetAncestorPathsAsync(
            structureId, [recordId], asAt, cancellationToken)).GetValueOrDefault(recordId) ?? [];

        var steps = new List<OrganisationStep>();

        foreach (var ancestor in ancestors)
        {
            var type = await _types.GetAsync(ancestor.DimensionTypeId, asAt, cancellationToken);

            steps.Add(new OrganisationStep(
                type is null ? string.Empty : BilingualText.Display(type.Name.En, type.Name.Ar),
                ancestor.RecordId,
                BilingualDisplay.Name(ancestor.NameEn, ancestor.NameAr)));
        }

        return steps;
    }

    private async Task<bool> IsAPositionAsync(string dimensionTypeId, CancellationToken cancellationToken) =>
        await _types.GetAsync(dimensionTypeId, cancellationToken: cancellationToken) is
            { StandardRole: Dimensions.Models.DimensionTypeRole.Position };
}
