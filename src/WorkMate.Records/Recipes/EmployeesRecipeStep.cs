using System.Text.Json.Nodes;
using Microsoft.Extensions.Localization;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Platform.Recipes;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;

namespace WorkMate.Records.Recipes;

/// <summary>
/// The <c>employees</c> recipe step: people, by code, with the status they are actually in.
/// </summary>
/// <remarks>
/// <b>The status is reached, not written.</b> <see cref="IEmployeeService.CreateAsync"/> always
/// produces a prospective employee whatever the join date, and this step then walks each one to
/// the status the row states through the same dated transitions a person uses — so an imported
/// employee has the status history the transitions produce rather than a status field somebody
/// set. Importing an active employee is two operations because becoming active is an event; a step
/// that assigned the status directly would create records no sequence of real decisions could
/// have produced, and the lifecycle handlers leave and attendance subscribe to would never fire.
///
/// <b>Re-running is safe, per ADR-0008.</b> A row naming a code that already exists is compared
/// against the tenant's record — the fields the row actually states, and only those — and skipped
/// with nothing written if they all match; the step fails, naming the code and what differs, if
/// any does not. A row that says nothing about a field is not claiming the field is empty.
///
/// <b>Everything is validated before anything is written.</b> Codes, dates, enum spellings,
/// duplicate codes within the file and the status sequence are all checked across the whole step
/// first, so a file with a typo in its last row leaves the tenant exactly as it found it. The one
/// thing that cannot be checked up front is a line manager naming somebody created later in the
/// same step, so managers are attached in a second pass after every employee exists — which also
/// means a file may list people in any order, unlike the dimension records step where a parent
/// has to precede its children.
/// </remarks>
internal sealed class EmployeesRecipeStep : IRecipeStepHandler
{
    private const string StepName = "employees";

    private readonly IEmployeeService _employees;
    private readonly ISystemOperation _systemOperation;
    private readonly IStringLocalizer S;

