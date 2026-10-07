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
/// The <c>dimension-types</c> recipe step: creates dimension types, in dependency order ahead of
/// <c>structures</c> and <c>dimension-records</c>.
/// </summary>
/// <remarks>
/// Re-running a recipe must be safe, which is why a row naming a code that already exists is not
/// automatically an error: if the existing type's content matches the row exactly, the row is
/// skipped — no write, no error — and if it differs, the step fails naming the code and the field
/// that differs, rather than silently overwriting someone's data or silently doing nothing. See the
/// README's note on recipe re-run behaviour for why this was chosen over making the whole recipe
/// one transaction.
///
/// Every row still validates against one <see cref="DimensionValidationBatch"/> before anything new
/// is created, so two rows sharing a code are caught before either is written — architecture
/// section 6's "an import validates every row and is rejected whole if the result would be
/// invalid" — and the step throws <see cref="RecipeExecutionException"/> naming every problem at
/// once rather than stopping at the first.
///
/// Carries an optional attribute schema since ADR-0011: an export that cannot reproduce a
/// Project's start date does not reproduce the tenant it came from. Optional, so a recipe written
/// before that still means what it said, and the existing-content comparison stays scoped to the
/// fields a row actually states — a row with no "attributes" key says nothing about the schema
/// rather than claiming the type has none.
/// </remarks>
internal sealed class DimensionTypesRecipeStep : IRecipeStepHandler
{
    private const string StepName = "dimension-types";

    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IDimensionValidator _validator;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public DimensionTypesRecipeStep(
        IDimensionTypeService dimensionTypeService,
        IDimensionValidator validator,
        ISystemOperation systemOperation,
        IStringLocalizer<DimensionTypesRecipeStep> stringLocalizer)
    {
        _dimensionTypeService = dimensionTypeService;
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

        var model = context.Step.ToObject<DimensionTypesStepModel>() ?? new DimensionTypesStepModel();

        using (_systemOperation.Begin("dimension-types recipe step"))
        {
            var batch = _validator.BeginBatch();
            var errors = new List<string>();
            var toCreate = new List<DimensionTypeStepEntry>();

            foreach (var type in model.Types)
            {
                var existing = await _dimensionTypeService.GetByCodeAsync(type.Code);

                if (existing is not null)
                {
                    var differences = DifferencesFrom(existing, type);

                    if (differences.Count > 0)
                    {
                        errors.Add(S["Dimension type '{0}' already exists with different content: {1}.", type.Code, string.Join("; ", differences)].Value);
                    }

                    // Matches exactly: nothing to validate or create for this row.
                    continue;
                }

                var name = new BilingualText(type.NameEn, type.NameAr);
                var violations = await _validator.ValidateDimensionTypeAsync(
                    null, type.Code, name, type.ToAttributeSchema(), batch);

                errors.AddRange(violations.Select(error => error.Message.Value));
                toCreate.Add(type);
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            foreach (var type in toCreate)
            {
                var result = await _dimensionTypeService.CreateAsync(
                    type.Code,
                    new BilingualText(type.NameEn, type.NameAr),
                    type.ToAttributeSchema(),
                    type.AllowsSelfNesting);

                if (!result.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        result.Errors.Count > 0
                            ? result.Errors.Select(error => error.Message.Value)
                            : [S["'{0}' could not be created.", type.Code].Value]);

                    return;
                }
            }
        }
    }

    private static List<string> DifferencesFrom(DimensionTypeDocument existing, DimensionTypeStepEntry type)
    {
        var differences = new List<string>();

        if (!RecipeNameComparison.Same(existing.Name.En, type.NameEn))
        {
            differences.Add(RecipeNameComparison.Describe("English name", existing.Name.En, type.NameEn));
        }

        if (!RecipeNameComparison.Same(existing.Name.Ar, type.NameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic name", existing.Name.Ar, type.NameAr));
        }

        if (existing.AllowsSelfNesting != type.AllowsSelfNesting)
        {
            differences.Add($"allowsSelfNesting is {existing.AllowsSelfNesting} in the tenant but {type.AllowsSelfNesting} in the recipe");
        }

        // Compared only when the row states one. ADR-0008 scopes the comparison to the fields a
        // row actually states, and a row written before the schema could be carried says nothing
        // about it — reporting "the tenant has three attributes and the recipe has none" for such
        // a row would refuse a re-run of a recipe that is still perfectly correct.
        if (type.Attributes.Count > 0)
        {
            var wanted = type.ToAttributeSchema();
            var held = existing.AttributeSchema;

            if (held.Count != wanted.Count ||
                held.Zip(wanted).Any(pair => !SameAttribute(pair.First, pair.Second)))
            {
                differences.Add("the attribute schema differs from the tenant's existing type");
            }
        }

        return differences;
    }

    private static bool SameAttribute(DimensionAttributeDefinition held, DimensionAttributeDefinition wanted) =>
        string.Equals(held.Name, wanted.Name, StringComparison.Ordinal) &&
        held.Kind == wanted.Kind &&
        held.IsRequired == wanted.IsRequired &&
        RecipeNameComparison.Same(held.Label.En, wanted.Label.En) &&
        RecipeNameComparison.Same(held.Label.Ar, wanted.Label.Ar);
}

internal sealed class DimensionTypesStepModel
{
    public List<DimensionTypeStepEntry> Types { get; set; } = [];
}

internal sealed class DimensionTypeStepEntry
{
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Legacy. Whether this type once permitted a record of it inside another of it.
    /// </summary>
    /// <remarks>
    /// Nothing reads it when deciding a placement any more — ADR-0010's addendum made the
    /// structure's containment map the only authority for self-nesting, diagonal included — and no
    /// screen sets it. It is still accepted, still stored and still compared on re-run, because one
    /// thing still consumes it: a <c>structures</c> row written as a plain chain
    /// (<c>levelTypeCodes</c> + <c>allowSkipLevel</c>) derives its map from the types' flags, and
    /// dropping it would silently change what such a row means. A row that states
    /// <c>rootTypeCodes</c> and <c>containment</c>, which is what every recipe here and every
    /// export now writes, never consults it.
    /// </remarks>
    public bool AllowsSelfNesting { get; set; }

    /// <summary>
    /// The fields this type's records carry beyond the standard ones, in order.
    /// </summary>
    /// <remarks>
    /// ADR-0008 recorded that a type's schema was built through the admin screen and not through
    /// the recipe, so a seeded type arrived with no attributes. ADR-0011 needed it: an export that
    /// cannot carry the schema cannot reproduce the tenant, and a Project with no start date is
    /// not the Project that was exported. Optional, so every recipe written before this still
    /// means what it said.
    /// </remarks>
    public List<DimensionAttributeStepEntry> Attributes { get; set; } = [];

    public IReadOnlyList<DimensionAttributeDefinition> ToAttributeSchema() =>
        [.. Attributes.Select(attribute => attribute.ToDefinition())];
}

/// <summary>One field on a dimension type's attribute schema, as a recipe states it.</summary>
internal sealed class DimensionAttributeStepEntry
{
    public string Name { get; set; } = string.Empty;

    public string LabelEn { get; set; } = string.Empty;

    public string LabelAr { get; set; } = string.Empty;

    /// <summary>Text, BilingualText, Number, Date or Boolean, by name.</summary>
    public DimensionAttributeKind Kind { get; set; }

    public bool IsRequired { get; set; }

    public DimensionAttributeDefinition ToDefinition() =>
        new(Name, new BilingualText(LabelEn, LabelAr), Kind, IsRequired);
}
