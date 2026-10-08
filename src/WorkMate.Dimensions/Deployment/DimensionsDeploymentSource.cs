using System.Text.Json.Nodes;
using OrchardCore.Deployment;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Deployment;

/// <summary>
/// Turns this tenant's dimension configuration and data into the three recipe steps the importers
/// already read, in the order they have to be applied.
/// </summary>
/// <remarks>
/// The export writes nothing the importers do not already understand. That is the whole design, and
/// ADR-0011 records why: an export format of its own would be a second description of the same
/// thing, and the moment the two disagree the export is the one nobody notices is wrong — it is
/// only read on the day somebody is relying on it.
///
/// Dependency order is not a convenience here, it is a constraint the importers enforce: a
/// structure resolves its level types by code against types that already exist, a record resolves
/// its type and structure the same way, and a placement resolves its parent by code against a
/// record created earlier in the same step. So types, then structures, then records — and within
/// records, parents before children.
/// </remarks>
internal sealed class DimensionsDeploymentSource : DeploymentSourceBase<DimensionsDeploymentStep>
{
    private readonly IDimensionTypeService _types;
    private readonly IStructureService _structures;
    private readonly IDimensionService _records;
    private readonly IDimensionGraphService _graph;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IEnumerable<IEmployeeLookup> _employees;

    public DimensionsDeploymentSource(
        IDimensionTypeService types,
        IStructureService structures,
        IDimensionService records,
        IDimensionGraphService graph,
        IEmployeeAssignmentService assignments,
        IEnumerable<IEmployeeLookup> employees)
    {
        _types = types;
        _structures = structures;
        _records = records;
        _graph = graph;
        _assignments = assignments;
        _employees = employees;
    }

    protected override async Task ProcessAsync(DimensionsDeploymentStep step, DeploymentPlanResult result)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(result);

        // Retired types and structures are exported too. A retired type still backs the records
        // that were created under it, and an import that skipped it would fail resolving them —
        // "this type is no longer offered" is not the same fact as "this type never existed".
        var types = await _types.ListAsync(includeRetired: true);
        var structures = await _structures.ListAsync();

        var typeCodeById = types.ToDictionary(type => type.DimensionTypeId, type => type.Code, StringComparer.Ordinal);

        if (step.IncludeTypes)
        {
            result.Steps.Add(TypesStep(types));
        }

        if (step.IncludeStructures)
        {
            result.Steps.Add(StructuresStep(structures, typeCodeById));
        }

        if (step.IncludeRecords)
        {
            result.Steps.Add(await RecordsStepAsync(types, structures, typeCodeById));
        }

        if (!step.IncludeAssignments && !step.IncludeHeads)
        {
            return;
        }

        // Both of the remaining steps name people by code, so both need the ids resolved the same
        // way. An employee the lookup cannot name is left out rather than exported by id: an id is
        // not portable, and a row carrying one would fail on import in a way that reads like a
        // missing employee rather than like an export that should not have written it.
        var codeByEmployeeId = await EmployeeCodesAsync(step);
        var codeByStructureId = structures.ToDictionary(
            structure => structure.StructureId, structure => structure.Code, StringComparer.Ordinal);

        // Every record on every type, retired ones included: somebody was placed at a unit that has
        // since closed, and the placement is still part of what happened.
        var allRecords = new List<DimensionNodeRef>();

        foreach (var type in types)
        {
            allRecords.AddRange(await _records.ListByTypeAsync(type.DimensionTypeId));
        }

        var codeByRecordId = allRecords
            .GroupBy(record => record.RecordId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Code, StringComparer.Ordinal);

        if (step.IncludeAssignments)
        {
            result.Steps.Add(AssignmentsStep(
                await _assignments.GetAllAssignmentsAsync(), codeByEmployeeId, codeByStructureId, codeByRecordId));
        }

