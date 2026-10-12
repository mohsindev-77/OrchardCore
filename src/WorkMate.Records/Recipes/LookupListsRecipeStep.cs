using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Platform.Recipes;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;

namespace WorkMate.Records.Recipes;

/// <summary>
/// The <c>lookup-lists</c> recipe step: the managed lists a choice field offers, by code.
/// </summary>
/// <remarks>
/// <b>Everything is validated before anything is written.</b> Codes, duplicate codes within the
/// file, and every existing list's comparison are all checked across the whole step first, so a
/// file with a typo in its last list leaves the tenant exactly as it found it — the shape all five
/// of this product's other steps take.
///
/// <b>Re-running is safe, per ADR-0008.</b> A row naming a code that already exists is compared
/// against the tenant on the fields the row actually states, and skipped with nothing written if
/// they all match; the step fails, naming the list and what differs, if any does not.
///
/// <b>Two asymmetries in that comparison, both following ADR-0008 rather than widening it.</b>
///
/// An entry the tenant holds and the file does not mention is <em>not</em> a difference. ADR-0008's
/// rule is that comparison is scoped to what a row actually states — "a row with no
/// <c>attributes</c> key says nothing about attributes rather than claiming there are none" — and a
/// list a customer has added their own entries to since the pack shipped is exactly that case. The
/// file is not claiming those entries do not exist.
///
/// An entry the file states and the tenant does <em>not</em> hold <b>is</b> a difference, and the
/// step fails. That is ADR-0008 applied literally — "an attribute the row does state and the tenant
/// does not hold is a difference, because that is the row making a claim the tenant contradicts" —
/// and it has a cost worth naming: a second version of a pack that adds an entry to a list a tenant
/// already has cannot be applied over it, and the operator has to add the entry through the screen.
/// The alternative, filling the gap in silently, is the behaviour ADR-0008 exists to refuse, and
/// the one case where it was allowed (the <c>employeeAttachableTypeCodes</c> addendum) turned on
/// the model documenting empty as an absence rather than as an answer. A list with entries in it
/// has answered.
/// </remarks>
internal sealed partial class LookupListsRecipeStep : IRecipeStepHandler
{
    private const string StepName = "lookup-lists";

