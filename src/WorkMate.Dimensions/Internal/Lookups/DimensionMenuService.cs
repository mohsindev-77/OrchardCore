using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Internal.Lookups;

/// <inheritdoc cref="IDimensionMenuService" />
/// <remarks>
/// <b>Gathers; it does not decide.</b> Every rule about what becomes an entry, what it reads and
/// which values it lists is in <see cref="DimensionMenuComposition"/>, which is pure and tested
/// without a tenant. This class exists to answer three questions from the database and hand the
/// answers over.
///
/// <b>A fixed number of queries, whatever the tenant holds.</b> The types and the structures come
/// from their cached lookups, so on a warm tenant they cost nothing; the values are one query per
/// shown dimension type, which is the smallest honest number — a dimension's values are exactly
/// what the menu is about. On a cold cache a tenant with nine dimensions pays nine queries once a
/// day, and <see cref="CachedDimensionMenuService"/> is what makes it once rather than per request.
/// </remarks>
internal sealed class DimensionMenuService : IDimensionMenuService
{
    private readonly IDimensionTypeLookup _types;
    private readonly IStructureLookup _structures;
    private readonly IDimensionRecordLookup _records;
    private readonly IDimensionGraphService _graph;
    private readonly IDimensionAuthorisation _authorisation;

    public DimensionMenuService(
        IDimensionTypeLookup types,
        IStructureLookup structures,
        IDimensionRecordLookup records,
        IDimensionGraphService graph,
        IDimensionAuthorisation authorisation)
    {
        _types = types;
        _structures = structures;
        _records = records;
        _graph = graph;
        _authorisation = authorisation;
    }

    public async Task<DimensionMenu> GetAsync(CancellationToken cancellationToken = default)
    {
        var types = await _types.ListAsync(cancellationToken);

        var shown = types
            .Where(type => type.ShowInMenu && type.RetiredOn is null)
            .ToList();

        if (shown.Count == 0)
        {
            return DimensionMenu.Empty;
        }

        var today = await _authorisation.TodayAsync();
        var structures = await _structures.ListAsync(cancellationToken);

        var structuresOfType = new Dictionary<string, IReadOnlyList<StructureRef>>(StringComparer.Ordinal);
        var activeRecordsOfType = new Dictionary<string, IReadOnlyList<DimensionNodeRef>>(StringComparer.Ordinal);

        foreach (var type in shown)
        {
            // By level rather than by what is placed, the same rule the list screen's structure
            // picker follows: a type that is a level of a chart belongs to it whether or not
            // anybody has created a value yet.
            structuresOfType[type.DimensionTypeId] =
            [
                .. structures
                    .Where(structure => structure.OrdinalOf(type.DimensionTypeId) is not null)
                    .OrderBy(structure => structure.Code, StringComparer.Ordinal)
                    .Select(structure => new StructureRef(
                        structure.StructureId, structure.Name, structure.DisplayShortName)),
            ];

            activeRecordsOfType[type.DimensionTypeId] =
            [
                .. (await _records.ListByTypeAsync(type.DimensionTypeId, cancellationToken))
                    .Where(record => record.EffectiveRange.Contains(today)),
            ];
        }

        var parentsOf = await ParentsOfAmbiguousAsync(
            shown, structuresOfType, activeRecordsOfType, today, cancellationToken);

        return DimensionMenuComposition.Compose(types, structuresOfType, activeRecordsOfType, parentsOf);
    }

