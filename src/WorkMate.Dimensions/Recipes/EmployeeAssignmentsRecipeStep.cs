using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Platform.Recipes;
using WorkMate.Platform.Services;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// The <c>employee-assignments</c> recipe step: where people work, over time, on every axis.
/// </summary>
/// <remarks>
/// <b>A change, not a placement, is the unit.</b> Each entry is one employee on one axis, carrying
/// the dated changes to where they sit; each change is the whole set of concurrent placements from
/// that date — one row for an ordinary placement, several for a split allocation. That is the shape
/// <see cref="IEmployeeAssignmentService.ReallocateAsync"/> takes, and it takes it because the
/// rules that matter are about the set: the allocations effective at once total 100 and exactly one
/// of them is primary. A file listing placements one at a time would be describing something
/// invalid at every line but the last, and the step would either have to refuse it or suspend the
/// rules while it caught up.
///
/// It also makes a transfer read as what it is. Somebody who moved department on 15 March has two
/// changes, the second replacing the first from that date, and the engine closes the earlier rows
/// on the 14th. The import therefore produces the same ranges the screen would have.
///
/// <b>Re-running is safe, per ADR-0008.</b> A change whose date the tenant already holds is
/// compared against the placements effective on that date — node, allocation and which is primary
/// — and skipped with nothing written if they match; the step fails, naming the employee, the axis
/// and the date, if they do not.
///
/// <b>Everything resolves and validates before anything is written.</b> Employee codes, structure
/// codes and every record code in every split are resolved first, across the whole step, so a file
/// with a typo in its last line leaves the tenant as it found it. The placement rules themselves
/// are enforced by <c>ReallocateAsync</c> as each change is applied, because they are rules about
/// the tree on a date and cannot be answered before the earlier changes exist.
/// </remarks>
internal sealed class EmployeeAssignmentsRecipeStep : IRecipeStepHandler
{
    private const string StepName = "employee-assignments";

    private readonly IStructureService _structures;
    private readonly IDimensionService _records;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IEnumerable<IEmployeeLookup> _employees;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public EmployeeAssignmentsRecipeStep(
        IStructureService structures,
        IDimensionService records,
        IEmployeeAssignmentService assignments,
        IEnumerable<IEmployeeLookup> employees,
        ISystemOperation systemOperation,
        IStringLocalizer<EmployeeAssignmentsRecipeStep> stringLocalizer)
    {
        _structures = structures;
        _records = records;
        _assignments = assignments;
        _employees = employees;
        _systemOperation = systemOperation;
        S = stringLocalizer;
    }

