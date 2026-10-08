using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Deployment;
using WorkMate.Core;
using WorkMate.Dimensions.Deployment;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Deployment;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The people half of ADR-0011's proof: seed a tenant with employees, placements and head
/// appointments, export it, import the export into a brand-new tenant, and assert the two answer
/// the same questions about the same people on the same dates.
/// </summary>
/// <remarks>
/// <b>Why this is a second round trip rather than more cases in the first.</b> The organisation and
/// the people in it are exported by two modules' deployment steps and imported by two modules'
/// recipe steps, and the join between them is a code resolved at import. That join is the thing
/// most likely to be wrong and the thing neither module's own tests can see: an assignment step
/// that resolved employees correctly against the tenant it was written on would pass every test in
/// the dimension engine and still fail the day the employees arrive in the same file.
///
/// <b>What the seeded tenant is built to contain.</b> Every shape an export of this data could
/// flatten, each one chosen because flattening it produces a destination that looks right:
/// <list type="bullet">
/// <item>a mid-month transfer, which is two changes where a naive export writes one placement;</item>
/// <item>a split allocation across two cost centres, which is one decision in several rows;</item>
/// <item>a matrix — somebody placed on two axes at once, home department and project team;</item>
/// <item>a head who is not a member of the unit they head, which ADR-0012 exists for;</item>
/// <item>one person heading several units, which an allocation-based model could not express;</item>
/// <item>a unit whose head left and was not replaced, which is vacancy rather than absence;</item>
/// <item>somebody who came off an axis entirely, which is not the same as transferring.</item>
/// </list>
///
/// Equality is asserted on the answers at dates <em>before and after</em> each change, never only on
/// today. A flattened history gives an identical answer today and a different one about March,
/// which is the entire reason any of this is dated.
/// </remarks>
public sealed class PeopleExportRoundTripTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// Both hosts are WebApplicationFactory instances, which are disposable. DisposeAsync below is
    /// what actually releases them; this exists because the analyser cannot see that an
    /// IAsyncLifetime does the job, and disposing twice is harmless.
    /// </summary>
    public void Dispose()
    {
        _source?.Dispose();
        _destination?.Dispose();
    }

    private BaseTenantFixture _source = default!;
    private BaseTenantFixture _destination = default!;

    // The dates the seeded history turns on. Each is asserted on from both sides.
    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly Hired = new(2024, 2, 1);
    private static readonly DateOnly BeforeTheTransfer = new(2025, 3, 14);
    private static readonly DateOnly Transferred = new(2025, 3, 15);
    private static readonly DateOnly Split = new(2025, 6, 1);
    private static readonly DateOnly HeadHandover = new(2025, 9, 1);
    private static readonly DateOnly HeadLeftTheUnit = new(2025, 11, 30);
    private static readonly DateOnly AfterTheVacancy = new(2025, 12, 1);
    private static readonly DateOnly ProjectEnded = new(2026, 2, 28);
    private static readonly DateOnly AfterTheProject = new(2026, 3, 1);

    /// <summary>The two axes the seeded tenant has, which is what makes the matrix a matrix.</summary>
    private static readonly string[] AxisCodes = ["rt-people-org", "rt-people-cost"];

    private static readonly string[] UnitCodes =
        ["rt-people-civil", "rt-people-mech", "rt-people-site", "rt-people-cc-a", "rt-people-cc-b"];

    public async Task InitializeAsync()
    {
        // Two hosts, each with its own content root, so the destination is a genuinely empty tenant
        // rather than the source with things added to it.
        _source = new BaseTenantFixture();
        _destination = new BaseTenantFixture();

        await _source.InitializeAsync();
        await _destination.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _destination.DisposeAsync();
    }

    [Fact]
    public async Task AnExportedTenantsPeopleImportIntoAFreshOneAndAnswerTheSameQuestions()
    {
        await SeedAsync(_source);

        // The destination is empty of everything this test is about to assert equality on. Without
        // this the test would still pass if both fixtures resolved to the same tenant, which is the
        // one way a round-trip test can be comprehensively green and prove nothing at all.
        (await DescribeAsync(_destination)).Employees.Should().BeEmpty(
            "the destination must be a genuinely fresh tenant, not the source under another name");

        var recipe = await ExportAsync(_source);

        // People first, then the organisation, then the two steps that join them. That order is the
        // operator's to choose on the plan — and it is the only order that applies, because an
        // assignment names its employee by a code the employees step has to have created.
        recipe["steps"]!.AsArray().Select(step => (string)step!["name"]!).Should().Equal(
            "employees",
            "dimension-types",
            "structures",
            "dimension-records",
            "employee-assignments",
            "unit-heads");

        // The export is a recipe, so importing it is running a recipe — the same path an operator
        // takes with the downloaded file, through the same importers every other recipe uses.
        await ApplyAsync(_destination, recipe);

        await AssertSameAsync();
    }

    /// <summary>
    /// Applying the same export a second time changes nothing and fails nothing — ADR-0008.
    /// </summary>
    /// <remarks>
    /// The re-run rule matters most for exactly this data. A recipe that re-applied placements
    /// would write a second identical transfer and a second head term, and nothing downstream would
    /// report an error — the chart would still draw, the headcount would still be one, and the
    /// history screen would show a handover from somebody to themselves.
    /// </remarks>
    [Fact]
    public async Task ApplyingTheSameExportTwiceChangesNothing()
    {
        await SeedAsync(_source);

        var recipe = await ExportAsync(_source);

        await ApplyAsync(_destination, recipe);

        var afterOnce = await DescribeAsync(_destination);

        await ApplyAsync(_destination, recipe);

        var afterTwice = await DescribeAsync(_destination);

        afterTwice.Should().BeEquivalentTo(afterOnce, "a second run of the same recipe is a no-op");
    }

    // ---- the seeded tenant ---------------------------------------------------------------

    private static async Task SeedAsync(BaseTenantFixture tenant) =>
        await InTenantAsync(tenant, async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var records = services.GetRequiredService<IDimensionService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var employees = services.GetRequiredService<IEmployeeService>();

            // Two axes, which is what makes a matrix a matrix: somebody's home department on one
            // and the project team they are lent to on the other.
            var organisation = await DimensionGraphScenario.StructureAsync(services, "rt-people-org");
            var cost = await DimensionGraphScenario.StructureAsync(services, "rt-people-cost");

            var civil = await DimensionGraphScenario.RecordAsync(services, types.Department, "rt-people-civil", Opened);
            var mechanical = await DimensionGraphScenario.RecordAsync(services, types.Department, "rt-people-mech", Opened);
            var siteTeam = await DimensionGraphScenario.RecordAsync(services, types.Department, "rt-people-site", Opened);
            var centreA = await DimensionGraphScenario.RecordAsync(services, types.Department, "rt-people-cc-a", Opened);
            var centreB = await DimensionGraphScenario.RecordAsync(services, types.Department, "rt-people-cc-b", Opened);

            // An engineer: hired into Civil, transferred to Mechanical mid-month, and lent to a
            // site team on the other axis for the whole time.
            var engineer = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-eng", joined: Hired, nameEn: "Hamza Siddiqui", nameAr: "حمزة صديقي");

            await PlaceAsync(assignments, engineer, organisation, civil, Hired);
            await PlaceAsync(assignments, engineer, organisation, mechanical, Transferred);
            await PlaceAsync(assignments, engineer, cost, siteTeam, Hired);

            // An accountant whose cost is split across two centres from one date — one decision,
            // two rows, which is the shape an export most easily turns into two decisions.
            var accountant = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-acct", joined: Hired, nameEn: "Mariam Bakhit");

            await PlaceAsync(assignments, accountant, organisation, civil, Hired);

            var split = await assignments.ReallocateAsync(
                accountant,
                cost,
                [
                    new AssignmentSplitEntry(centreA, 60m, IsPrimary: true),
                    new AssignmentSplitEntry(centreB, 40m, IsPrimary: false),
                ],
                Split);

            split.Succeeded.Should().BeTrue(Why(split.Errors));

            // A foreman hired for a project, who comes off the axis when it finishes rather than
            // transferring anywhere. Without endedOn the import would leave him on it for ever.
            var foreman = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-foreman", joined: Hired, nameEn: "Yousaf Khan");

            await PlaceAsync(assignments, foreman, cost, siteTeam, Hired);

            var ended = await assignments.EndAsync(foreman, cost, ProjectEnded);

            ended.Succeeded.Should().BeTrue(Why(ended.Errors));

            // A general manager who works in neither department and heads both, plus a cost centre
            // on the other axis. ADR-0012's case: no allocation anywhere, counted by nobody.
            var manager = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-gm", joined: Hired, nameEn: "Imran Qureshi");

            await AppointAsync(assignments, organisation, civil, manager, Opened);
            await AppointAsync(assignments, organisation, mechanical, manager, Opened);
            await AppointAsync(assignments, cost, centreA, manager, Opened);

            // A handover on the site team, so one unit has two terms and the earlier one is closed
            // by the later rather than by a clear.
            var firstLead = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-lead-1", joined: Hired, nameEn: "Adeel Mahmood");

            var secondLead = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-lead-2", joined: Hired, nameEn: "Sana Iqbal");

            await AppointAsync(assignments, cost, siteTeam, firstLead, Opened);
            await AppointAsync(assignments, cost, siteTeam, secondLead, HeadHandover);

            // And a post left empty: centre B had a head who stopped and nobody took over. A
            // vacancy is a fact about the organisation, so the export has to carry the ending.
            await AppointAsync(assignments, cost, centreB, firstLead, Opened);

            var cleared = await assignments.ClearHeadAsync(cost, centreB, HeadLeftTheUnit);

            cleared.Succeeded.Should().BeTrue(Why(cleared.Errors));

            // A leaver, because an export that quietly dropped them would make the destination
            // disagree with the source about last year.
            var leaver = await EmployeeScenario.ActiveEmployeeAsync(
                services, "rt-people-leaver", joined: Hired, nameEn: "Rashid Noor");

            var exit = await employees.ExitAsync(leaver, ProjectEnded);

            exit.Succeeded.Should().BeTrue(exit.Describe());

            // Nothing in this test depends on the records' own placements, but a unit has to be on
            // the tree for the organisation to be an organisation.
            foreach (var unit in new[] { civil, mechanical })
            {
                (await records.MoveAsync(organisation, unit, null, Opened)).Succeeded.Should().BeTrue();
            }

            foreach (var unit in new[] { siteTeam, centreA, centreB })
            {
                (await records.MoveAsync(cost, unit, null, Opened)).Succeeded.Should().BeTrue();
            }
        });

    private static async Task PlaceAsync(
        IEmployeeAssignmentService assignments,
        string employeeId,
        string structureId,
        string recordId,
        DateOnly from)
    {
        var placed = await assignments.PlaceAsync(employeeId, structureId, recordId, from);

        placed.Succeeded.Should().BeTrue(Why(placed.Errors));
    }

    private static async Task AppointAsync(
        IEmployeeAssignmentService assignments,
        string structureId,
        string recordId,
        string employeeId,
        DateOnly from)
    {
        var appointed = await assignments.SetHeadAsync(structureId, recordId, employeeId, from);

        appointed.Succeeded.Should().BeTrue(Why(appointed.Errors));
    }

    // ---- export, import -------------------------------------------------------------------

    /// <summary>
    /// Both deployment sources into one recipe, people before the organisation that places them.
    /// </summary>
    /// <remarks>
    /// Driven directly rather than through a deployment plan, like the dimension round trip: a plan
    /// would add a file builder and a download, neither of which is under test. The ordering is the
    /// part worth reproducing faithfully, because it is the part an operator gets wrong.
    /// </remarks>
    private static async Task<JsonObject> ExportAsync(BaseTenantFixture tenant)
    {
        JsonObject? recipe = null;

        await InTenantAsync(tenant, async services =>
        {
            var result = new DeploymentPlanResult(
                new MemoryFileBuilder(), new OrchardCore.Recipes.Models.RecipeDescriptor());

            var sources = services.GetServices<IDeploymentSource>().ToList();

            var records = sources.Single(candidate => candidate.GetType() == typeof(RecordsDeploymentSource));
            var dimensions = sources.Single(candidate => candidate.GetType() == typeof(DimensionsDeploymentSource));

            await records.ProcessDeploymentStepAsync(new RecordsDeploymentStep(), result);

            await dimensions.ProcessDeploymentStepAsync(
                new DimensionsDeploymentStep { IncludeAssignments = true, IncludeHeads = true }, result);

            await result.FinalizeAsync();

            recipe = result.Recipe;
        });

        recipe.Should().NotBeNull();

        return recipe!;
    }

    private static async Task ApplyAsync(BaseTenantFixture tenant, JsonObject recipe)
    {
        await InTenantAsync(tenant, async services =>
        {
            var directory = Directory.CreateTempSubdirectory("workmate-people-roundtrip-");

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory.FullName, "recipe.json"), recipe.ToJsonString());

                var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(directory.FullName);

                var descriptor = new OrchardCore.Recipes.Models.RecipeDescriptor
                {
                    Name = "people-round-trip",
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo("recipe.json"),
                    RequireNewScope = false,
                };

                await services.GetRequiredService<OrchardCore.Recipes.Services.IRecipeExecutor>()
                    .ExecuteAsync(
                        Guid.NewGuid().ToString("n"),
                        descriptor,
                        new Dictionary<string, object>(),
                        CancellationToken.None);
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        });
    }

    // ---- equality -------------------------------------------------------------------------

    private async Task AssertSameAsync()
    {
        var before = await DescribeAsync(_source);
        var after = await DescribeAsync(_destination);

        after.Employees.Should().BeEquivalentTo(before.Employees, "every person, their status and its date travel");
        after.Placements.Should().BeEquivalentTo(before.Placements, "every placement on every axis, on every date asked about");
        after.Heads.Should().BeEquivalentTo(before.Heads, "every head term, including the handover and the vacancy");

        // Named explicitly as well as compared, because "the two are equivalent" would pass if both
        // sides had lost the same thing — which is the kind of agreement between two wrongs a round
        // trip is otherwise very good at hiding. Each of these is one of the shapes in the remarks.
        after.Placements.Should().Contain(
            line => line.Contains("rt-people-eng", StringComparison.Ordinal) &&
                    line.Contains("2025-03-14", StringComparison.Ordinal) &&
                    line.Contains("rt-people-civil", StringComparison.Ordinal),
            "the day before the transfer the engineer is still in Civil, which a flattened export loses");

        after.Placements.Should().Contain(
            line => line.Contains("rt-people-acct", StringComparison.Ordinal) &&
                    line.Contains("60", StringComparison.Ordinal) &&
                    line.Contains("40", StringComparison.Ordinal),
            "the split survives as a split rather than as one placement or two full ones");

        after.Placements.Should().Contain(
            line => line.Contains("rt-people-foreman", StringComparison.Ordinal) &&
                    line.Contains("2026-03-01", StringComparison.Ordinal) &&
                    line.Contains("nowhere", StringComparison.Ordinal),
            "the foreman is off the axis after the project, not still on it");

        after.Heads.Should().Contain(
            line => line.Contains("rt-people-cc-b", StringComparison.Ordinal) &&
                    line.Contains("2025-12-01", StringComparison.Ordinal) &&
                    line.Contains("vacant", StringComparison.Ordinal),
            "a post whose holder stopped is vacant in the destination too");

        after.Employees.Should().Contain(
            line => line.Contains("rt-people-leaver", StringComparison.Ordinal) &&
                    line.Contains("Exited", StringComparison.Ordinal),
            "somebody who has left is still somebody this tenant knows about");
    }

    /// <summary>
    /// A tenant as a set of flat lines: what it says about each person, each placement on each
    /// date that matters, and each head term.
    /// </summary>
    /// <remarks>
    /// Strings rather than objects, so a failure names the difference in the message rather than
    /// requiring the reader to diff two object graphs — and so that the assertions above can make
    /// specific claims about specific lines.
    /// </remarks>
    private static async Task<TenantDescription> DescribeAsync(BaseTenantFixture tenant)
    {
        var employees = new List<string>();
        var placements = new List<string>();
        var heads = new List<string>();

        await InTenantAsync(tenant, async services =>
        {
            var service = services.GetRequiredService<IEmployeeService>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var page = await service.ListAsync(take: 200);

            var codeById = page.Items.ToDictionary(
                employee => employee.EmployeeId, employee => employee.Code, StringComparer.Ordinal);

            foreach (var employee in page.Items.OrderBy(employee => employee.Code, StringComparer.Ordinal))
            {
                employees.Add(
                    $"{employee.Code} | {employee.NameEn} | {employee.NameAr} | joined {Iso(employee.JoinDate)} " +
                    $"| {employee.Status} from {Iso(employee.StatusEffectiveFrom)}");
            }

            var axes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var code in AxisCodes)
            {
                var structure = await structures.GetByCodeAsync(code);

                if (structure is not null)
                {
                    axes[code] = structure.StructureId;
                }
            }

            var unitCodeById = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var code in UnitCodes)
            {
                var record = await records.GetByCodeAsync(code);

                if (record is not null)
                {
                    unitCodeById[record.RecordId] = code;
                }
            }

            // Every person, on every axis, on every date the seeded history turns on — not only
            // today. This is where a flattened history is caught.
            var dates = new[]
            {
                Hired, BeforeTheTransfer, Transferred, Split,
                HeadHandover, HeadLeftTheUnit, AfterTheVacancy, ProjectEnded, AfterTheProject,
            };

            foreach (var employee in page.Items.OrderBy(employee => employee.Code, StringComparer.Ordinal))
            {
                foreach (var (axisCode, structureId) in axes.OrderBy(axis => axis.Key, StringComparer.Ordinal))
                {
                    foreach (var date in dates)
                    {
                        var rows = await assignments.GetAllEffectiveAsync(employee.EmployeeId, structureId, date);

                        var where = rows.Count == 0
                            ? "nowhere"
                            : string.Join(
                                " + ",
                                rows.OrderByDescending(row => row.IsPrimary)
                                    .ThenBy(row => unitCodeById.GetValueOrDefault(row.RecordId, row.RecordId), StringComparer.Ordinal)
                                    .Select(row =>
                                        $"{unitCodeById.GetValueOrDefault(row.RecordId, row.RecordId)}" +
                                        $"@{row.AllocationPercent}{(row.IsPrimary ? " primary" : string.Empty)}"));

                        placements.Add($"{employee.Code} | {axisCode} | {Iso(date)} | {where}");
                    }
                }
            }

            foreach (var (axisCode, structureId) in axes.OrderBy(axis => axis.Key, StringComparer.Ordinal))
            {
                foreach (var (recordId, unitCode) in unitCodeById.OrderBy(unit => unit.Value, StringComparer.Ordinal))
                {
                    foreach (var date in dates)
                    {
                        var head = await assignments.GetHeadAsync(structureId, recordId, date);

                        // Only where somebody has ever led it: a unit on an axis it does not belong
                        // to would otherwise fill this with lines saying nothing.
                        if (head is null && (await assignments.GetHeadHistoryAsync(structureId, recordId)).Count == 0)
                        {
                            continue;
                        }

                        heads.Add(
                            $"{axisCode} | {unitCode} | {Iso(date)} | " +
                            (head is null ? "vacant" : codeById.GetValueOrDefault(head.EmployeeId, head.EmployeeId)));
                    }
                }
            }
        });

        return new TenantDescription(employees, placements, heads);
    }

    private sealed record TenantDescription(
        IReadOnlyList<string> Employees,
        IReadOnlyList<string> Placements,
        IReadOnlyList<string> Heads);

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Task InTenantAsync(BaseTenantFixture tenant, Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("people round trip test"))
            {
                await work(services);
            }
        });

    private static string Why(IReadOnlyList<DimensionError> errors) =>
        string.Join("; ", errors.Select(error => error.Message.Value));

    /// <summary>
    /// A file builder that keeps nothing: the recipe is read from
    /// <see cref="DeploymentPlanResult.Recipe"/>, not from a file on disk.
    /// </summary>
    private sealed class MemoryFileBuilder : IFileBuilder
    {
        public Task SetFileAsync(string subpath, Stream stream) => Task.CompletedTask;
    }
}
