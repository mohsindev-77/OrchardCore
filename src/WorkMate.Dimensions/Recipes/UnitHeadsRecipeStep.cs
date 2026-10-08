using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Platform.Recipes;
using WorkMate.Platform.Services;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Recipes;

/// <summary>
/// The <c>unit-heads</c> recipe step: who has led each unit, and when.
/// </summary>
/// <remarks>
/// Its own step rather than a field on an assignment, because a head appointment is its own dated
/// record — ADR-0012. A head need not work in the unit they head, so there is no assignment to
/// hang the fact on; and one person may hold several units at once, which an allocation could not
/// express without charging them to all of them.
///
/// <b>Terms, in order, through the real operations.</b> Each unit carries the terms it has had,
/// earliest first, and the step applies them with <see cref="IEmployeeAssignmentService.SetHeadAsync"/>
/// — which closes the sitting term the day before the new one starts, exactly as a handover does
/// through the screen. A term with a stated end that nothing follows, or that a later term does not
/// immediately abut, is closed with <c>ClearHeadAsync</c>: that gap is a vacancy, and a vacancy is a
/// fact about the organisation rather than an absence of data.
///
/// <b>Re-running is safe, per ADR-0008.</b> A unit whose recorded history already matches the terms
/// stated is skipped with nothing written; one that differs fails the step naming the unit and the
/// difference. The comparison is against the whole history rather than the current head, because
/// two tenants can agree about who leads a unit today and disagree about everything before that.
/// </remarks>
internal sealed class UnitHeadsRecipeStep : IRecipeStepHandler
{
    private const string StepName = "unit-heads";

    private readonly IStructureService _structures;
    private readonly IDimensionService _records;
    private readonly IEmployeeAssignmentService _assignments;
    private readonly IEnumerable<IEmployeeLookup> _employees;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public UnitHeadsRecipeStep(
        IStructureService structures,
        IDimensionService records,
        IEmployeeAssignmentService assignments,
        IEnumerable<IEmployeeLookup> employees,
        ISystemOperation systemOperation,
        IStringLocalizer<UnitHeadsRecipeStep> stringLocalizer)
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

        var model = context.Step.ToObject<UnitHeadsStepModel>() ?? new UnitHeadsStepModel();

        if (model.Heads.Count == 0)
        {
            return;
        }

