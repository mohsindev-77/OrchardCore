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
                    differences.AddRange(await AttributeDifferencesAsync(existing, record));

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
                // Created with the name it opened under, not the one it currently holds, when the
                // row carries a history: the renames below walk it forward from there. Creating it
                // with the latest name and then renaming to the same thing would leave the earlier
                // periods carrying a name the record did not have then.
                var opening = record.NameHistory.Count > 0
                    ? record.NameHistory.OrderBy(period => period.EffectiveFrom).First()
                    : null;

                var created = await _dimensionService.CreateAsync(
                    typeIdByRecord[record],
                    record.Code,
                    opening is null
                        ? new BilingualText(record.NameEn, record.NameAr)
                        : new BilingualText(opening.NameEn, opening.NameAr),
                    new EffectiveRange(record.EffectiveFrom, record.EffectiveTo),
                    batch: null,
                    // Stated, or the order the recipe lists them in: records are created here in
                    // list order and an unstated sort order is assigned from the tenant's highest,
                    // so "Engineering, Projects, Corporate" in the file is what the chart shows.
                    record.SortOrder);

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

                // Before the placements, so a record is complete the moment it is on the tree.
                // Validated against its type's schema by the service, so an attribute the type
                // does not declare, or a value that is not of its declared kind, fails the step
                // naming the attribute rather than being written and ignored.
                if (record.Attributes.Count > 0)
                {
                    var written = await _dimensionService.SetAttributeValuesAsync(
                        created.Value.RecordId, record.ToAttributeValues());

                    if (!written.Succeeded)
                    {
                        RecipeStepFailures.Throw(
                            context,
                            written.Errors.Count > 0
                                ? written.Errors.Select(error => error.Message.Value)
                                : [S["'{0}' could not be given its attribute values.", record.Code].Value]);

                        return;
                    }
                }

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

                // The renames, after the placements and in date order. Each is applied as the
                // substantive rename it was, so the imported record ends up with the same dated
                // periods rather than with one period carrying the latest name. The first period
                // is the name the record was created with and is already in place.
                foreach (var period in record.NameHistory
                    .OrderBy(period => period.EffectiveFrom)
                    .Skip(1))
                {
                    var renamed = await _dimensionService.RenameAsync(
                        recordIdByCode[record.Code],
                        new BilingualText(period.NameEn, period.NameAr),
                        period.EffectiveFrom);

                    if (!renamed.Succeeded)
                    {
                        RecipeStepFailures.Throw(
                            context,
                            renamed.Errors.Count > 0
                                ? renamed.Errors.Select(error => error.Message.Value)
                                : [S["'{0}' could not be renamed.", record.Code].Value]);

                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// How the tenant's attribute values differ from the ones this row states, per ADR-0008.
    /// </summary>
    /// <remarks>
    /// Scoped to the attributes the row actually states, like every other comparison in these
    /// steps: a row written before attributes could be carried says nothing about them, and
    /// reporting "the tenant has three and the recipe has none" would refuse a re-run of a recipe
    /// that is still perfectly correct. An attribute the row states and the tenant does not hold
    /// <em>is</em> a difference — that is the row making a claim the tenant contradicts.
    ///
    /// Named individually rather than as "the attributes differ", because the whole point of the
    /// ADR-0008 message is that an operator can see what to change.
    /// </remarks>
    private async Task<List<string>> AttributeDifferencesAsync(
        DimensionNodeRef existing, DimensionRecordStepEntry record)
    {
        var differences = new List<string>();

        if (record.Attributes.Count == 0)
        {
            return differences;
        }

        var held = (await _dimensionService.GetAttributeValuesAsync(existing.RecordId))
            .ToDictionary(value => value.Name, StringComparer.Ordinal);

        foreach (var wanted in record.ToAttributeValues())
        {
            held.TryGetValue(wanted.Name, out var current);

            if (!RecipeNameComparison.Same(current?.Value, wanted.Value))
            {
                differences.Add(RecipeNameComparison.Describe(
                    $"attribute '{wanted.Name}'", current?.Value, wanted.Value));
            }

            if (!RecipeNameComparison.Same(current?.ValueAr, wanted.ValueAr))
            {
                differences.Add(RecipeNameComparison.Describe(
                    $"Arabic half of attribute '{wanted.Name}'", current?.ValueAr, wanted.ValueAr));
            }
        }

        return differences;
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
            differences.Add(RecipeNameComparison.Describe("English name", existing.NameEn, record.NameEn));
        }

        if (!RecipeNameComparison.Same(existing.NameAr, record.NameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic name", existing.NameAr, record.NameAr));
        }

        if (existing.EffectiveRange != range)
        {
            differences.Add($"the effective range is {existing.EffectiveRange} in the tenant but {range} in the recipe");
        }

        // Only when the row states one, per ADR-0008: a recipe that says nothing about sort order
        // is not claiming the record has none, and the order it was created in is still correct.
        if (record.SortOrder is { } sortOrder && existing.SortOrder != sortOrder)
        {
            differences.Add($"the sort order is {existing.SortOrder} in the tenant but {sortOrder} in the recipe");
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

    /// <summary>
    /// Where this record sorts among its siblings, when the recipe cares enough to say.
    /// </summary>
    /// <remarks>
    /// Usually absent, and absent means the order the file lists the records in: the step creates
    /// them in that order and the service assigns each one the next sort order in the tenant. An
    /// export states it, because an export is reproducing a tenant rather than describing an
    /// intent, and the record's order there is a fact that has to survive the trip.
    /// </remarks>
    public int? SortOrder { get; set; }

    /// <summary>
    /// Where this record has sat, over time. Empty for a record that has never been placed.
    /// </summary>
    /// <remarks>
    /// A list of dated decisions, not a single current placement: a record that moved in March and
    /// again in June has two entries, and one taken off the tree has an entry naming no parent. An
    /// import applies them in order through the same <c>MoveAsync</c> a person's move goes through,
    /// so the ranges it ends up with are the ones the engine would have produced.
    /// </remarks>
    public List<DimensionRecordPlacementStepEntry> Placements { get; set; } = [];

    /// <summary>
    /// What this record has been called, over time, when that is more than one thing.
    /// </summary>
    /// <remarks>
    /// Only substantive renames leave a history worth carrying — a corrective rename rewrites the
    /// past on purpose, so it has no earlier period to reproduce. Empty for a record that has never
    /// been renamed, whose single name is <see cref="NameEn"/>/<see cref="NameAr"/> already.
    ///
    /// Without this an export would flatten the history: every report for a period before the
    /// rename would resolve to the new name in the imported tenant, which is exactly the
    /// difference between the two kinds of rename that architecture section 5 exists to keep.
    /// </remarks>
    public List<DimensionRecordNameStepEntry> NameHistory { get; set; } = [];

    /// <summary>
    /// The record's custom attribute values, by attribute name.
    /// </summary>
    /// <remarks>
    /// Undated, because attribute values are: they are fields on the record's own content part
    /// with no effective range, unlike everything else this entry carries. Optional, so every
    /// recipe written before ADR-0011's addendum still means what it said.
    /// </remarks>
    public List<DimensionRecordAttributeStepEntry> Attributes { get; set; } = [];

    public IReadOnlyList<DimensionAttributeValue> ToAttributeValues() =>
        [.. Attributes.Select(attribute =>
            new DimensionAttributeValue(attribute.Name, attribute.Value, attribute.ValueAr))];
}

/// <summary>One custom attribute value on a record, as a recipe states it.</summary>
internal sealed class DimensionRecordAttributeStepEntry
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The value as text, read invariantly: <c>yyyy-MM-dd</c> for a date, an invariant decimal for
    /// a number, <c>true</c>/<c>false</c> for a boolean, and the text itself otherwise.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>The Arabic half, for a bilingual attribute and nothing else.</summary>
    public string? ValueAr { get; set; }
}

/// <summary>One period a record was called something, as a recipe states it.</summary>
internal sealed class DimensionRecordNameStepEntry
{
    public DateOnly EffectiveFrom { get; set; }

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;
}

internal sealed class DimensionRecordPlacementStepEntry
{
    public string StructureCode { get; set; } = string.Empty;

    /// <summary>The parent's code, resolved at import, or null to place this record as a root.</summary>
    public string? ParentCode { get; set; }

    public DateOnly EffectiveFrom { get; set; }
}
