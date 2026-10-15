using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Recipes;
using WorkMate.Platform.Services;

namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// The <c>dimension-references</c> recipe step: which records point at which others, of a named
/// kind. Today there is one kind — a Position's allowed grades (D26 C).
/// </summary>
/// <remarks>
/// <b>Its own step, after <c>dimension-records</c>, for two reasons.</b> A reference needs both ends
/// to exist and the records step creates them in one pass, so a reference stated on a record row
/// would be a forward reference half the time. And a reference is <em>not a placement</em>: it never
/// enters the closure index and appears on no tree (D21, as amended by D26 C), so putting it beside
/// <c>placements</c> on a record would file it as the thing it most needs not to be mistaken for.
///
/// <b>Re-running is safe, the same way every other step here is (ADR-0008).</b> A record whose
/// stored set already matches the row is skipped with nothing written; one that differs fails naming
/// the record and both sets, rather than silently overwriting a set a customer has since changed.
/// Order within a row is part of what is compared, because order is stored and is what the screens
/// list the grades in.
///
/// <b>Every reference is validated before any is written</b>, through the same
/// <c>IDimensionValidator.ValidateReferencesAsync</c> the Add form and slice F6's employee screen
/// use — so a recipe that points a department at a grade is refused in the same words a person
/// would see, which is specification rule 5 and ADR-0010's lesson.
/// </remarks>
internal sealed class DimensionReferencesRecipeStep : IRecipeStepHandler
{
    private const string StepName = "dimension-references";

    private readonly IDimensionService _dimensionService;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public DimensionReferencesRecipeStep(
        IDimensionService dimensionService,
        ISystemOperation systemOperation,
        IStringLocalizer<DimensionReferencesRecipeStep> stringLocalizer)
    {
        _dimensionService = dimensionService;
        _systemOperation = systemOperation;
        S = stringLocalizer;
    }

    public async Task ExecuteAsync(RecipeExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(context.Name, StepName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var model = context.Step.ToObject<DimensionReferencesStepModel>() ?? new DimensionReferencesStepModel();

        using (_systemOperation.Begin("dimension-references recipe step"))
        {
            var errors = new List<string>();
            var toWrite = new List<(string RecordId, string Kind, List<string> TargetIds)>();

            foreach (var row in model.References)
            {
                var record = await _dimensionService.GetByCodeAsync(row.RecordCode);

                if (record is null)
                {
                    errors.Add(S["There is no dimension record with the code '{0}'.", row.RecordCode].Value);

                    continue;
                }

                var targetIds = new List<string>();
                var missing = new List<string>();

                foreach (var targetCode in row.TargetCodes)
                {
                    var target = await _dimensionService.GetByCodeAsync(targetCode);

                    if (target is null)
                    {
                        missing.Add(targetCode);
                    }
                    else
                    {
                        targetIds.Add(target.RecordId);
                    }
                }

                if (missing.Count > 0)
                {
                    errors.Add(S["'{0}' points at {1}, which {2} does not have.",
                        row.RecordCode, string.Join(", ", missing), "this tenant"].Value);

                    continue;
                }

                // The RAW stored set, undated. The dated read would filter out a target that is not
                // effective today, so a row stating three grades — one of them retired last year —
                // would read the tenant's set as two and report a difference nobody made. ADR-0008
                // compares what a row states against what the tenant holds, and "holds" is the
                // stored list.
                var heldIds = await _dimensionService.GetReferenceTargetIdsAsync(record.RecordId, row.Kind);

                if (heldIds.SequenceEqual(targetIds, StringComparer.Ordinal))
                {
                    // Matches exactly, in the same order: nothing to write for this row. Order is
                    // part of the comparison because it is stored and it is what the screens list
                    // the grades in.
                    continue;
                }

                if (heldIds.Count > 0)
                {
                    // Resolved for the message only, and falling back to the id for a target that
                    // no longer resolves today — a retired grade still in a position's set. An
                    // error naming an id is poorer than one naming a code and better than one that
                    // leaves the target out of the sentence entirely.
                    var heldCodes = new List<string>();

                    foreach (var heldId in heldIds)
                    {
                        heldCodes.Add((await _dimensionService.GetAsync(heldId))?.Code ?? heldId);
                    }

                    errors.Add(S["'{0}' already has {1} set to {2} in this tenant, but the recipe says {3}.",
                        row.RecordCode,
                        row.Kind,
                        string.Join(", ", heldCodes),
                        string.Join(", ", row.TargetCodes)].Value);

                    continue;
                }

                toWrite.Add((record.RecordId, row.Kind, targetIds));
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);

                return;
            }

            foreach (var (recordId, kind, targetIds) in toWrite)
            {
                var result = await _dimensionService.SetReferencesAsync(recordId, kind, targetIds);

                if (!result.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        result.Errors.Count > 0
                            ? result.Errors.Select(error => error.Message.Value)
                            : [S["References for '{0}' could not be set.", recordId].Value]);

                    return;
                }
            }
        }
    }
}

internal sealed class DimensionReferencesStepModel
{
    public List<DimensionReferenceStepEntry> References { get; set; } = [];
}

/// <summary>One record's references of one kind, as a recipe states them.</summary>
internal sealed class DimensionReferenceStepEntry
{
    /// <summary>The record doing the referring, by code — a position, for the only kind that ships.</summary>
    public string RecordCode { get; set; } = string.Empty;

    /// <summary>Which kind. One of <c>DimensionReferenceKinds</c>; today only <c>allowed-grade</c>.</summary>
    public string Kind { get; set; } = DimensionReferenceKinds.AllowedGrade;

    /// <summary>
    /// What it points at, by code, in the order the screens should list them.
    /// </summary>
    /// <remarks>
    /// By code rather than by id, like every other cross-reference in these steps: a generated id is
    /// never portable, and <c>IDimensionService.GetByCodeAsync</c> exists so an import can resolve
    /// one this way. Order is part of the statement, because it is stored and it is what a reader
    /// sees — SE-1, SE-2, SE-3 rather than whatever order a set happened to come back in.
    /// </remarks>
    public List<string> TargetCodes { get; set; } = [];
}
