using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;

namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// The <c>structures</c> recipe step: defines axes, with levels named by dimension type code
/// rather than id, resolved against the types the <c>dimension-types</c> step already created —
/// the same reason <c>IDimensionService.GetByCodeAsync</c>'s doc comment gives for records:
/// "this is how a recipe or an import resolves a parent reference". A recipe is portable precisely
/// because it never carries a generated id.
/// </summary>
/// <remarks>
/// Same two-phase, re-run-safe shape as <see cref="DimensionTypesRecipeStep"/>: every row is
/// validated against one batch, including the level codes resolving at all, before anything is
/// created, and a row naming an already-existing code is skipped if its content matches exactly —
/// name, levels in order, and the three flags — or fails the step, naming the code and what
/// differs, if it does not.
/// </remarks>
internal sealed class StructuresRecipeStep : IRecipeStepHandler
{
    private const string StepName = "structures";

    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IStructureService _structureService;
    private readonly IDimensionValidator _validator;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public StructuresRecipeStep(
        IDimensionTypeService dimensionTypeService,
        IStructureService structureService,
        IDimensionValidator validator,
        ISystemOperation systemOperation,
        IStringLocalizer<StructuresRecipeStep> stringLocalizer)
    {
        _dimensionTypeService = dimensionTypeService;
        _structureService = structureService;
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

        var model = context.Step.ToObject<StructuresStepModel>() ?? new StructuresStepModel();

        using (_systemOperation.Begin("structures recipe step"))
        {
            var batch = _validator.BeginBatch();
            var errors = new List<string>();
            var levelIdsByStructure = new Dictionary<StructureStepEntry, List<string>>();
            var toCreate = new List<StructureStepEntry>();

            foreach (var structure in model.Structures)
            {
                var levelIds = new List<string>();
                var unresolved = false;

                foreach (var typeCode in structure.LevelTypeCodes)
                {
                    var type = await _dimensionTypeService.GetByCodeAsync(typeCode);

                    if (type is null)
                    {
                        errors.Add(S["Structure '{0}' names level type '{1}', which does not exist. Levels must be listed after the types that back them.", structure.Code, typeCode].Value);
                        unresolved = true;
                        continue;
                    }

                    levelIds.Add(type.DimensionTypeId);
                }

                if (unresolved)
                {
                    continue;
                }

                levelIdsByStructure[structure] = levelIds;

                var existing = await _structureService.GetByCodeAsync(structure.Code);

                if (existing is not null)
                {
                    var differences = DifferencesFrom(existing, structure, levelIds);

                    if (differences.Count > 0)
                    {
                        errors.Add(S["Structure '{0}' already exists with different content: {1}.", structure.Code, string.Join("; ", differences)].Value);
                    }

                    // Matches exactly: nothing to validate or create for this row.
                    continue;
                }

                var violations = await _validator.ValidateStructureAsync(
                    null,
                    structure.Code,
                    new BilingualText(structure.NameEn, structure.NameAr),
                    levelIds,
                    structure.IsPrimaryOrganisation,
                    batch);

                errors.AddRange(violations.Select(error => error.Message.Value));
                toCreate.Add(structure);
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            foreach (var structure in toCreate)
            {
                var result = await _structureService.CreateAsync(
                    structure.Code,
                    new BilingualText(structure.NameEn, structure.NameAr),
                    levelIdsByStructure[structure],
                    structure.AllowSkipLevel,
                    structure.IsStrict,
                    structure.IsPrimaryOrganisation);

                if (!result.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        result.Errors.Count > 0
                            ? result.Errors.Select(error => error.Message.Value)
                            : [S["'{0}' could not be created.", structure.Code].Value]);

                    return;
                }
            }
        }
    }

    private static List<string> DifferencesFrom(StructureDocument existing, StructureStepEntry structure, List<string> levelIds)
    {
        var differences = new List<string>();

        if (!string.Equals(existing.Name.En, structure.NameEn, StringComparison.Ordinal))
        {
            differences.Add($"the English name is '{existing.Name.En}' in the tenant but '{structure.NameEn}' in the recipe");
        }

        if (!string.Equals(existing.Name.Ar, structure.NameAr, StringComparison.Ordinal))
        {
            differences.Add($"the Arabic name is '{existing.Name.Ar}' in the tenant but '{structure.NameAr}' in the recipe");
        }

        if (existing.AllowSkipLevel != structure.AllowSkipLevel)
        {
            differences.Add($"allowSkipLevel is {existing.AllowSkipLevel} in the tenant but {structure.AllowSkipLevel} in the recipe");
        }

        if (existing.IsStrict != structure.IsStrict)
        {
            differences.Add($"isStrict is {existing.IsStrict} in the tenant but {structure.IsStrict} in the recipe");
        }

        if (existing.IsPrimaryOrganisation != structure.IsPrimaryOrganisation)
        {
            differences.Add($"isPrimaryOrganisation is {existing.IsPrimaryOrganisation} in the tenant but {structure.IsPrimaryOrganisation} in the recipe");
        }

        var existingLevelIds = existing.Levels.OrderBy(level => level.Ordinal).Select(level => level.DimensionTypeId).ToList();

        if (!existingLevelIds.SequenceEqual(levelIds, StringComparer.Ordinal))
        {
            differences.Add("the levels, or their order, differ from the tenant's existing structure");
        }

        return differences;
    }
}

internal sealed class StructuresStepModel
{
    public List<StructureStepEntry> Structures { get; set; } = [];
}

internal sealed class StructureStepEntry
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>Root first. Resolved against each type's code, not its generated id.</summary>
    public List<string> LevelTypeCodes { get; set; } = [];

    public bool AllowSkipLevel { get; set; }

    public bool IsStrict { get; set; } = true;

    public bool IsPrimaryOrganisation { get; set; }
}