    /// <summary>
    /// The unit each same-named value sits under, for the values that need one to be told apart.
    /// Decision D24.
    /// </summary>
    /// <remarks>
    /// <b>Only the ambiguous ones, and only ever one query per chart.</b> Crescent has two
    /// departments called <em>Credit</em> and two called <em>Operations</em>; everything else in
    /// the tenant is uniquely named and costs nothing. The candidates are worked out in memory from
    /// data already loaded, then <see cref="IDimensionGraphService.GetAncestorPathsAsync"/> resolves
    /// them all in two queries per structure — the same batched read the list screen's Parent column
    /// uses, never a lookup per value.
    ///
    /// <b>The grouping rule is the menu's, not a second copy of it.</b> Which values are in the same
    /// group comes from <see cref="DimensionMenuComposition.KeyOf"/>, so a change to how dimensions
    /// are grouped cannot leave this resolving parents for the wrong set.
    ///
    /// <b>A type on several charts is skipped</b>, for the reason the organisation bracket is: the
    /// value has a different parent on each, and picking one would be a guess. Such a value keeps
    /// its bare name, which is the state before this existed.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, BilingualText>> ParentsOfAmbiguousAsync(
        IReadOnlyList<DimensionTypeDocument> shown,
        IReadOnlyDictionary<string, IReadOnlyList<StructureRef>> structuresOfType,
        IReadOnlyDictionary<string, IReadOnlyList<DimensionNodeRef>> activeRecordsOfType,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        // Record ids needing a parent, grouped by the one chart their type is on.
        var wanted = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var group in shown.GroupBy(DimensionMenuComposition.KeyOf, StringComparer.Ordinal))
        {
            var members = group.ToList();

            var inGroup = members
                .SelectMany(type => (activeRecordsOfType.GetValueOrDefault(type.DimensionTypeId) ?? [])
                    .Select(record => (type.DimensionTypeId, record)))
                .ToList();

            // Same display name in either language is enough to need a parent in that language; the
            // composition decides per language which readers actually see it.
            var ambiguous = inGroup
                .GroupBy(
                    entry => BilingualText.Display(entry.record.NameEn, entry.record.NameAr, EnglishCulture)
                        + ""
                        + BilingualText.Display(entry.record.NameEn, entry.record.NameAr, ArabicCulture),
                    StringComparer.OrdinalIgnoreCase)
                .Where(named => named.Count() > 1)
                .SelectMany(named => named)
                .ToList();

            foreach (var (dimensionTypeId, record) in ambiguous)
            {
                var structures = structuresOfType.GetValueOrDefault(dimensionTypeId) ?? [];

                if (structures.Count != 1)
                {
                    continue;
                }

                if (!wanted.TryGetValue(structures[0].StructureId, out var ids))
                {
                    wanted[structures[0].StructureId] = ids = [];
                }

                ids.Add(record.RecordId);
            }
        }

        var parents = new Dictionary<string, BilingualText>(StringComparer.Ordinal);

        foreach (var (structureId, recordIds) in wanted)
        {
            var paths = await _graph.GetAncestorPathsAsync(structureId, recordIds, today, cancellationToken);

            foreach (var recordId in recordIds)
            {
                // Root-first, so the immediate parent is the last entry. A value at the top of the
                // chart has none, and keeps its bare name — there is nothing above it to name.
                if ((paths.GetValueOrDefault(recordId) ?? []) is { Count: > 0 } chain)
                {
                    parents[recordId] = new BilingualText(chain[^1].NameEn, chain[^1].NameAr);
                }
            }
        }

        return parents;
    }

    private static readonly System.Globalization.CultureInfo EnglishCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en");

    private static readonly System.Globalization.CultureInfo ArabicCulture =
        System.Globalization.CultureInfo.GetCultureInfo("ar");
}

/// <summary>
/// Caches the Organisation menu for the tenant, invalidated by signal after every write that could
/// change it. ADR-0019, ADR-0024, decision D23 D.
/// </summary>
/// <remarks>
/// <b>Why this one is cached when no other dated read in the module is.</b> The module README's
/// caching section says dated graph queries are not cached, because the date is a parameter and the
/// answers are many. The menu is the exception that proves the rule rather than a departure from
/// it: there is exactly one date it is ever asked about — today — and it is read on <em>every</em>
/// admin request, by a navigation provider that runs before the page it decorates. Rebuilding it
/// per request would put a query per dimension on the critical path of every screen in the product.
///
/// <b>The date is in the key, not in a sliding expiry.</b> A menu built on Monday is a correct
/// answer about Monday and a wrong one about Tuesday, so Tuesday asks a different key and gets a
/// fresh build. An expiry would have the same effect at the cost of being approximately right:
/// somewhere near midnight, for some readers, depending on when the entry happened to be written.
///
/// <b>One signal, three writers.</b> <c>DimensionCacheKeys.MenuSignal</c> is cancelled by
/// <c>DimensionTypeService</c> (a type's name, plural or menu settings), by <c>StructureService</c>
/// (which charts a type is a level of, which decides the organisation suffix) and by
/// <c>DimensionService</c> (a value added, renamed, moved or retired). Coarse on purpose, like every
/// other signal in this module: the menu is one object, so "something that shapes it changed" is the
/// only granularity worth having.
/// </remarks>
internal sealed class CachedDimensionMenuService : IDimensionMenuService
{
    private readonly DimensionMenuService _inner;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    private readonly OrchardCore.Environment.Cache.ISignal _signal;
    private readonly OrchardCore.Environment.Shell.ShellSettings _shellSettings;
    private readonly IDimensionAuthorisation _authorisation;

    public CachedDimensionMenuService(
        DimensionMenuService inner,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        OrchardCore.Environment.Cache.ISignal signal,
        OrchardCore.Environment.Shell.ShellSettings shellSettings,
        IDimensionAuthorisation authorisation)
    {
        _inner = inner;
        _cache = cache;
        _signal = signal;
        _shellSettings = shellSettings;
        _authorisation = authorisation;
    }

    public async Task<DimensionMenu> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = await _authorisation.TodayAsync();

        return await CacheAside.GetOrSetAsync(
            _cache,
            _signal,
            DimensionCacheKeys.Menu(_shellSettings.Name, today.ToIso()),
            DimensionCacheKeys.MenuSignal(_shellSettings.Name),
            () => _inner.GetAsync(cancellationToken));
    }
}