    public async Task ExecuteAsync(RecipeExecutionContext context)
    {
        if (!string.Equals(context.Name, StepName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var model = context.Step.ToObject<EmployeeAssignmentsStepModel>() ?? new EmployeeAssignmentsStepModel();

        if (model.Assignments.Count == 0)
        {
            return;
        }

        using (_systemOperation.Begin("employee-assignments recipe step"))
        {
            var lookup = _employees.FirstOrDefault();

            if (lookup is null)
            {
                RecipeStepFailures.Throw(context, [
                    S["Employee assignments cannot be imported because no module providing employee records is enabled."].Value,
                ]);

                return;
            }

            var errors = new List<string>();
            var resolved = new List<ResolvedEntry>();

            foreach (var entry in model.Assignments)
            {
                var employee = await lookup.GetByCodeAsync(entry.EmployeeCode);

                if (employee is null)
                {
                    errors.Add(S["Assignment names employee '{0}', who does not exist.", entry.EmployeeCode].Value);
                    continue;
                }

                var structure = await _structures.GetByCodeAsync(entry.StructureCode);

                if (structure is null)
                {
                    errors.Add(S["Employee '{0}' is assigned on structure '{1}', which does not exist.", entry.EmployeeCode, entry.StructureCode].Value);
                    continue;
                }

                var changes = new List<ResolvedChange>();

                foreach (var change in entry.Changes.OrderBy(change => change.EffectiveFrom))
                {
                    var split = new List<AssignmentSplitEntry>();

                    foreach (var row in change.Split)
                    {
                        var record = await _records.GetByCodeAsync(row.RecordCode);

                        if (record is null)
                        {
                            errors.Add(S["Employee '{0}' is placed at unit '{1}', which does not exist.", entry.EmployeeCode, row.RecordCode].Value);
                            continue;
                        }

                        split.Add(new AssignmentSplitEntry(record.RecordId, row.AllocationPercent, row.IsPrimary));
                    }

                    if (split.Count != change.Split.Count)
                    {
                        continue;
                    }

                    changes.Add(new ResolvedChange(change.EffectiveFrom, split));
                }

                if (changes.Count != entry.Changes.Count)
                {
                    continue;
                }

                resolved.Add(new ResolvedEntry(entry, employee.EmployeeId, structure.StructureId, changes));
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            // A second pass over what resolved, comparing each change against the tenant. Done
            // before any write for the reason the whole step is: a file that disagrees with the
            // tenant halfway down should change nothing at all, not the half above the
            // disagreement.
            var toApply = new List<(ResolvedEntry Entry, List<ResolvedChange> Changes)>();

            foreach (var entry in resolved)
            {
                var outstanding = new List<ResolvedChange>();

                foreach (var change in entry.Changes)
                {
                    var held = await _assignments.GetAllEffectiveAsync(
                        entry.EmployeeId, entry.StructureId, change.EffectiveFrom);

                    if (held.Count == 0)
                    {
                        outstanding.Add(change);
                        continue;
                    }

                    var differences = Differences(held, change);

                    if (differences.Count > 0)
                    {
                        errors.Add(S["Employee '{0}' on structure '{1}' is already placed differently from {2}: {3}.",
                            entry.Entry.EmployeeCode,
                            entry.Entry.StructureCode,
                            change.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                            string.Join("; ", differences)].Value);
                    }
                }

                if (outstanding.Count > 0)
                {
                    toApply.Add((entry, outstanding));
                }
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            foreach (var (entry, changes) in toApply)
            {
                foreach (var change in changes)
                {
                    var applied = await _assignments.ReallocateAsync(
                        entry.EmployeeId, entry.StructureId, change.Split, change.EffectiveFrom);

                    if (!applied.Succeeded)
                    {
                        RecipeStepFailures.Throw(
                            context,
                            applied.Errors.Count > 0
                                ? applied.Errors.Select(error => error.Message.Value)
                                : [S["Employee '{0}' could not be placed on structure '{1}'.", entry.Entry.EmployeeCode, entry.Entry.StructureCode].Value]);

                        return;
                    }
                }

                // An axis somebody has left carries the date they left it. Applied after the
                // changes, because it closes what they put in place.
                if (entry.Entry.EndedOn is { } lastDay)
                {
                    var ended = await _assignments.EndAsync(entry.EmployeeId, entry.StructureId, lastDay);

                    if (!ended.Succeeded)
                    {
                        RecipeStepFailures.Throw(
                            context,
                            ended.Errors.Count > 0
                                ? ended.Errors.Select(error => error.Message.Value)
                                : [S["Employee '{0}' could not be taken off structure '{1}'.", entry.Entry.EmployeeCode, entry.Entry.StructureCode].Value]);

                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// How the placements the tenant holds on a date differ from the ones a change states.
    /// </summary>
    /// <remarks>
    /// Compared as a set of (node, allocation, primary), because that is what a change is: the
    /// order rows come back in is the service's business, and two files describing the same split
    /// in a different order describe the same split.
    /// </remarks>
    private static List<string> Differences(IReadOnlyList<EmployeeAssignment> held, ResolvedChange change)
    {
        var differences = new List<string>();

        var inTenant = held
            .Select(row => (row.RecordId, row.AllocationPercent, row.IsPrimary))
            .OrderBy(row => row.RecordId, StringComparer.Ordinal)
            .ToList();

        var inRecipe = change.Split
            .Select(row => (row.RecordId, row.AllocationPercent, row.IsPrimary))
            .OrderBy(row => row.RecordId, StringComparer.Ordinal)
            .ToList();

        if (inTenant.Count != inRecipe.Count)
        {
            differences.Add($"the tenant has {inTenant.Count} placement(s) on that date and the recipe states {inRecipe.Count}");

            return differences;
        }

        for (var index = 0; index < inTenant.Count; index++)
        {
            if (inTenant[index] != inRecipe[index])
            {
                differences.Add("a placement differs in its unit, its allocation or which one is primary");

                return differences;
            }
        }

        return differences;
    }

    private sealed record ResolvedEntry(
        EmployeeAssignmentStepEntry Entry,
        string EmployeeId,
        string StructureId,
        List<ResolvedChange> Changes);

    private sealed record ResolvedChange(DateOnly EffectiveFrom, IReadOnlyList<AssignmentSplitEntry> Split);
}

internal sealed class EmployeeAssignmentsStepModel
{
    public List<EmployeeAssignmentStepEntry> Assignments { get; set; } = [];
}

/// <summary>One employee's placements on one axis, as a recipe states them.</summary>
internal sealed class EmployeeAssignmentStepEntry
{
    public string EmployeeCode { get; set; } = string.Empty;

    public string StructureCode { get; set; } = string.Empty;

    /// <summary>Each dated decision about where this person sits on this axis, earliest first.</summary>
    public List<EmployeeAssignmentChangeStepEntry> Changes { get; set; } = [];

    /// <summary>
    /// The last day they were on this axis at all, when they have come off it without being
    /// placed somewhere else.
    /// </summary>
    /// <remarks>
    /// Distinct from a change naming a different unit, which is a transfer. This is the end of
    /// their presence on the axis — a project-hired foreman whose project finished, say — and
    /// without it the export could not tell the two apart.
    /// </remarks>
    public DateOnly? EndedOn { get; set; }
}

/// <summary>Where somebody sits from one date: one row, or several that split them.</summary>
internal sealed class EmployeeAssignmentChangeStepEntry
{
    public DateOnly EffectiveFrom { get; set; }

    public List<EmployeeAssignmentSplitStepEntry> Split { get; set; } = [];
}

/// <summary>One of the concurrent placements in a change.</summary>
internal sealed class EmployeeAssignmentSplitStepEntry
{
    public string RecordCode { get; set; } = string.Empty;

    /// <summary>How much of the person this placement accounts for. The split totals 100.</summary>
    public decimal AllocationPercent { get; set; } = 100m;

    /// <summary>Whether this is the one that answers "where do they work". Exactly one is.</summary>
    public bool IsPrimary { get; set; }
}
