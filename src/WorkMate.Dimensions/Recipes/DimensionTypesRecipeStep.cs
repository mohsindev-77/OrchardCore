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
            var toFill = new List<DimensionTypeStepEntry>();

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
                    else if (FillsTheMenuOrderGap(existing, type))
                    {
                        toFill.Add(type);
                    }

                    // Matches, or matches but for a gap to fill: nothing to validate or create.
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

            // The gaps first. Each goes through IDimensionTypeService.UpdateAsync rather than a
            // direct document write, so it is validated and audited exactly as an administrator's
            // own edit on the Dimension types screen would be — and it changes nothing except the
            // one field, because every other argument is read back from the type as it stands.
            // The same shape StructuresRecipeStep uses for its own gap.
            foreach (var type in toFill)
            {
                var existing = await _dimensionTypeService.GetByCodeAsync(type.Code);

                if (existing is null)
                {
                    continue;
                }

                var filled = await _dimensionTypeService.UpdateAsync(
                    existing.DimensionTypeId,
                    existing.Name,
                    existing.AttributeSchema,
                    existing.AllowsSelfNesting,
                    new DimensionTypeMenu(
                        existing.ShowInMenu,
                        existing.PluralName,
                        type.MenuOrder ?? existing.MenuOrder),

                    // Null, so a row that says nothing about the role leaves it alone. Filling one
                    // gap must not be a way of quietly restating everything else.
                    standardRole: null);

                if (!filled.Succeeded)
                {
                    RecipeStepFailures.Throw(
                        context,
                        filled.Errors.Count > 0
                            ? filled.Errors.Select(error => error.Message.Value)
                            : [S["'{0}' could not be updated.", type.Code].Value]);

                    return;
                }
            }

            foreach (var type in toCreate)
            {
                var result = await _dimensionTypeService.CreateAsync(
                    type.Code,
                    new BilingualText(type.NameEn, type.NameAr),
                    type.ToAttributeSchema(),
                    type.AllowsSelfNesting,
                    menu: type.ToMenu(),
                    standardRole: type.Role);

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

        // The menu settings, each only when the row states it — the same rule, for the same reason.
        // A recipe written before ADR-0024 brought the per-dimension menu back states none of them,
        // and must not refuse a re-run against a tenant whose types have since been given plurals.
        if (type.PluralEn is { } pluralEn && !RecipeNameComparison.Same(existing.PluralName.En, pluralEn))
        {
            differences.Add(RecipeNameComparison.Describe("menu plural", existing.PluralName.En, pluralEn));
        }

        if (type.PluralAr is { } pluralAr && !RecipeNameComparison.Same(existing.PluralName.Ar, pluralAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic menu plural", existing.PluralName.Ar, pluralAr));
        }

        if (type.ShowInMenu is { } showInMenu && existing.ShowInMenu != showInMenu)
        {
            differences.Add($"showInMenu is {existing.ShowInMenu} in the tenant but {showInMenu} in the recipe");
        }

        // Menu order, and the one place this step departs from "different means fail".
        //
        // ADR-0008's 14 October addendum: a stored zero is the absence of an answer rather than a
        // different one. The field arrived with slice F2, so no tenant created before it could have
        // authored a value, and zero is what every type born before it deserialises to. A row that
        // states an order against a tenant holding zero is therefore filling a gap, not overwriting
        // a decision — see FillsTheMenuOrderGap, which carries the argument and its one risk.
        if (type.MenuOrder is { } menuOrder &&
            existing.MenuOrder != menuOrder &&
            existing.MenuOrder != UnorderedMenuPosition)
        {
            differences.Add($"menuOrder is {existing.MenuOrder} in the tenant but {menuOrder} in the recipe");
        }

        // The role, under the same rule again: a recipe written before roles existed states none
        // and must not refuse a re-run against a tenant whose Position dimension has since been
        // given one.
        if (type.Role is { } role && existing.StandardRole != role)
        {
            differences.Add($"role is {existing.StandardRole} in the tenant but {role} in the recipe");
        }

        return differences;
    }

    /// <summary>
    /// The menu order a type has when nobody has given it one. <c>DimensionTypeMenu.Default</c>'s.
    /// </summary>
    private const int UnorderedMenuPosition = 0;

    /// <summary>
    /// Whether this row is filling in a menu order the tenant has never had, rather than changing
    /// one it holds. ADR-0008's 14 October addendum.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> <c>MenuOrder</c> arrived with slice F2. Every dimension
    /// type created before it deserialises to zero, and the demo recipes were then given orders —
    /// so re-applying one of them to a tenant built from its earlier version failed on every row at
    /// once: <em>menuOrder is 0 in the tenant but 10 in the recipe</em>. The recipes that most need
    /// re-applying, because they are how a tenant gets new demo content, are exactly the ones this
    /// refused.
    ///
    /// <b>Why zero is read as "nobody said".</b> It is the value the field was born with, and the
    /// field did not exist when those types were created, so no customer can have chosen it there.
    /// That is the same argument <c>Migrations.UpdateFrom11Async</c> makes for back-filling an
    /// unauthored plural, and the same one ADR-0008's 11 October addendum makes for an empty
    /// employee-attachment set.
    ///
    /// <b>The risk, stated rather than hidden.</b> Zero is also a legitimate order somebody could
    /// type on the Dimension types screen to put a dimension first, and this cannot tell the two
    /// apart. It is accepted because the cost of being wrong is a menu position a customer can
    /// change in one field, while the cost of refusing is a recipe nobody can re-apply — and
    /// because a tenant that has deliberately ordered its menu has almost certainly given the other
    /// entries non-zero orders, which are compared and still refused.
    ///
    /// <b>Only the order.</b> A plural, a <c>showInMenu</c> or a role that differs is still a
    /// difference, because for each of those the tenant's value is one somebody could have meant.
    /// </remarks>
    private static bool FillsTheMenuOrderGap(DimensionTypeDocument existing, DimensionTypeStepEntry type) =>
        type.MenuOrder is { } menuOrder &&
        menuOrder != UnorderedMenuPosition &&
        existing.MenuOrder == UnorderedMenuPosition;

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
    /// The plural the Organisation menu shows for this dimension, in each language (ADR-0024).
    /// </summary>
    /// <remarks>
    /// <b>Nullable, so that "absent" and "stated as empty" are different things</b> — the same
    /// reason <c>sortOrder</c> and <c>aliasEn</c> are nullable on the <c>dimension-records</c> step.
    /// A row written before the menu came back says nothing about a plural, and ADR-0008's
    /// comparison is scoped to what a row states, so re-running it against a tenant whose types
    /// have since been given plurals must skip rather than refuse. A row that states <c>""</c> is
    /// claiming the dimension has no authored plural — which reads as the singular — and a tenant
    /// where it has one is a difference the step reports.
    /// </remarks>
    public string? PluralEn { get; set; }

    /// <inheritdoc cref="PluralEn"/>
    public string? PluralAr { get; set; }

    /// <summary>Whether this dimension gets its own entry in the Organisation menu.</summary>
    /// <inheritdoc cref="PluralEn" path="/remarks"/>
    public bool? ShowInMenu { get; set; }

    /// <summary>Where it sits among the others. Lower first.</summary>
    /// <inheritdoc cref="PluralEn" path="/remarks"/>
    public int? MenuOrder { get; set; }

    /// <summary>
    /// The part this dimension plays in the product: <c>Position</c>, <c>Grade</c> or absent.
    /// </summary>
    /// <remarks>
    /// <b>This is how the product finds its Position and Grade dimensions</b> — declared by the
    /// recipe that ships them, never guessed from a code or a name. <see cref="DimensionTypeRole"/>
    /// sets out why each available guess is wrong, and a customer building their own grade
    /// dimension states it here or on the Dimension types screen.
    ///
    /// Nullable under the same rule as <see cref="PluralEn"/>: absent says nothing, so a recipe
    /// written before roles existed re-runs cleanly against a tenant whose Position dimension has
    /// since been given one.
    /// </remarks>
    public DimensionTypeRole? Role { get; set; }

    /// <summary>
    /// The menu settings as the service's value, or null when the row states none of them.
    /// </summary>
    /// <remarks>
    /// Null is what makes a row that says nothing about the menu leave a tenant's authored plural
    /// alone, which is <c>IDimensionTypeService.UpdateAsync</c>'s rule for a null menu and the same
    /// rule <c>StructureShape.EmployeeAttachableDimensionTypeIds</c> states for its own nullable. A
    /// row that states <em>any</em> of the four is describing the menu, and the ones it leaves out
    /// take the default rather than the stored value — because a half-stated menu is still a
    /// statement, and guessing which half was meant is worse than taking the documented default.
    /// </remarks>
    public DimensionTypeMenu? ToMenu() =>
        PluralEn is null && PluralAr is null && ShowInMenu is null && MenuOrder is null
            ? null
            : new DimensionTypeMenu(
                ShowInMenu ?? DimensionTypeMenu.Default.ShowInMenu,
                new BilingualText(PluralEn ?? string.Empty, PluralAr ?? string.Empty),
                MenuOrder ?? DimensionTypeMenu.Default.MenuOrder);

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