    /// <summary>
    /// Operator-facing, not user-facing, so not localised — the same judgement
    /// <c>SystemOperation</c>'s log lines take. Information rather than Debug: "what did this
    /// recipe change" is a review question, and an answer that is off by default is no answer.
    /// </summary>
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "The lookup-lists step added {Count} entry/entries to the existing list "
            + "'{Code}': {Entries}.")]
    private partial void LogEntriesFilled(int count, string code, string entries);

    private readonly ILookupListService _lookupLists;
    private readonly ISystemOperation _systemOperation;

    /// <summary>
    /// Where a fill is reported. Not through the localiser: a log line is read by an operator
    /// reviewing a run, in whatever language the host logs in, and the audit trail is where the
    /// user-facing record of the same change lives.
    /// </summary>
    private readonly ILogger _logger;

    private readonly IStringLocalizer S;

    public LookupListsRecipeStep(
        ILookupListService lookupLists,
        ISystemOperation systemOperation,
        ILogger<LookupListsRecipeStep> logger,
        IStringLocalizer<LookupListsRecipeStep> stringLocalizer)
    {
        _lookupLists = lookupLists;
        _systemOperation = systemOperation;
        _logger = logger;
        S = stringLocalizer;
    }

    public async Task ExecuteAsync(RecipeExecutionContext context)
    {
        if (!string.Equals(context.Name, StepName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var model = context.Step.ToObject<LookupListsStepModel>() ?? new LookupListsStepModel();

        using (_systemOperation.Begin("lookup-lists recipe step"))
        {
            var errors = new List<string>();
            var toCreate = new List<LookupListStepEntry>();

            // The gap-filling half of ADR-0008's lookup-list addendum: the list the tenant already
            // holds, and the entries this file names that it does not. Resolved in the validation
            // pass so that a file whose last list is malformed writes nothing at all, including no
            // fills.
            var toFill = new List<(LookupListDocument Existing, List<LookupListItemStepEntry> Missing)>();

            var seen = new HashSet<string>(LookupCodes.Comparer);

            foreach (var list in model.Lists)
            {
                var code = LookupCodes.Normalise(list.Code);

                if (!seen.Add(code))
                {
                    errors.Add(S["Lookup list '{0}' is listed more than once.", code].Value);
                    continue;
                }

                if (!LookupCodes.IsValidListCode(code))
                {
                    errors.Add(S["'{0}' is not a valid lookup list code.", code].Value);
                    continue;
                }

                var itemErrors = ItemErrorsIn(list, code);

                if (itemErrors.Count > 0)
                {
                    errors.AddRange(itemErrors);
                    continue;
                }

                var existing = await _lookupLists.GetByCodeAsync(code);

                if (existing is null)
                {
                    toCreate.Add(list);
                    continue;
                }

                var differences = DifferencesFrom(existing, list);

                if (differences.Count > 0)
                {
                    errors.Add(S["Lookup list '{0}' already exists with different content: {1}.",
                        code,
                        string.Join("; ", differences)].Value);

                    continue;
                }

                // Nothing the tenant holds disagrees with the file, so whatever the file names and
                // the tenant lacks is a gap rather than a divergence — there is no decision to
                // overwrite, because ADR-0014 guarantees an entry is never removed, only retired.
                var missing = MissingEntries(existing, list);

                if (missing.Count > 0)
                {
                    toFill.Add((existing, missing));
                }
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            foreach (var (existing, missing) in toFill)
            {
                // Through the service, so the fill is validated and audited exactly as an
                // administrator's own edit on the lookup list screen would be. Not a direct
                // document write — the same rule ADR-0008's employeeAttachableTypeCodes addendum
                // set for the only other step that can write to a row it did not create.
                var filled = await _lookupLists.UpdateAsync(
                    existing.LookupListId,
                    existing.Name,
                    [.. existing.Items, .. missing.Select((item, ordinal) => ItemOf(item, existing.Items.Count + ordinal))]);

                if (!filled.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        Failures(
                            filled,
                            S["Lookup list '{0}' could not be given the entries it was missing.", existing.Code].Value));

                    return;
                }

                // Said rather than done silently. A recipe that changes a tenant without saying so
                // is the thing an operator cannot review, and "where did this entry come from" has
                // to be answerable from the run as well as from the audit trail.
                // Joined into a local rather than inline, because CA1873 is right that work done
                // for a log line should not be done where it cannot be skipped — and a fill
                // happens only when a pack genuinely adds something, so doing it unconditionally
                // here costs nothing worth guarding.
                var added = string.Join(", ", missing.Select(item => LookupCodes.Normalise(item.Code)));

                LogEntriesFilled(missing.Count, existing.Code, added);
            }

            foreach (var list in toCreate)
            {
                var created = await _lookupLists.CreateAsync(
                    LookupCodes.Normalise(list.Code),
                    new BilingualText(list.NameEn, list.NameAr),
                    ItemsOf(list),
                    // A list that arrives in a recipe is one WorkMate or a pack ships, so the
                    // customer may extend it and may not delete it. A list they invent through the
                    // screen is theirs.
                    isSystemDefined: list.IsSystemDefined ?? true);

                if (!created.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        Failures(created, S["Lookup list '{0}' could not be created.", list.Code].Value));

                    return;
                }
            }
        }
    }

    private static IReadOnlyList<LookupListItem> ItemsOf(LookupListStepEntry list) =>
        [.. list.Items.Select(ItemOf)];

    /// <summary>One entry as the service takes it, with the defaults an absent field means.</summary>
    /// <param name="ordinal">
    /// Where the file lists it, used only when the file states no sort order of its own.
    /// </param>
    private static LookupListItem ItemOf(LookupListItemStepEntry item, int ordinal) =>
        new(
            LookupCodes.Normalise(item.Code),
            new BilingualText(item.NameEn, item.NameAr),
            // Absent means offered. A pack listing an entry it does not expect anybody to choose
            // would say so; the common case is a list of live choices.
            item.IsActive ?? true,
            // Absent means "the order the file lists them in is good enough", which is the same
            // ruling ADR-0008's sort-order addendum made for dimension records — a hand-written
            // file states nothing and gets its own listing order, and an export states every one.
            item.SortOrder ?? (ordinal + 1) * 10);

    /// <summary>
    /// The entries this row names that the tenant's existing list does not hold.
    /// </summary>
    /// <remarks>
    /// ADR-0008's lookup-list addendum. An absent entry is an absence rather than an answer: there
    /// is no administrator's decision to overwrite, because an entry cannot be removed from a list
    /// at all — only retired — so one that is not there has never been there.
    ///
    /// Only consulted once <see cref="DifferencesFrom"/> has found nothing, so a file that
    /// disagrees with the tenant about an entry that <em>does</em> exist fails rather than being
    /// partly applied.
    /// </remarks>
    private static List<LookupListItemStepEntry> MissingEntries(
        LookupListDocument existing,
        LookupListStepEntry entry) =>
        [
            .. entry.Items.Where(item =>
                !existing.Items.Any(stored => LookupCodes.Same(stored.Code, item.Code))),
        ];

    private List<string> ItemErrorsIn(LookupListStepEntry list, string listCode)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(LookupCodes.Comparer);

        foreach (var item in list.Items)
        {
            var code = LookupCodes.Normalise(item.Code);

            if (!LookupCodes.IsValidItemCode(code))
            {
                errors.Add(S["Lookup list '{0}' states the entry code '{1}', which is not valid.", listCode, code].Value);
                continue;
            }

            if (!seen.Add(code))
            {
                errors.Add(S["Lookup list '{0}' lists the entry '{1}' more than once.", listCode, code].Value);
            }
        }

        return errors;
    }

    /// <summary>
    /// How the tenant's list differs from the one this row states, for ADR-0008's skip rule.
    /// </summary>
    /// <remarks>
    /// Scoped to what the row states. A row carrying no <c>nameAr</c> is not claiming the list has
    /// none, and a row listing four entries is not claiming the tenant's fifth should not be there.
    /// What it does claim is that the four it names exist and say what it says they say.
    /// </remarks>
    private static List<string> DifferencesFrom(LookupListDocument existing, LookupListStepEntry entry)
    {
        var differences = new List<string>();

        if (!RecipeNameComparison.Same(existing.Name.En, entry.NameEn))
        {
            differences.Add(RecipeNameComparison.Describe("English name", existing.Name.En, entry.NameEn));
        }

        if (!RecipeNameComparison.Same(existing.Name.Ar, entry.NameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic name", existing.Name.Ar, entry.NameAr));
        }

        foreach (var item in entry.Items)
        {
            var code = LookupCodes.Normalise(item.Code);

            var stored = existing.Items.FirstOrDefault(
                candidate => LookupCodes.Same(candidate.Code, code));

            if (stored is null)
            {
                // Not a difference. ADR-0008's lookup-list addendum: an entry the tenant does not
                // hold has never been in the list, so there is nothing to overwrite and nothing to
                // refuse. MissingEntries collects it and the step adds it.
                continue;
            }

            if (!RecipeNameComparison.Same(stored.Name.En, item.NameEn))
            {
                differences.Add(RecipeNameComparison.Describe($"entry '{code}' English name", stored.Name.En, item.NameEn));
            }

            if (!RecipeNameComparison.Same(stored.Name.Ar, item.NameAr))
            {
                differences.Add(RecipeNameComparison.Describe($"entry '{code}' Arabic name", stored.Name.Ar, item.NameAr));
            }

            if (item.IsActive is { } active && stored.IsActive != active)
            {
                differences.Add(
                    $"the entry '{code}' is {(stored.IsActive ? "active" : "retired")} in the tenant "
                    + $"but {(active ? "active" : "retired")} in the recipe");
            }

            if (item.SortOrder is { } order && stored.SortOrder != order)
            {
                differences.Add($"the entry '{code}' is at order {stored.SortOrder} in the tenant but {order} in the recipe");
            }
        }

        return differences;
    }

    private static IReadOnlyList<string> Failures<T>(RecordResult<T> result, string fallback) =>
        result.Succeeded
            ? []
            : result.Errors.Count > 0
                ? [.. result.Errors.Select(error => error.Message.Value)]
                : [fallback];
}

internal sealed class LookupListsStepModel
{
    public List<LookupListStepEntry> Lists { get; set; } = [];
}

/// <summary>One lookup list, as a recipe states it.</summary>
internal sealed class LookupListStepEntry
{
    /// <summary>The natural key, and the only way anything else refers to this list.</summary>
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Whether the list is WorkMate's rather than the tenant's. Absent means it is, because
    /// arriving in a recipe is what makes a list shipped rather than invented.
    /// </summary>
    public bool? IsSystemDefined { get; set; }

    public List<LookupListItemStepEntry> Items { get; set; } = [];
}

/// <summary>One entry in a list, as a recipe states it.</summary>
/// <remarks>
/// <see cref="IsActive"/> and <see cref="SortOrder"/> are nullable so that "absent" and "stated"
/// are different things. ADR-0008's comparison is scoped to what a row states, and a
/// non-nullable <c>bool</c> defaulting to <c>false</c> would make every file that says nothing
/// about activity claim every entry is retired.
/// </remarks>
internal sealed class LookupListItemStepEntry
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool? IsActive { get; set; }

    public int? SortOrder { get; set; }
}