        if (step.IncludeHeads)
        {
            result.Steps.Add(HeadsStep(
                await _assignments.GetAllHeadAppointmentsAsync(), codeByEmployeeId, codeByStructureId, codeByRecordId));
        }
    }

    /// <summary>
    /// Every employee this export will have to name, by code.
    /// </summary>
    /// <remarks>
    /// Resolved from the ids the assignments and appointments actually mention rather than by
    /// listing the tenant's employees: the export is of the dimension engine's data, and which
    /// people exist is the other module's export to write.
    /// </remarks>
    private async Task<Dictionary<string, string>> EmployeeCodesAsync(DimensionsDeploymentStep step)
    {
        var lookup = _employees.FirstOrDefault();

        if (lookup is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (step.IncludeAssignments)
        {
            foreach (var assignment in await _assignments.GetAllAssignmentsAsync())
            {
                ids.Add(assignment.EmployeeId);
            }
        }

        if (step.IncludeHeads)
        {
            foreach (var appointment in await _assignments.GetAllHeadAppointmentsAsync())
            {
                ids.Add(appointment.EmployeeId);
            }
        }

        var people = await lookup.GetManyAsync([.. ids]);

        return people.ToDictionary(entry => entry.Key, entry => entry.Value.Code, StringComparer.Ordinal);
    }

    /// <summary>
    /// The placements, grouped the way the importer applies them: one entry per employee per axis,
    /// carrying the dated changes, each change the whole set of rows that begin on that date.
    /// </summary>
    /// <remarks>
    /// Grouping by start date is what turns rows back into decisions. A split allocation is several
    /// rows sharing a date and is one decision; a transfer is a later date whose rows replace the
    /// earlier ones, and the engine closed those on import exactly as it closed them here, so the
    /// ranges do not need carrying and cannot disagree.
    ///
    /// An axis somebody has come off entirely — every row closed, nothing starting afterwards —
    /// carries the day they came off it. Without that the import would leave the last placement
    /// running, and the difference between "transferred" and "left the axis" would be lost.
    /// </remarks>
    private static JsonObject AssignmentsStep(
        IReadOnlyList<EmployeeAssignment> assignments,
        Dictionary<string, string> codeByEmployeeId,
        Dictionary<string, string> codeByStructureId,
        Dictionary<string, string> codeByRecordId)
    {
        var entries = new JsonArray();

        var byEmployeeAndStructure = assignments
            .Where(assignment =>
                codeByEmployeeId.ContainsKey(assignment.EmployeeId) &&
                codeByStructureId.ContainsKey(assignment.StructureId) &&
                codeByRecordId.ContainsKey(assignment.RecordId))
            .GroupBy(assignment => (assignment.EmployeeId, assignment.StructureId))
            .OrderBy(group => codeByEmployeeId[group.Key.EmployeeId], StringComparer.Ordinal)
            .ThenBy(group => codeByStructureId[group.Key.StructureId], StringComparer.Ordinal);

        foreach (var group in byEmployeeAndStructure)
        {
            var changes = new JsonArray();
            var dates = group.GroupBy(row => row.Range.From).OrderBy(change => change.Key).ToList();

            foreach (var change in dates)
            {
                changes.Add(new JsonObject
                {
                    ["effectiveFrom"] = Iso(change.Key),
                    ["split"] = new JsonArray(
                    [
                        .. change
                            .OrderByDescending(row => row.IsPrimary)
                            .ThenBy(row => codeByRecordId[row.RecordId], StringComparer.Ordinal)
                            .Select(row => (JsonNode)new JsonObject
                            {
                                ["recordCode"] = codeByRecordId[row.RecordId],
                                ["allocationPercent"] = row.AllocationPercent,
                                ["isPrimary"] = row.IsPrimary,
                            }),
                    ]),
                });
            }

            var entry = new JsonObject
            {
                ["employeeCode"] = codeByEmployeeId[group.Key.EmployeeId],
                ["structureCode"] = codeByStructureId[group.Key.StructureId],
                ["changes"] = changes,
            };

            // The last change's rows all closed, and nothing after them: they are off this axis.
            var last = dates[^1];

            if (last.All(row => row.Range.To is not null))
            {
                entry["endedOn"] = Iso(last.Max(row => row.Range.To!.Value));
            }

            entries.Add(entry);
        }

        return new JsonObject
        {
            ["name"] = "employee-assignments",
            ["assignments"] = entries,
        };
    }

    /// <summary>
    /// The head appointments, one entry per unit per axis, carrying every term it has had.
    /// </summary>
    private static JsonObject HeadsStep(
        IReadOnlyList<HeadAppointment> appointments,
        Dictionary<string, string> codeByEmployeeId,
        Dictionary<string, string> codeByStructureId,
        Dictionary<string, string> codeByRecordId)
    {
        var entries = new JsonArray();

        var byUnit = appointments
            .Where(appointment =>
                codeByEmployeeId.ContainsKey(appointment.EmployeeId) &&
                codeByStructureId.ContainsKey(appointment.StructureId) &&
                codeByRecordId.ContainsKey(appointment.RecordId))
            .GroupBy(appointment => (appointment.StructureId, appointment.RecordId))
            .OrderBy(group => codeByStructureId[group.Key.StructureId], StringComparer.Ordinal)
            .ThenBy(group => codeByRecordId[group.Key.RecordId], StringComparer.Ordinal);

        foreach (var group in byUnit)
        {
            var terms = new JsonArray();

            foreach (var term in group.OrderBy(appointment => appointment.Range.From))
            {
                var node = new JsonObject
                {
                    ["employeeCode"] = codeByEmployeeId[term.EmployeeId],
                    ["effectiveFrom"] = Iso(term.Range.From),
                };

                // Only when the term has ended. An open term is the person who holds the post now,
                // and writing an end for it would import somebody as a former head.
                if (term.Range.To is { } lastDay)
                {
                    node["effectiveTo"] = Iso(lastDay);
                }

                terms.Add(node);
            }

            entries.Add(new JsonObject
            {
                ["structureCode"] = codeByStructureId[group.Key.StructureId],
                ["recordCode"] = codeByRecordId[group.Key.RecordId],
                ["terms"] = terms,
            });
        }

        return new JsonObject
        {
            ["name"] = "unit-heads",
            ["heads"] = entries,
        };
    }

    private static JsonObject TypesStep(IReadOnlyList<DimensionTypeDocument> types) =>
        new()
        {
            ["name"] = "dimension-types",
            ["types"] = new JsonArray(
            [
                .. types
                    .OrderBy(type => type.Code, StringComparer.Ordinal)
                    .Select(type => (JsonNode)new JsonObject
                    {
                        ["code"] = type.Code,
                        ["nameEn"] = type.Name.En,
                        ["nameAr"] = type.Name.Ar,
                        ["allowsSelfNesting"] = type.AllowsSelfNesting,
                        ["attributes"] = new JsonArray(
                        [
                            .. type.AttributeSchema.Select(attribute => (JsonNode)new JsonObject
                            {
                                ["name"] = attribute.Name,
                                ["labelEn"] = attribute.Label.En,
                                ["labelAr"] = attribute.Label.Ar,
                                ["kind"] = attribute.Kind.ToString(),
                                ["isRequired"] = attribute.IsRequired,
                            }),
                        ]),
                    }),
            ]),
        };

    private static JsonObject StructuresStep(
        IReadOnlyList<StructureDocument> structures,
        Dictionary<string, string> typeCodeById)
    {
        string? CodeOf(string dimensionTypeId) =>
            typeCodeById.TryGetValue(dimensionTypeId, out var code) ? code : null;

        return new JsonObject
        {
            ["name"] = "structures",
            ["structures"] = new JsonArray(
            [
                .. structures
                    .OrderBy(structure => structure.Code, StringComparer.Ordinal)
                    .Select(structure =>
                    {
                        // The explicit form, always. A structure's containment is the thing
                        // ADR-0010 made it impossible to derive from the level order, so exporting
                        // the chain-and-skip-flag description would lose every rule a customer
                        // added or removed by hand.
                        var containment = new JsonObject();

                        foreach (var parent in structure.DimensionTypeIds)
                        {
                            var children = structure.DeclaredChildTypeIdsOf(parent)
                                .Select(CodeOf)
                                .Where(code => code is not null)
                                .OrderBy(code => code, StringComparer.Ordinal)
                                .ToList();

                            if (children.Count > 0 && CodeOf(parent) is { } parentCode)
                            {
                                containment[parentCode] = new JsonArray(
                                    [.. children.Select(code => (JsonNode)code!)]);
                            }
                        }

                        return (JsonNode)new JsonObject
                        {
                            ["code"] = structure.Code,
                            ["nameEn"] = structure.Name.En,
                            ["nameAr"] = structure.Name.Ar,
                            ["levelTypeCodes"] = new JsonArray(
                            [
                                .. structure.DimensionTypeIds
                                    .Select(CodeOf)
                                    .Where(code => code is not null)
                                    .Select(code => (JsonNode)code!),
                            ]),
                            ["rootTypeCodes"] = new JsonArray(
                            [
                                .. structure.RootDimensionTypeIds
                                    .Select(CodeOf)
                                    .Where(code => code is not null)
                                    .OrderBy(code => code, StringComparer.Ordinal)
                                    .Select(code => (JsonNode)code!),
                            ]),
                            ["containment"] = containment,
                            ["isStrict"] = structure.IsStrict,
                            ["isPrimaryOrganisation"] = structure.IsPrimaryOrganisation,
                        };
                    }),
            ]),
        };
    }

    private async Task<JsonObject> RecordsStepAsync(
        IReadOnlyList<DimensionTypeDocument> types,
        IReadOnlyList<StructureDocument> structures,
        Dictionary<string, string> typeCodeById)
    {
        var all = new List<DimensionNodeRef>();

        // Every record of every type, including retired ones: a retired unit is what historical
        // reporting resolves through, and an export that dropped it would lose the history its
        // descendants' closure rows are built from.
        foreach (var type in types)
        {
            all.AddRange(await _records.ListByTypeAsync(type.DimensionTypeId));
        }

        var records = all
            .DistinctBy(record => record.RecordId, StringComparer.Ordinal)
            .ToList();

        var codeById = records.ToDictionary(
            record => record.RecordId, record => record.Code, StringComparer.Ordinal);

        // Every decision on every record, read once. The placements are both what gets exported
        // and what decides the order the records are exported in, so reading them twice would be
        // asking the same question of the database twice and risking two different answers.
        var placementsByRecord = new Dictionary<string, List<ExportedPlacement>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var placements = new List<ExportedPlacement>();

            foreach (var structure in structures)
            {
                foreach (var move in await _graph.GetRecordedMovesAsync(structure.StructureId, record.RecordId))
                {
                    placements.Add(new ExportedPlacement(structure.Code, move.ParentRecordId, move.EffectiveFrom));
                }
            }

            placementsByRecord[record.RecordId] = [.. placements.OrderBy(placement => placement.EffectiveFrom)];
        }

        var entries = new List<JsonNode>();

        foreach (var record in ParentsFirst(records, placementsByRecord))
        {
            entries.Add(await RecordEntryAsync(
                record, placementsByRecord[record.RecordId], typeCodeById, codeById));
        }

        return new JsonObject
        {
            ["name"] = "dimension-records",
            ["records"] = new JsonArray([.. entries]),
        };
    }

    /// <summary>One dated placement, as the export carries it.</summary>
    private sealed record ExportedPlacement(string StructureCode, string? ParentRecordId, DateOnly EffectiveFrom);

    private async Task<JsonObject> RecordEntryAsync(
        DimensionNodeRef record,
        IReadOnlyList<ExportedPlacement> moves,
        Dictionary<string, string> typeCodeById,
        Dictionary<string, string> codeById)
    {
        var placements = new JsonArray();

        // Every decision on record, in the order they were made, including the ones that put the
        // unit nowhere. A null parentCode is a dated "no parent" entry since stage D2, and the
        // importer applies it through the same MoveAsync a person's move goes through.
        foreach (var move in moves)
        {
            placements.Add(new JsonObject
            {
                ["structureCode"] = move.StructureCode,
                ["parentCode"] = move.ParentRecordId is { } parentId && codeById.TryGetValue(parentId, out var parentCode)
                    ? parentCode
                    : null,
                ["effectiveFrom"] = Iso(move.EffectiveFrom),
            });
        }

        var history = await _records.GetNameHistoryAsync(record.RecordId);
        var attributes = await _records.GetAttributeValuesAsync(record.RecordId);

        var entry = new JsonObject
        {
            ["code"] = record.Code,
            ["typeCode"] = typeCodeById.TryGetValue(record.DimensionTypeId, out var typeCode)
                ? typeCode
                : record.DimensionTypeId,
            ["nameEn"] = record.NameEn,
            ["nameAr"] = record.NameAr,
            ["effectiveFrom"] = Iso(record.EffectiveRange.From),
            // Stated even when it is zero. An import assigns an unstated sort order from its own
            // tenant's highest, which is the right default for a hand-written recipe and the wrong
            // one here: an export reproduces a tenant, and sibling order is part of what it is.
            ["sortOrder"] = record.SortOrder,
            ["placements"] = placements,
        };

        if (record.EffectiveRange.To is { } retiredOn)
        {
            entry["effectiveTo"] = Iso(retiredOn);
        }

        // The record's own custom fields. Undated, because they are: an attribute is a field on
        // the record's content part with no effective range of its own, unlike its name, its
        // placement and its existence. Omitted entirely when the record has none, so a type with
        // no schema produces no noise.
        if (attributes.Count > 0)
        {
            entry["attributes"] = new JsonArray(
            [
                .. attributes.Select(attribute =>
                {
                    var value = new JsonObject
                    {
                        ["name"] = attribute.Name,
                        ["value"] = attribute.Value,
                    };

                    // Only for a bilingual attribute, which is the only kind that has one. An
                    // empty valueAr on a Text attribute would read as "this has an Arabic half
                    // and it is blank", which is a different claim from "there is no such half".
                    if (!string.IsNullOrEmpty(attribute.ValueAr))
                    {
                        value["valueAr"] = attribute.ValueAr;
                    }

                    return (JsonNode)value;
                }),
            ]);
        }

        // Only when there is more than one period. One period is the name the record has always
        // had, which the fields above already carry.
        if (history.Count > 1)
        {
            entry["nameHistory"] = new JsonArray(
            [
                .. history.Select(period => (JsonNode)new JsonObject
                {
                    ["effectiveFrom"] = Iso(period.Range.From),
                    ["nameEn"] = period.Name.En,
                    ["nameAr"] = period.Name.Ar,
                }),
            ]);
        }

        return entry;
    }

    /// <summary>
    /// Records in an order an import can apply: a parent always before any child it ever had.
    /// </summary>
    /// <remarks>
    /// A correctness requirement, not a tidiness one — the importer resolves a placement's parent
    /// by code against records it has already created, and refuses the row outright if it cannot.
    /// "Ever had" rather than "has today", because the export carries the whole dated history: a
    /// parent a unit left in March still has to exist before the entry that names it.
    ///
    /// Depth-first with a visited set. A cycle cannot occur — the validator refuses one on every
    /// write, across every date — but the visited set is what makes that assumption safe to make
    /// rather than something this method would hang on if it were ever wrong.
    /// </remarks>
    private static List<DimensionNodeRef> ParentsFirst(
        List<DimensionNodeRef> records,
        IReadOnlyDictionary<string, List<ExportedPlacement>> placementsByRecord)
    {
        var byId = records.ToDictionary(record => record.RecordId, StringComparer.Ordinal);
        var ordered = new List<DimensionNodeRef>(records.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        void Emit(DimensionNodeRef record)
        {
            if (!placed.Add(record.RecordId) || !visiting.Add(record.RecordId))
            {
                return;
            }

            foreach (var placement in placementsByRecord.GetValueOrDefault(record.RecordId, []))
            {
                if (placement.ParentRecordId is { } parentId && byId.TryGetValue(parentId, out var parent))
                {
                    Emit(parent);
                }
            }

            visiting.Remove(record.RecordId);
            ordered.Add(record);
        }

        // A stable starting order, so two exports of the same tenant produce the same file and a
        // diff between them is a difference in the data rather than in the iteration.
        foreach (var record in records.OrderBy(record => record.Code, StringComparer.Ordinal))
        {
            Emit(record);
        }

        return ordered;
    }

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
