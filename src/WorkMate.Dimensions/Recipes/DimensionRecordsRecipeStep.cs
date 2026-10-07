using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;

namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// The <c>dimension-records</c> recipe step: creates records and places them, with the dimension
/// type, the structure and the parent all referenced by code and resolved at import — a generated
/// id is never portable, and <c>IDimensionService.GetByCodeAsync</c> exists specifically so an
/// import can resolve a parent reference this way.
/// </summary>
/// <remarks>
/// Re-running a recipe must be safe. A row naming a code that already exists is compared against
/// the tenant's record — type, name, effective range, and where every placement the row names
/// currently has the record — and skipped, with nothing written, if everything matches; the step
/// fails, naming the code and what differs, if anything does not. See the README's note on recipe
/// re-run behaviour.
///
/// Every row's own data — code, name, effective range, and that its type and every placement's
/// structure already exist — is validated against one <see cref="DimensionValidationBatch"/> before
/// any new record is created, the same all-or-nothing shape <see cref="DimensionTypesRecipeStep"/>
/// and <see cref="StructuresRecipeStep"/> use.
///
/// A placement's parent is the one thing that batch cannot cover for a genuinely new record: a
/// parent reference naming another record in this same step does not exist yet at validation time,
/// only once that earlier row has actually been created. New records are therefore placed after
/// every new record in the step has been created — in list order, so a parent must be listed
/// before the children it carries, exactly as architecture section 6's record layer already
/// requires between types, structures and records themselves — through
/// <see cref="IDimensionService.MoveAsync"/>, which revalidates for real. A placement rule
/// genuinely broken by the recipe's own data (not merely a forward reference) is therefore caught
/// at that point rather than in the earlier batch, and stops the run there; records already created
/// earlier in the same step are not rolled back. A recipe that lists parents before children and
/// places records that do not break a rule — which is what every recipe this module ships does —
/// never reaches that case.
/// </remarks>
internal sealed class DimensionRecordsRecipeStep : IRecipeStepHandler
{
    private const string StepName = "dimension-records";

    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IStructureService _structureService;
    private readonly IDimensionService _dimensionService;
    private readonly IDimensionGraphService _graph;
    private readonly IDimensionValidator _validator;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public DimensionRecordsRecipeStep(
        IDimensionTypeService dimensionTypeService,
        IStructureService structureService,
        IDimensionService dimensionService,
        IDimensionGraphService graph,
        IDimensionValidator validator,
        ISystemOperation systemOperation,
        IStringLocalizer<DimensionRecordsRecipeStep> stringLocalizer)
    {
        _dimensionTypeService = dimensionTypeService;
        _structureService = structureService;
        _dimensionService = dimensionService;
        _graph = graph;
        _validator = validator;
        _systemOperation = systemOperation;
        S = stringLocalizer;
    }