        using (_systemOperation.Begin("unit-heads recipe step"))
        {
            var lookup = _employees.FirstOrDefault();

            if (lookup is null)
            {
                RecipeStepFailures.Throw(context, [
                    S["Unit heads cannot be imported because no module providing employee records is enabled."].Value,
                ]);

                return;
            }

            var errors = new List<string>();
            var resolved = new List<ResolvedUnit>();

            foreach (var entry in model.Heads)
            {
                var structure = await _structures.GetByCodeAsync(entry.StructureCode);

                if (structure is null)
                {
                    errors.Add(S["A head is named for structure '{0}', which does not exist.", entry.StructureCode].Value);
                    continue;
                }

                var record = await _records.GetByCodeAsync(entry.RecordCode);

                if (record is null)
                {
                    errors.Add(S["A head is named for unit '{0}', which does not exist.", entry.RecordCode].Value);
                    continue;
                }

                var terms = new List<ResolvedTerm>();

                foreach (var term in entry.Terms.OrderBy(term => term.EffectiveFrom))
                {
                    var employee = await lookup.GetByCodeAsync(term.EmployeeCode);

                    if (employee is null)
                    {
                        errors.Add(S["Unit '{0}' names head '{1}', who does not exist.", entry.RecordCode, term.EmployeeCode].Value);
                        continue;
                    }

                    if (term.EffectiveTo is { } to && to < term.EffectiveFrom)
                    {
                        errors.Add(S["Unit '{0}' has a term for '{1}' that ends before it begins.", entry.RecordCode, term.EmployeeCode].Value);
                        continue;
                    }

                    terms.Add(new ResolvedTerm(
                        employee.EmployeeId,
                        term.EmployeeCode,
                        new EffectiveRange(term.EffectiveFrom, term.EffectiveTo)));
                }

                if (terms.Count != entry.Terms.Count)
                {
                    continue;
                }

                resolved.Add(new ResolvedUnit(entry, structure.StructureId, record.RecordId, terms));
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            var toApply = new List<ResolvedUnit>();

            foreach (var unit in resolved)
            {
                var history = await _assignments.GetHeadHistoryAsync(unit.StructureId, unit.RecordId);

                if (history.Count == 0)
                {
                    toApply.Add(unit);
                    continue;
                }

                var differences = Differences(history, unit.Terms);

                if (differences is not null)
                {
                    errors.Add(S["Unit '{0}' on structure '{1}' already has a different history of heads: {2}.",
                        unit.Entry.RecordCode, unit.Entry.StructureCode, differences].Value);
                }

                // Matches: nothing to write. A partial match is a difference, not a catch-up —
                // re-applying half a history would interleave terms with the ones already there.
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            foreach (var unit in toApply)
            {
                for (var index = 0; index < unit.Terms.Count; index++)
                {
                    var term = unit.Terms[index];

                    var appointed = await _assignments.SetHeadAsync(
                        unit.StructureId, unit.RecordId, term.EmployeeId, term.Range.From);

                    if (!appointed.Succeeded)
                    {
                        RecipeStepFailures.Throw(
                            context,
                            appointed.Errors.Count > 0
                                ? appointed.Errors.Select(error => error.Message.Value)
                                : [S["'{0}' could not be made head of '{1}'.", term.EmployeeCode, unit.Entry.RecordCode].Value]);

                        return;
                    }

                    // A stated end that the next term does not take over from the very next day
                    // leaves the post empty, and that emptiness is the thing being imported. The
                    // appointment of the next term, when there is one, closes this one by itself.
                    var next = index + 1 < unit.Terms.Count ? unit.Terms[index + 1] : null;

                    if (term.Range.To is { } lastDay && (next is null || next.Range.From > lastDay.AddDays(1)))
                    {
                        var cleared = await _assignments.ClearHeadAsync(unit.StructureId, unit.RecordId, lastDay);

                        if (!cleared.Succeeded)
                        {
                            RecipeStepFailures.Throw(
                                context,
                                cleared.Errors.Count > 0
                                    ? cleared.Errors.Select(error => error.Message.Value)
                                    : [S["'{0}' could not be left without a head.", unit.Entry.RecordCode].Value]);

                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// How a unit's recorded history differs from the one stated, or null when they agree.
    /// </summary>
    private static string? Differences(
        IReadOnlyList<HeadAppointment> history, IReadOnlyList<ResolvedTerm> terms)
    {
        if (history.Count != terms.Count)
        {
            return $"the tenant has {history.Count} term(s) and the recipe states {terms.Count}";
        }

        for (var index = 0; index < history.Count; index++)
        {
            if (!string.Equals(history[index].EmployeeId, terms[index].EmployeeId, StringComparison.Ordinal))
            {
                return $"the term from {Iso(history[index].Range.From)} is held by somebody else in the tenant";
            }

            if (history[index].Range != terms[index].Range)
            {
                return $"the term for '{terms[index].EmployeeCode}' runs {history[index].Range} in the tenant but {terms[index].Range} in the recipe";
            }
        }

        return null;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record ResolvedUnit(
        UnitHeadStepEntry Entry,
        string StructureId,
        string RecordId,
        List<ResolvedTerm> Terms);

    private sealed record ResolvedTerm(string EmployeeId, string EmployeeCode, EffectiveRange Range);
}

internal sealed class UnitHeadsStepModel
{
    public List<UnitHeadStepEntry> Heads { get; set; } = [];
}

/// <summary>One unit's heads over time, as a recipe states them.</summary>
/// <remarks>
/// A unit with no entry is a unit nobody has ever led, which is the ordinary case and says itself
/// by being absent. A unit whose last term has ended and has no successor is one that is vacant
/// now, which is a different statement and needs the <c>effectiveTo</c> to make it.
/// </remarks>
internal sealed class UnitHeadStepEntry
{
    public string StructureCode { get; set; } = string.Empty;

    public string RecordCode { get; set; } = string.Empty;

    public List<UnitHeadTermStepEntry> Terms { get; set; } = [];
}

/// <summary>One person leading one unit for one period.</summary>
internal sealed class UnitHeadTermStepEntry
{
    public string EmployeeCode { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>The last day of the term, inclusive. Absent means they still hold it.</summary>
    public DateOnly? EffectiveTo { get; set; }
}
