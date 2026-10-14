using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;

using WorkMate.Platform.Recipes;

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
            var toFill = new List<StructureStepEntry>();

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
                    else if (FillsTheEmployeeAttachmentGap(existing, shape))
                    {
                        // The one case where a row changes a structure it did not create. See
                        // ADR-0008's 11 October addendum: for this field alone, empty in the tenant
                        // is the absence of an answer rather than a different one, so a row that
                        // states a set is filling a gap rather than overwriting a decision.
                        toFill.Add(structure);
                    }

                    // Matches, or matches but for a gap to fill: nothing to validate or create.
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

            // The gaps first. Each goes through IStructureService.UpdateAsync rather than a direct
            // document write, so it is validated and audited exactly as an administrator's own edit
            // on the structures screen would be, and it changes nothing except the one field: every
            // other argument is read back from the structure as it stands.
            foreach (var structure in toFill)
            {
                var existing = await _structureService.GetByCodeAsync(structure.Code);

                if (existing is null)
                {
                    continue;
                }

                var filled = await _structureService.UpdateAsync(
                    existing.StructureId,
                    existing.Name,
                    existing.DimensionTypeIds,
                    existing.AllowSkipLevel,
                    existing.IsStrict,
                    existing.IsPrimaryOrganisation,
                    new StructureShape(
                        existing.RootDimensionTypeIds,
                        existing.Containment,
                        shapesByStructure[structure].EmployeeAttachableDimensionTypeIds),
                    structure.ToShortName());

                if (!filled.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        filled.Errors.Count > 0
                            ? filled.Errors.Select(error => error.Message.Value)
                            : [S["'{0}' could not be updated.", structure.Code].Value]);

                    return;
                }
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
                    shapesByStructure[structure],
                    batch: null,
                    structure.ToShortName());

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
        var attachable = await ResolveAttachableAsync(structure, errors);

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

            return StructureShape.FromChain(levelIds, structure.AllowSkipLevel == true, selfNesting)
                with { EmployeeAttachableDimensionTypeIds = attachable };
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

        return errors.Count > before ? null : new StructureShape(roots, containment, attachable);
    }

    /// <summary>
    /// The types this row says may hold employees, by id, or null when it says nothing.
    /// </summary>
    /// <remarks>
    /// Null and empty are different answers all the way down: null means the row is not describing
    /// employee attachment and the structure's own setting stands; an empty array means the row is
    /// saying the axis constrains nothing, which is a statement a recipe is entitled to make.
    /// </remarks>
    private async Task<IReadOnlyList<string>?> ResolveAttachableAsync(
        StructureStepEntry structure,
        List<string> errors)
    {
        if (structure.EmployeeAttachableTypeCodes is null)
        {
            return null;
        }

        var ids = new List<string>();

        foreach (var typeCode in structure.EmployeeAttachableTypeCodes)
        {
            var type = await _dimensionTypeService.GetByCodeAsync(typeCode);

            if (type is null)
            {
                errors.Add(S["Structure '{0}' names type '{1}' as holding employees, which does not exist.", structure.Code, typeCode].Value);

                continue;
            }

            ids.Add(type.DimensionTypeId);
        }

        return ids;
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

        // The short name, each half only when the row states it — the same rule, for the same
        // reason. A recipe written before decision D24 states neither and must not refuse a re-run
        // against a tenant whose structures have since been given one.
        if (structure.ShortNameEn is { } shortNameEn &&
            !RecipeNameComparison.Same(existing.ShortName.En, shortNameEn))
        {
            differences.Add(RecipeNameComparison.Describe("short name", existing.ShortName.En, shortNameEn));
        }

        if (structure.ShortNameAr is { } shortNameAr &&
            !RecipeNameComparison.Same(existing.ShortName.Ar, shortNameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic short name", existing.ShortName.Ar, shortNameAr));
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

        // Employee attachment, and the one place this step departs from "different means fail".
        //
        // ADR-0008's 11 October addendum: for this field, empty in the tenant is the absence of an
        // answer rather than a different one — the model documents it as "this axis is not
        // expressing the constraint", and it is the state every structure written before the field
        // existed deserialises to. So a gap is filled (see FillsTheEmployeeAttachmentGap) and only
        // a tenant that holds a real, different set is refused.
        if (shape.EmployeeAttachableDimensionTypeIds is { } wantedAttachable &&
            existing.EmployeeAttachableDimensionTypeIds.Count > 0 &&
            !existing.EmployeeAttachableDimensionTypeIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(wantedAttachable.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            differences.Add("the types that may hold employees differ from the tenant's existing structure");
        }

        return differences;
    }

    /// <summary>
    /// Whether this row states employee attachment for a structure that declares none — the gap the
    /// addendum permits filling.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose, and the narrowness is the argument. A structure holding a non-empty set
    /// has an answer, and a recipe does not get to replace it; a structure holding none has not been
    /// asked, because the model says empty and unset are the same state. A row stating an empty
    /// array against an empty structure is also not a gap — it agrees with what is there, and
    /// writing would be a change that changes nothing.
    /// </remarks>
    private static bool FillsTheEmployeeAttachmentGap(StructureDocument existing, StructureShape shape) =>
        shape.EmployeeAttachableDimensionTypeIds is { Count: > 0 } &&
        existing.EmployeeAttachableDimensionTypeIds.Count == 0;
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

    /// <summary>
    /// A short form of the name for places with no room for the whole one — the Organisation menu's
    /// brackets (decision D24). Optional in both halves.
    /// </summary>
    /// <remarks>
    /// Nullable, so that "absent" and "stated as empty" are different things — the same reason
    /// <c>pluralEn</c> is nullable on the <c>dimension-types</c> step. A row written before this
    /// existed says nothing about it, and ADR-0008's comparison is scoped to what a row states, so
    /// re-running it against a tenant whose structures have since been given short names must skip
    /// rather than refuse.
    /// </remarks>
    public string? ShortNameEn { get; set; }

    /// <inheritdoc cref="ShortNameEn"/>
    public string? ShortNameAr { get; set; }

    /// <summary>
    /// The short name as the service's value, or null when the row states neither half.
    /// </summary>
    /// <remarks>
    /// Null is what makes a row that says nothing leave a tenant's authored short name alone, which
    /// is <c>IStructureService.UpdateAsync</c>'s documented rule for it.
    /// </remarks>
    public BilingualText? ToShortName() =>
        ShortNameEn is null && ShortNameAr is null
            ? null
            : new BilingualText(ShortNameEn ?? string.Empty, ShortNameAr ?? string.Empty);

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

    /// <summary>
    /// The types that may hold employees on this axis, by code.
    /// </summary>
    /// <remarks>
    /// Nullable so that "absent" and "stated as empty" are different answers, the same reason
    /// <see cref="AllowSkipLevel"/> is: a row with no key says nothing about employee attachment
    /// and leaves whatever the structure declares, which is what every recipe written before this
    /// field existed means. See <see cref="StructureShape.EmployeeAttachableDimensionTypeIds"/>,
    /// where the same distinction is drawn for the same reason.
    /// </remarks>
    public List<string>? EmployeeAttachableTypeCodes { get; set; }

    public bool IsStrict { get; set; } = true;

    public bool IsPrimaryOrganisation { get; set; }

    /// <summary>Whether this row describes its rules explicitly rather than as a chain.</summary>
    public bool StatesContainment => Containment.Count > 0 || RootTypeCodes.Count > 0;
}