    public async Task ExecuteAsync(RecipeExecutionContext context)
    {
        if (!string.Equals(context.Name, StepName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var model = context.Step.ToObject<DimensionRecordsStepModel>() ?? new DimensionRecordsStepModel();

        using (_systemOperation.Begin("dimension-records recipe step"))
        {
            var batch = _validator.BeginBatch();
            var errors = new List<string>();
            var typeIdByRecord = new Dictionary<DimensionRecordStepEntry, string>();
            var structureIdByCode = new Dictionary<string, string>(StringComparer.Ordinal);
            var toCreate = new List<DimensionRecordStepEntry>();
            var alreadyExists = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var record in model.Records)
            {
                var type = await _dimensionTypeService.GetByCodeAsync(record.TypeCode);

                if (type is null)
                {
                    errors.Add(S["Record '{0}' names dimension type '{1}', which does not exist.", record.Code, record.TypeCode].Value);
                    continue;
                }

                foreach (var placement in record.Placements)
                {
                    if (structureIdByCode.ContainsKey(placement.StructureCode))
                    {
                        continue;
                    }

                    var structure = await _structureService.GetByCodeAsync(placement.StructureCode);

                    if (structure is null)
                    {
                        errors.Add(S["Record '{0}' is placed on structure '{1}', which does not exist.", record.Code, placement.StructureCode].Value);
                        continue;
                    }

                    structureIdByCode[placement.StructureCode] = structure.StructureId;
                }

                var range = new EffectiveRange(record.EffectiveFrom, record.EffectiveTo);
                var existing = await _dimensionService.GetByCodeAsync(record.Code);

                if (existing is not null)
                {
                    var differences = DifferencesFrom(existing, record, type.DimensionTypeId, range);
                    differences.AddRange(await PlacementDifferencesAsync(existing, record, structureIdByCode));

                    if (differences.Count > 0)
                    {
                        errors.Add(S["Record '{0}' already exists with different content: {1}.", record.Code, string.Join("; ", differences)].Value);
                        continue;
                    }

                    // Matches exactly, placements included: nothing to validate, create or move.
                    alreadyExists[record.Code] = existing.RecordId;
                    continue;
                }

                typeIdByRecord[record] = type.DimensionTypeId;

                var name = new BilingualText(record.NameEn, record.NameAr);
                var violations = await _validator.ValidateRecordAsync(null, type.DimensionTypeId, record.Code, name, range, batch);

                errors.AddRange(violations.Select(error => error.Message.Value));
                toCreate.Add(record);
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            var recordIdByCode = new Dictionary<string, string>(alreadyExists, StringComparer.Ordinal);

            foreach (var record in toCreate)
            {
                var created = await _dimensionService.CreateAsync(
                    typeIdByRecord[record],
                    record.Code,
                    new BilingualText(record.NameEn, record.NameAr),
                    new EffectiveRange(record.EffectiveFrom, record.EffectiveTo));

                if (!created.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        created.Errors.Count > 0
                            ? created.Errors.Select(error => error.Message.Value)
                            : [S["'{0}' could not be created.", record.Code].Value]);

                    return;
                }

                recordIdByCode[record.Code] = created.Value!.RecordId;

                foreach (var placement in record.Placements)
                {
                    string? parentId = null;

                    if (placement.ParentCode is not null)
                    {
                        if (!recordIdByCode.TryGetValue(placement.ParentCode, out parentId))
                        {
                            RecipeStepFailures.Throw(context, [
                                S["Record '{0}' could not be placed: its parent '{1}' was not found. Parents must be listed before their children.", record.Code, placement.ParentCode].Value,
                            ]);

                            return;
                        }
                    }

                    var moved = await _dimensionService.MoveAsync(
                        structureIdByCode[placement.StructureCode], recordIdByCode[record.Code], parentId, placement.EffectiveFrom);

                    if (!moved.Succeeded)
                    {
                        RecipeStepFailures.Throw(
                            context,
                            moved.Errors.Count > 0
                                ? moved.Errors.Select(error => error.Message.Value)
                                : [S["'{0}' could not be placed.", record.Code].Value]);

                        return;
                    }
                }
            }
        }
    }

    private static List<string> DifferencesFrom(
        DimensionNodeRef existing, DimensionRecordStepEntry record, string typeId, EffectiveRange range)
    {
        var differences = new List<string>();

        if (!string.Equals(existing.DimensionTypeId, typeId, StringComparison.Ordinal))
        {
            differences.Add("its dimension type in the tenant does not match the recipe's typeCode");
        }

        if (!RecipeNameComparison.Same(existing.NameEn, record.NameEn))
        {
            differences.Add(RecipeNameComparison.Describe("English", existing.NameEn, record.NameEn));
        }

        if (!RecipeNameComparison.Same(existing.NameAr, record.NameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic", existing.NameAr, record.NameAr));
        }

        if (existing.EffectiveRange != range)
        {
            differences.Add($"the effective range is {existing.EffectiveRange} in the tenant but {range} in the recipe");
        }

        return differences;
    }

    /// <summary>
    /// Where <paramref name="existing"/> is placed today against where the recipe says it should
    /// be, for every structure the row names. A parent the comparison itself cannot find is
    /// reported rather than assumed to match — an already-existing record whose recipe-stated
    /// parent is missing is not safely re-applicable without a human looking at it.
    /// </summary>
    private async Task<List<string>> PlacementDifferencesAsync(
        DimensionNodeRef existing, DimensionRecordStepEntry record, Dictionary<string, string> structureIdByCode)
    {
        var differences = new List<string>();

        foreach (var placement in record.Placements)
        {
            if (!structureIdByCode.TryGetValue(placement.StructureCode, out var structureId))
            {
                // Already reported: the structure itself does not exist.
                continue;
            }

            string? expectedParentId = null;

            if (placement.ParentCode is not null)
            {
                var parent = await _dimensionService.GetByCodeAsync(placement.ParentCode);

                if (parent is null)
                {
                    differences.Add($"its parent '{placement.ParentCode}' on structure '{placement.StructureCode}' could not be found to compare against");
                    continue;
                }

                expectedParentId = parent.RecordId;
            }

            var ancestors = await _graph.GetAncestorsAsync(structureId, existing.RecordId, placement.EffectiveFrom);
            var actualParentId = ancestors.Count > 0 ? ancestors[0].RecordId : null;

            if (!string.Equals(actualParentId, expectedParentId, StringComparison.Ordinal))
            {
                differences.Add(
                    $"on structure '{placement.StructureCode}' it is currently placed differently from what the recipe specifies");
            }
        }

        return differences;
    }
}

internal sealed class DimensionRecordsStepModel
{
    public List<DimensionRecordStepEntry> Records { get; set; } = [];
}

internal sealed class DimensionRecordStepEntry
{
    public string Code { get; set; } = string.Empty;

    public string TypeCode { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Where this record sits, if anywhere. Empty for a deliberately unplaced record.</summary>
    public List<DimensionRecordPlacementStepEntry> Placements { get; set; } = [];
}

internal sealed class DimensionRecordPlacementStepEntry
{
    public string StructureCode { get; set; } = string.Empty;

    /// <summary>The parent's code, resolved at import, or null to place this record as a root.</summary>
    public string? ParentCode { get; set; }

    public DateOnly EffectiveFrom { get; set; }
}
