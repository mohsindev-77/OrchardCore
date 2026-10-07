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
            var shapesByStructure = new Dictionary<StructureStepEntry, StructureShape>();
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

                // Two descriptions of one thing. Merging them would mean the recipe said something
                // nobody wrote, so the row is refused and told which two keys to choose between.
                if (structure.StatesContainment && structure.AllowSkipLevel is not null)
                {
                    errors.Add(S["Structure '{0}' states both 'allowSkipLevel' and an explicit 'containment' or 'rootTypeCodes'. They are two ways of saying the same thing; use one.", structure.Code].Value);
                    continue;
                }

                var shape = await ResolveShapeAsync(structure, levelIds, errors);

                if (shape is null)
                {
                    continue;
                }

                shapesByStructure[structure] = shape;

                var existing = await _structureService.GetByCodeAsync(structure.Code);

                if (existing is not null)
                {
                    var differences = DifferencesFrom(existing, structure, levelIds, shape);

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
                    shape,
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
                    structure.AllowSkipLevel == true,
                    structure.IsStrict,
                    structure.IsPrimaryOrganisation,
                    shapesByStructure[structure]);

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

    /// <summary>
    /// The row's rules, however it chose to state them: the explicit map, or the one its chain and
    /// skip-level flag describe.
    /// </summary>
    /// <remarks>
    /// Every type is named by code here, as everything in a recipe is — a recipe is portable
    /// precisely because it never carries a generated id — so a code naming no type is a failure
    /// of this row rather than something to resolve to nothing and pass on.
    /// </remarks>
    private async Task<StructureShape?> ResolveShapeAsync(
        StructureStepEntry structure,
        List<string> levelIds,
        List<string> errors)
    {
        if (!structure.StatesContainment)
        {
            // The one place a dimension type's AllowsSelfNesting is still read. It is not a veto
            // any more — ADR-0010's addendum made the map below the only authority — it is part of
            // translating a chain description into that map, exactly as allowSkipLevel is. A row
            // written before ADR-0010 therefore still produces the map it has always meant.
            var selfNesting = new HashSet<string>(StringComparer.Ordinal);

            foreach (var typeCode in structure.LevelTypeCodes)
            {
                if (await _dimensionTypeService.GetByCodeAsync(typeCode) is { AllowsSelfNesting: true } type)
                {
                    selfNesting.Add(type.DimensionTypeId);
                }
            }

            return StructureShape.FromChain(levelIds, structure.AllowSkipLevel == true, selfNesting);
        }

        var before = errors.Count;

        async Task<string?> IdOfAsync(string typeCode)
        {
            var type = await _dimensionTypeService.GetByCodeAsync(typeCode);

            if (type is null)
            {
                errors.Add(S["Structure '{0}' names type '{1}' in its rules, which does not exist.", structure.Code, typeCode].Value);
            }

            return type?.DimensionTypeId;
        }

        var roots = new List<string>();

        // No roots stated alongside an explicit map means the chain's own answer: the first level.
        foreach (var typeCode in structure.RootTypeCodes.Count > 0
            ? structure.RootTypeCodes
            : structure.LevelTypeCodes.Take(1).ToList())
        {
            if (await IdOfAsync(typeCode) is { } id)
            {
                roots.Add(id);
            }
        }

        var containment = new List<StructureContainment>();

        foreach (var (parentCode, childCodes) in structure.Containment)
        {
            var parentId = await IdOfAsync(parentCode);

            foreach (var childCode in childCodes)
            {
                var childId = await IdOfAsync(childCode);

                if (parentId is not null && childId is not null)
                {
                    containment.Add(new StructureContainment(parentId, childId));
                }
            }
        }

        return errors.Count > before ? null : new StructureShape(roots, containment);
    }

    private static List<string> DifferencesFrom(
        StructureDocument existing,
        StructureStepEntry structure,
        List<string> levelIds,
        StructureShape shape)
    {
        var differences = new List<string>();

        if (!RecipeNameComparison.Same(existing.Name.En, structure.NameEn))
        {
            differences.Add(RecipeNameComparison.Describe("English name", existing.Name.En, structure.NameEn));
        }

        if (!RecipeNameComparison.Same(existing.Name.Ar, structure.NameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic name", existing.Name.Ar, structure.NameAr));
        }

        // Compared only when the row states it. A row written in the explicit form says nothing
        // about skipping, and ADR-0008 scopes the comparison to the fields a row actually states.
        if (structure.AllowSkipLevel is { } allowSkipLevel && existing.AllowSkipLevel != allowSkipLevel)
        {
            differences.Add($"allowSkipLevel is {existing.AllowSkipLevel} in the tenant but {allowSkipLevel} in the recipe");
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

        // Both sides are deduplicated and ordered by the derivation before they are compared, so
        // two spellings of one map compare equal and only a genuine difference is reported.
        var wanted = Internal.StructureContainmentDerivation.Deduplicate(shape.Containment);
        var wantedRoots = Internal.StructureContainmentDerivation.DeduplicateRoots(shape.RootDimensionTypeIds);

        if (!existing.Containment.SequenceEqual(wanted))
        {
            differences.Add("the rules about what may sit under what differ from the tenant's existing structure");
        }

        if (!existing.RootDimensionTypeIds.SequenceEqual(wantedRoots, StringComparer.Ordinal))
        {
            differences.Add("the types allowed at the top of the structure differ from the tenant's existing structure");
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

    /// <summary>
    /// Nullable so that "absent" and "deliberately false" are different answers, which is what
    /// lets the step refuse a row that states both this and <see cref="Containment"/>.
    /// </summary>
    public bool? AllowSkipLevel { get; set; }

    /// <summary>
    /// The types that may sit at the top of the axis, by code. Optional: a row that omits it and
    /// states <see cref="LevelTypeCodes"/> gets the first level, which is what the chain meant.
    /// </summary>
    public List<string> RootTypeCodes { get; set; } = [];

    /// <summary>
    /// The containment map: parent type code to the child type codes it may contain, by code.
    /// </summary>
    /// <remarks>
    /// The explicit form ADR-0010 introduced. A row that states it is describing the rules
    /// directly; a row that omits it is describing them as a chain and has them derived through
    /// the same function the migration uses. Stating it alongside <c>allowSkipLevel</c> is refused
    /// rather than merged — the two are different descriptions of the same thing, and silently
    /// picking one would make the recipe mean something nobody wrote.
    /// </remarks>
    public Dictionary<string, List<string>> Containment { get; set; } = [];

    public bool IsStrict { get; set; } = true;

    public bool IsPrimaryOrganisation { get; set; }

    /// <summary>Whether this row describes its rules explicitly rather than as a chain.</summary>
    public bool StatesContainment => Containment.Count > 0 || RootTypeCodes.Count > 0;
}
