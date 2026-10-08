using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The <c>employees</c> recipe step, on the cases the demo recipes do not reach.
/// </summary>
/// <remarks>
/// The demo recipes hire everybody and activate them, which is one status out of five and one
/// shape of row out of several. That left the step's own rules — the statuses it can walk to, what
/// it refuses, and ADR-0008's skip-or-fail — covered only by the happy path, and one of them was
/// wrong: the validation pass asked whether a prospective employee could move <em>directly</em> to
/// the stated status, while the walk reached on leave and suspended in two moves. Every row asking
/// for either was refused with a message saying it was impossible, by a step that could do it.
///
/// Both now read one table, so they cannot disagree. These tests are what says so.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeesRecipeStepTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public EmployeesRecipeStepTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Joined = new(2024, 1, 15);
    private static readonly DateOnly Changed = new(2024, 6, 1);

    /// <summary>
    /// Every status a row may ask for is reached, by the transitions that reach it.
    /// </summary>
    [Theory]
    [InlineData("Prospective", EmploymentStatus.Prospective)]
    [InlineData("Active", EmploymentStatus.Active)]
    [InlineData("OnLeave", EmploymentStatus.OnLeave)]
    [InlineData("Suspended", EmploymentStatus.Suspended)]
    [InlineData("Exited", EmploymentStatus.Exited)]
    public async Task AStatedStatusIsReached(string stated, EmploymentStatus expected)
    {
        var code = $"step-status-{stated.ToLowerInvariant()}";

        await ApplyAsync(Recipe(Employee(code, status: stated, statusFrom: Changed)));

        await InTenantAsync(async services =>
        {
            var employee = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync(code);

            employee.Should().NotBeNull();
            employee!.Status.Should().Be(expected);

            // Always the first day of the status, including for an exit — which is why the step
            // converts for ExitAsync rather than making every recipe know about the off-by-one.
            employee.StatusEffectiveFrom.Should().Be(
                expected == EmploymentStatus.Prospective ? Joined : Changed);
        });
    }

    /// <summary>
    /// A row identical to the tenant is skipped; one that differs fails the step naming what.
    /// </summary>
    [Fact]
    public async Task AnIdenticalRowIsSkippedAndADifferingOneFails()
    {
        var recipe = Recipe(Employee("step-rerun-1", nameEn: "Hira Malik", status: "Active", statusFrom: Changed));

        await ApplyAsync(recipe);
        await ApplyAsync(recipe);

        await InTenantAsync(async services =>
        {
            var employee = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("step-rerun-1");

            employee!.NameEn.Should().Be("Hira Malik");
            employee.Status.Should().Be(EmploymentStatus.Active, "the second run wrote nothing at all");
        });

        // The same code, a different name. ADR-0008: fail, naming the difference, rather than
        // overwriting — the tenant may be right and the file may be stale.
        var changed = Recipe(Employee("step-rerun-1", nameEn: "Hira Butt", status: "Active", statusFrom: Changed));

        var failure = await Record.ExceptionAsync(() => ApplyAsync(changed));

        failure.Should().NotBeNull("a row that disagrees with the tenant stops the step");

        await InTenantAsync(async services =>
        {
            var employee = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("step-rerun-1");

            employee!.NameEn.Should().Be("Hira Malik", "and nothing was written on the way to failing");
        });
    }

    /// <summary>
    /// A line manager listed after the person who reports to them still resolves.
    /// </summary>
    /// <remarks>
    /// Unlike the dimension records step, where a parent must precede its children, a file here may
    /// list people in any order: managers are attached in a second pass once everybody exists. A
    /// recipe is written by a person, and "sort your staff list so nobody precedes their manager"
    /// is a rule a person would get wrong on a file of any size.
    /// </remarks>
    [Fact]
    public async Task ALineManagerMayBeListedAfterTheirReport()
    {
        await ApplyAsync(Recipe(
            Employee("step-report-1", status: "Active", statusFrom: Joined, lineManagerCode: "step-manager-1"),
            Employee("step-manager-1", status: "Active", statusFrom: Joined)));

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            var report = await employees.GetByCodeAsync("step-report-1");
            var manager = await employees.GetByCodeAsync("step-manager-1");

            report!.LineManagerEmployeeId.Should().Be(manager!.EmployeeId);
        });
    }

    /// <summary>
    /// A manager nobody has and the file does not create fails the step before anything is written.
    /// </summary>
    [Fact]
    public async Task AMissingLineManagerFailsTheStepAndWritesNothing()
    {
        var failure = await Record.ExceptionAsync(() => ApplyAsync(Recipe(
            Employee("step-orphan-1", status: "Active", statusFrom: Joined, lineManagerCode: "step-nobody"))));

        failure.Should().NotBeNull();

        await InTenantAsync(async services =>
        {
            (await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("step-orphan-1"))
                .Should().BeNull("the whole step is validated before any of it is applied");
        });
    }

    /// <summary>
    /// Two rows sharing a code fail, rather than one of them quietly winning.
    /// </summary>
    [Fact]
    public async Task ACodeListedTwiceFailsTheStep()
    {
        var failure = await Record.ExceptionAsync(() => ApplyAsync(Recipe(
            Employee("step-dup-1", nameEn: "First"),
            Employee("step-dup-1", nameEn: "Second"))));

        failure.Should().NotBeNull();

        await InTenantAsync(async services =>
        {
            (await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("step-dup-1"))
                .Should().BeNull();
        });
    }

    // ---- harness ---------------------------------------------------------------------------

    private static JsonObject Employee(
        string code,
        string? nameEn = null,
        string? status = null,
        DateOnly? statusFrom = null,
        string? lineManagerCode = null)
    {
        var entry = new JsonObject
        {
            ["code"] = code,
            ["nameEn"] = nameEn ?? code,
            ["nameAr"] = $"{code}-ar",
            ["joinDate"] = Iso(Joined),
        };

        if (status is not null)
        {
            entry["status"] = status;
        }

        if (statusFrom is { } from)
        {
            entry["statusEffectiveFrom"] = Iso(from);
        }

        if (lineManagerCode is not null)
        {
            entry["lineManagerCode"] = lineManagerCode;
        }

        return entry;
    }

    private static JsonObject Recipe(params JsonObject[] employees) => new()
    {
        ["name"] = "employees-step-test",
        ["steps"] = new JsonArray(
        [
            new JsonObject
            {
                ["name"] = "employees",
                ["employees"] = new JsonArray([.. employees.Cast<JsonNode>()]),
            },
        ]),
    };

    private async Task ApplyAsync(JsonObject recipe)
    {
        await InTenantAsync(async services =>
        {
            var directory = Directory.CreateTempSubdirectory("workmate-employees-step-");

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory.FullName, "recipe.json"), recipe.ToJsonString());

                var fileProvider = new PhysicalFileProvider(directory.FullName);

                await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                    Guid.NewGuid().ToString("n"),
                    new RecipeDescriptor
                    {
                        Name = "employees-step-test",
                        BasePath = string.Empty,
                        FileProvider = fileProvider,
                        RecipeFileInfo = fileProvider.GetFileInfo("recipe.json"),
                        RequireNewScope = false,
                    },
                    new Dictionary<string, object>(),
                    CancellationToken.None);
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        });
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("employees recipe step test"))
            {
                await work(services);
            }
        });

    private static string Iso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