    public EmployeesRecipeStep(
        IEmployeeService employees,
        ISystemOperation systemOperation,
        IStringLocalizer<EmployeesRecipeStep> stringLocalizer)
    {
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

        var model = context.Step.ToObject<EmployeesStepModel>() ?? new EmployeesStepModel();

        using (_systemOperation.Begin("employees recipe step"))
        {
            var errors = new List<string>();
            var toCreate = new List<EmployeeStepEntry>();
            var idByCode = new Dictionary<string, string>(EmployeeCodes.Comparer);
            var seen = new HashSet<string>(EmployeeCodes.Comparer);

            foreach (var employee in model.Employees)
            {
                if (!seen.Add(employee.Code))
                {
                    errors.Add(S["Employee '{0}' is listed more than once.", employee.Code].Value);
                    continue;
                }

                if (!EmployeeCodes.IsValid(employee.Code))
                {
                    errors.Add(S["'{0}' is not a valid employee code.", employee.Code].Value);
                    continue;
                }

                if (!TryReadStatus(employee, out var status, out var statusError))
                {
                    errors.Add(statusError!);
                    continue;
                }

                if (!TryReadGender(employee, out _, out var genderError))
                {
                    errors.Add(genderError!);
                    continue;
                }

                // A status with no route from prospective is refused here rather than part way
                // through the walk, where it would leave the employee in whatever state the last
                // successful transition reached. Asked of the same table the walk follows, so the
                // two cannot disagree about what is reachable — they did, and the disagreement
                // refused every row asking for on leave or suspended while the walk handled both.
                if (RouteFromNew(status) is null)
                {
                    errors.Add(S["Employee '{0}' asks for status '{1}', which a new employee cannot be put into.", employee.Code, status.ToString()].Value);
                    continue;
                }

                var existing = await _employees.GetByCodeAsync(employee.Code);

                if (existing is not null)
                {
                    var differences = DifferencesFrom(existing, employee, status);

                    if (differences.Count > 0)
                    {
                        errors.Add(S["Employee '{0}' already exists with different content: {1}.", employee.Code, string.Join("; ", differences)].Value);
                        continue;
                    }

                    idByCode[employee.Code] = existing.EmployeeId;
                    continue;
                }

                toCreate.Add(employee);
            }

            // Line managers, now that every code in the file is known: one named by a row that is
            // neither in the tenant nor in this file is a reference that will never resolve.
            foreach (var employee in model.Employees.Where(entry => entry.LineManagerCode is { Length: > 0 }))
            {
                if (seen.Contains(employee.LineManagerCode!) || idByCode.ContainsKey(employee.LineManagerCode!))
                {
                    continue;
                }

                if (await _employees.GetByCodeAsync(employee.LineManagerCode!) is null)
                {
                    errors.Add(S["Employee '{0}' names line manager '{1}', who does not exist.", employee.Code, employee.LineManagerCode!].Value);
                }
            }

            if (errors.Count > 0)
            {
                RecipeStepFailures.Throw(context, errors);
                return;
            }

            foreach (var employee in toCreate)
            {
                TryReadGender(employee, out var gender, out _);

                var created = await _employees.CreateAsync(
                    employee.Code,
                    new BilingualText(employee.NameEn, employee.NameAr),
                    employee.JoinDate,
                    new EmployeeDetails(
                        employee.DateOfBirth,
                        employee.NationalityCode,
                        gender,
                        // Attached in the second pass: the manager may be created later in this
                        // same step, so there is nothing to point at yet.
                        LineManagerEmployeeId: null,
                        employee.PhotoPath));

                if (!created.Succeeded)
                {
                    RecipeStepFailures.Throw(context, Failures(created, S["'{0}' could not be created.", employee.Code].Value));
                    return;
                }

                idByCode[employee.Code] = created.Value!.EmployeeId;

                TryReadStatus(employee, out var status, out _);

                var moved = await WalkToAsync(
                    created.Value.EmployeeId,
                    status,
                    employee.StatusEffectiveFrom ?? employee.JoinDate,
                    S["'{0}' could not be put into status '{1}'.", employee.Code, status.ToString()].Value);

                if (moved.Count > 0)
                {
                    RecipeStepFailures.Throw(context, moved);
                    return;
                }
            }

            foreach (var employee in toCreate.Where(entry => entry.LineManagerCode is { Length: > 0 }))
            {
                var current = await _employees.GetAsync(idByCode[employee.Code]);

                if (current is null)
                {
                    continue;
                }

                var updated = await _employees.UpdateAsync(
                    current.EmployeeId,
                    current.Name,
                    new EmployeeDetails(
                        current.DateOfBirth,
                        current.NationalityCode,
                        current.Gender,
                        idByCode.TryGetValue(employee.LineManagerCode!, out var managerId)
                            ? managerId
                            : (await _employees.GetByCodeAsync(employee.LineManagerCode!))?.EmployeeId,
                        current.PhotoPath));

                var failures = Failures(updated, S["'{0}' could not be given a line manager.", employee.Code].Value);

                if (failures.Count > 0)
                {
                    RecipeStepFailures.Throw(context, failures);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The transitions that get a brand-new employee to <paramref name="status"/>, or null when
    /// nothing does.
    /// </summary>
    /// <remarks>
    /// One table, read by the validation pass and by the walk. On leave and suspended are two moves
    /// each because both describe an employment that is already running: nobody goes from "offered
    /// the job" to "away on long leave" in one step, and <c>PutOnLeaveAsync</c> would refuse it.
    /// Prospective is the empty route rather than no route — they are already there.
    /// </remarks>
    private static IReadOnlyList<EmploymentStatus>? RouteFromNew(EmploymentStatus status) =>
        status switch
        {
            EmploymentStatus.Prospective => [],
            EmploymentStatus.Active => [EmploymentStatus.Active],
            EmploymentStatus.OnLeave => [EmploymentStatus.Active, EmploymentStatus.OnLeave],
            EmploymentStatus.Suspended => [EmploymentStatus.Active, EmploymentStatus.Suspended],
            EmploymentStatus.Exited => [EmploymentStatus.Exited],
            _ => null,
        };

    /// <summary>
    /// Moves a newly created employee to the status the row states, through the real transitions.
    /// </summary>
    /// <remarks>
    /// Exiting is the one that does more than change a status — it closes placements and headships
    /// — so it is reached through <see cref="IEmployeeService.ExitAsync"/> like any other exit. The
    /// date a row states for an exit is the first day of ex-employment, matching
    /// <c>EmployeePart.StatusEffectiveFrom</c> everywhere else, so the last day of service is the
    /// day before it.
    /// </remarks>
    private async Task<IReadOnlyList<string>> WalkToAsync(
        string employeeId, EmploymentStatus status, DateOnly effectiveFrom, string fallback)
    {
        foreach (var step in RouteFromNew(status) ?? [])
        {
            var errors = step switch
            {
                EmploymentStatus.Active => Failures(await _employees.ActivateAsync(employeeId, effectiveFrom), fallback),
                EmploymentStatus.OnLeave => Failures(await _employees.PutOnLeaveAsync(employeeId, effectiveFrom), fallback),
                EmploymentStatus.Suspended => Failures(await _employees.SuspendAsync(employeeId, effectiveFrom), fallback),
                // The date a row states is the first day of ex-employment; ExitAsync takes the
                // last day worked, which is the day before it.
                _ => Failures(await _employees.ExitAsync(employeeId, effectiveFrom.AddDays(-1)), fallback),
            };

            if (errors.Count > 0)
            {
                return errors;
            }
        }

        return [];
    }

    private static IReadOnlyList<string> Failures<T>(RecordResult<T> result, string fallback) =>
        result.Succeeded
            ? []
            : result.Errors.Count > 0
                ? [.. result.Errors.Select(error => error.Message.Value)]
                : [fallback];

    private static bool TryReadStatus(EmployeeStepEntry entry, out EmploymentStatus status, out string? error)
    {
        error = null;
        status = EmploymentStatus.Prospective;

        if (entry.Status is not { Length: > 0 })
        {
            return true;
        }

        if (Enum.TryParse(entry.Status, ignoreCase: true, out status))
        {
            return true;
        }

        error = $"Employee '{entry.Code}' states status '{entry.Status}', which is not one of {string.Join(", ", Enum.GetNames<EmploymentStatus>())}.";

        return false;
    }

    private static bool TryReadGender(EmployeeStepEntry entry, out Gender gender, out string? error)
    {
        error = null;
        gender = Gender.Unspecified;

        if (entry.Gender is not { Length: > 0 })
        {
            return true;
        }

        if (Enum.TryParse(entry.Gender, ignoreCase: true, out gender))
        {
            return true;
        }

        error = $"Employee '{entry.Code}' states gender '{entry.Gender}', which is not one of {string.Join(", ", Enum.GetNames<Gender>())}.";

        return false;
    }

    /// <summary>
    /// How the tenant's employee differs from the one this row states, for ADR-0008's skip rule.
    /// </summary>
    /// <remarks>
    /// Scoped to what the row states, like every other step's comparison: a row carrying no
    /// nationality is not claiming the employee has none. The status is the exception worth
    /// naming — a row stating a status the employee has moved on from is a real disagreement,
    /// because re-applying it would mean walking somebody backwards through a lifecycle.
    /// </remarks>
    private static List<string> DifferencesFrom(EmployeeRecord existing, EmployeeStepEntry entry, EmploymentStatus status)
    {
        var differences = new List<string>();

        if (!RecipeNameComparison.Same(existing.NameEn, entry.NameEn))
        {
            differences.Add(RecipeNameComparison.Describe("English name", existing.NameEn, entry.NameEn));
        }

        if (!RecipeNameComparison.Same(existing.NameAr, entry.NameAr))
        {
            differences.Add(RecipeNameComparison.Describe("Arabic name", existing.NameAr, entry.NameAr));
        }

        if (existing.JoinDate != entry.JoinDate)
        {
            differences.Add($"the join date is {existing.JoinDate:yyyy-MM-dd} in the tenant but {entry.JoinDate:yyyy-MM-dd} in the recipe");
        }

        if (entry.Status is { Length: > 0 } && existing.Status != status)
        {
            differences.Add($"the status is {existing.Status} in the tenant but {status} in the recipe");
        }

        if (entry.DateOfBirth is { } born && existing.DateOfBirth != born)
        {
            differences.Add($"the date of birth is {existing.DateOfBirth:yyyy-MM-dd} in the tenant but {born:yyyy-MM-dd} in the recipe");
        }

        if (entry.NationalityCode is { Length: > 0 } &&
            !RecipeNameComparison.Same(existing.NationalityCode, entry.NationalityCode))
        {
            differences.Add(RecipeNameComparison.Describe("nationality", existing.NationalityCode, entry.NationalityCode));
        }

        return differences;
    }
}

internal sealed class EmployeesStepModel
{
    public List<EmployeeStepEntry> Employees { get; set; } = [];
}

/// <summary>One employee, as a recipe states them.</summary>
internal sealed class EmployeeStepEntry
{
    /// <summary>The natural key, and the only way any other step refers to this person.</summary>
    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public DateOnly JoinDate { get; set; }

    /// <summary>
    /// The status to walk them to, by name. Absent means prospective, which is where everybody
    /// starts.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The date that status took effect. Absent means the join date.
    /// </summary>
    /// <remarks>
    /// Always the first day of the status, including for an exit — the day ex-employment began,
    /// not the last day worked, matching <c>EmployeePart.StatusEffectiveFrom</c>. The step
    /// converts for <c>ExitAsync</c>, which takes the last day, so that the one off-by-one in this
    /// model lives in one place.
    /// </remarks>
    public DateOnly? StatusEffectiveFrom { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? NationalityCode { get; set; }

    /// <summary>By name — <c>Male</c>, <c>Female</c>, <c>Unspecified</c>. Absent means unspecified.</summary>
    public string? Gender { get; set; }

    /// <summary>
    /// The manager's employee code, which may name somebody listed later in the same file.
    /// </summary>
    public string? LineManagerCode { get; set; }

    public string? PhotoPath { get; set; }
}
