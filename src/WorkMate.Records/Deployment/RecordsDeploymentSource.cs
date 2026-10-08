using System.Globalization;
using System.Text.Json.Nodes;
using OrchardCore.Deployment;
using WorkMate.Records.Models;
using WorkMate.Records.Services;

namespace WorkMate.Records.Deployment;

/// <summary>
/// Turns this tenant's employees into the <c>employees</c> recipe step the importer already reads.
/// </summary>
/// <remarks>
/// The export writes nothing the importer does not already understand, for the reason ADR-0011
/// gives: a format of its own would be a second description of the same thing, and the moment the
/// two disagree the export is the one nobody notices is wrong, because it is only read on the day
/// somebody is relying on it.
///
/// <b>The status travels as a status, not as a history.</b> An employee's past statuses are in the
/// audit trail rather than in a dated table, so what the record holds — and therefore all this can
/// carry — is where somebody is now and the date they reached it. The importer walks a new employee
/// to that status through the real transitions, so the imported record is one the lifecycle could
/// have produced; what it cannot reproduce is a route somebody took to get there. Said plainly here
/// because the dimension engine's export <em>does</em> carry full histories, and the difference is
/// in the two models rather than in the two exports.
///
/// <b>Leavers are exported.</b> Somebody who left in March is part of what this tenant is: payroll
/// and gratuity still answer questions about them, and an import that quietly dropped them would
/// produce a tenant that disagreed with the original about last year.
/// </remarks>
internal sealed class RecordsDeploymentSource : DeploymentSourceBase<RecordsDeploymentStep>
{
    /// <summary>
    /// How many employees are read at a time.
    /// </summary>
    /// <remarks>
    /// <see cref="IEmployeeService.ListAsync"/> is paged and has no unpaged twin, deliberately: a
    /// tenant can hold tens of thousands of people and nothing on a screen's path should be able to
    /// ask for all of them. An export does need all of them, so it pages through — which is the
    /// right shape anyway, since it never holds more than a page of content in flight.
    /// </remarks>
    private const int PageSize = 200;

    private readonly IEmployeeService _employees;

    public RecordsDeploymentSource(IEmployeeService employees) => _employees = employees;

    protected override async Task ProcessAsync(RecordsDeploymentStep step, DeploymentPlanResult result)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(result);

        if (!step.IncludeEmployees)
        {
            return;
        }

        var all = new List<EmployeeRecord>();
        var skip = 0;

        while (true)
        {
            var page = await _employees.ListAsync(skip: skip, take: PageSize);

            all.AddRange(page.Items);

            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            skip += page.Items.Count;
        }

        // By code, so two exports of the same tenant produce the same file and a diff between them
        // is a difference in the data rather than in the iteration.
        var byId = all.ToDictionary(employee => employee.EmployeeId, StringComparer.Ordinal);
        var ordered = all.OrderBy(employee => employee.Code, StringComparer.Ordinal).ToList();

        result.Steps.Add(new JsonObject
        {
            ["name"] = "employees",
            ["employees"] = new JsonArray([.. ordered.Select(employee => Entry(employee, byId))]),
        });
    }

    private static JsonObject Entry(EmployeeRecord employee, Dictionary<string, EmployeeRecord> byId)
    {
        var entry = new JsonObject
        {
            ["code"] = employee.Code,
            ["nameEn"] = employee.NameEn,
            ["joinDate"] = Iso(employee.JoinDate),
            // Stated even when prospective. An import defaults an unstated status to prospective,
            // which is right for a hand-written recipe and wrong here: an export reproduces a
            // tenant, and "they have not started yet" is a fact it is making rather than omitting.
            ["status"] = employee.Status.ToString(),
            ["statusEffectiveFrom"] = Iso(employee.StatusEffectiveFrom),
        };

        // Arabic is optional everywhere since the ADR-0003 addendum, so an absent half is absent
        // from the file rather than present and empty — the two say different things, and the
        // importer's comparison treats a stated empty name as a claim.
        if (!string.IsNullOrWhiteSpace(employee.NameAr))
        {
            entry["nameAr"] = employee.NameAr;
        }

        if (employee.DateOfBirth is { } born)
        {
            entry["dateOfBirth"] = Iso(born);
        }

        if (!string.IsNullOrWhiteSpace(employee.NationalityCode))
        {
            entry["nationalityCode"] = employee.NationalityCode;
        }

        if (employee.Gender != Gender.Unspecified)
        {
            entry["gender"] = employee.Gender.ToString();
        }

        // By code, like every other reference in every step. A manager who is somehow not in this
        // export is left out rather than carried as an id: an id means nothing in the tenant the
        // file lands in, and a dangling one would fail the import with a message about a person
        // rather than about an export that should not have written it.
        if (!string.IsNullOrEmpty(employee.LineManagerEmployeeId) &&
            byId.TryGetValue(employee.LineManagerEmployeeId, out var manager))
        {
            entry["lineManagerCode"] = manager.Code;
        }

        if (!string.IsNullOrWhiteSpace(employee.PhotoPath))
        {
            entry["photoPath"] = employee.PhotoPath;
        }

        return entry;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
