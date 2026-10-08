using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using WorkMate.Platform;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The employee screens, exercised through real HTTP against the base tenant.
/// </summary>
/// <remarks>
/// What these answer that a service test cannot: that the screens are routed where the menu and
/// every link say they are, that they render, that a POST through the page's own fields does what
/// the service would, and — the part worth the most — that each is refused to somebody who is
/// signed in and lacks the permission it needs. That last is a different question from what the
/// anonymous client answers, and it is the one a permission bug actually shows up as.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public EmployeeAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnAnonymousVisitorIsNotShownTheEmployeeList()
    {
        var response = await _fixture.Anonymous.GetAsync("/Admin/Employees/Index");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheAdministratorSeesTheList()
    {
        var html = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Employees/Index");

        html.Should().Contain("Employees");
    }

    /// <summary>
    /// Adding an employee through the screen produces the same record the service would.
    /// </summary>
    /// <remarks>
    /// Posted from the rendered page's own fields rather than from a dictionary this test invents,
    /// so a field the page carries and this test has never heard of still posts as the page meant
    /// it to. See <c>RenderedForm</c>.
    /// </remarks>
    [Fact]
    public async Task AddingAnEmployeeThroughTheScreenCreatesPospectiveEmployee()
    {
        var page = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Employees/Create");

        var fields = RenderedForm.FieldsOf(page)
            .With("Code", "http-emp-1")
            .With("NameEn", "Nadia Farooq")
            .With("NameAr", "نادية فاروق")
            .With("JoinDate", "2025-02-01");

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Employees/Create", new FormUrlEncodedContent(fields));

        response.IsSuccessStatusCode.Should().BeTrue();

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var employee = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("http-emp-1");

            employee.Should().NotBeNull();
            employee!.NameEn.Should().Be("Nadia Farooq");
            employee.JoinDate.Should().Be(new DateOnly(2025, 2, 1));

            employee.Status.Should().Be(
                EmploymentStatus.Prospective,
                "the screen creates through IEmployeeService, which has one answer about this");
        });
    }

    [Fact]
    public async Task TheEditorShowsTheCodeAndStatusWithoutOfferingToChangeThem()
    {
        var id = await GivenAnEmployeeAsync("http-emp-editor", "Kamran Aziz");

        var html = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, $"/Admin/Employees/Edit/{id}");

        html.Should().Contain("http-emp-editor");

        // The code is shown as text, not as an input. It is the natural key every recipe and
        // integration names this person by; an editable one would make every exported file wrong
        // the moment somebody tidied it.
        html.Should().NotContain("name=\"Code\"");

        // Likewise the status: changing it closes placements and headships and raises an event,
        // none of which a field edit can carry out.
        html.Should().NotContain("name=\"Status\"");
    }

    /// <summary>
    /// The exit screen is a dry run first, and names the units it would leave without a head.
    /// </summary>
    /// <remarks>
    /// The part of this screen that earns its place. Everything else it reports — which placements
    /// close — is recoverable by looking; a unit silently losing its approver is not.
    /// </remarks>
    [Fact]
    public async Task TheExitScreenNamesTheUnitsItWouldLeaveWithoutAHead()
    {
        string employeeId = null!;
        string unitCode = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "http-exit-org");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(
                services, types.Department, "http-exit-dep", new DateOnly(2024, 1, 1));

            unitCode = "http-exit-dep";
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "http-exit-head", nameEn: "Rania Haddad");

            (await assignments.PlaceAsync(employeeId, structure, department, new DateOnly(2024, 1, 1)))
                .Succeeded.Should().BeTrue();

            (await assignments.SetHeadAsync(structure, department, employeeId, new DateOnly(2024, 1, 1)))
                .Succeeded.Should().BeTrue();
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Employees/Lifecycle/Exit/{employeeId}");

        var fields = RenderedForm.FieldsOf(page).With("LastDay", "2026-06-30");

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Employees/Lifecycle/ExitPreview", new FormUrlEncodedContent(fields));

        var preview = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue();
        preview.Should().Contain("exit-headships", "the dry run must say which units lose their head");
        preview.Should().Contain(unitCode);
        preview.Should().Contain("exit-placements");

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            (await services.GetRequiredService<IEmployeeService>().GetAsync(employeeId))!
                .Status.Should().Be(EmploymentStatus.Active, "a dry run changes nothing");
        });
    }

    [Fact]
    public async Task ThePlacementScreenShowsEveryAxisAndWhatThePersonHeads()
    {
        string employeeId = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "http-place-org");
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(
                services, types.Department, "http-place-dep", new DateOnly(2024, 1, 1));

            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "http-place-emp");

            (await assignments.PlaceAsync(employeeId, structure, department, new DateOnly(2024, 1, 1)))
                .Succeeded.Should().BeTrue();

            (await assignments.SetHeadAsync(structure, department, employeeId, new DateOnly(2024, 1, 1)))
                .Succeeded.Should().BeTrue();
        });

        var html = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Employees/Placement/Index/{employeeId}");

        html.Should().Contain("http-place-dep");
        html.Should().Contain("headships", "the screen lists what this person leads as well as where they sit");
    }

    /// <summary>
    /// Every screen is refused to somebody signed in without the permission it needs.
    /// </summary>
    /// <remarks>
    /// The auditor holds <c>ViewEmployees</c> and nothing else in this module, which makes it
    /// exactly the right reader to test with: it can see the list, and must not be able to add,
    /// exit or place anybody. A test using an anonymous client would prove something much weaker.
    /// </remarks>
    [Fact]
    public async Task AnAuditorMayReadTheListAndChangeNothing()
    {
        var auditor = await _fixture.CreateSignedInClientAsync(
            "employee-auditor", "Workmate!Auditor1", PlatformRoles.Auditor, allowAutoRedirect: false);

        var list = await auditor.GetAsync("/Admin/Employees/Index");
        list.StatusCode.Should().Be(HttpStatusCode.OK, "the auditor holds ViewEmployees");

        foreach (var forbidden in new[]
        {
            "/Admin/Employees/Create",
            "/Admin/Employees/Lifecycle/Exit/anything",
            "/Admin/Employees/Placement/Place/anything",
        })
        {
            var response = await auditor.GetAsync(forbidden);

            response.StatusCode.Should().NotBe(
                HttpStatusCode.OK,
                $"{forbidden} needs a permission the auditor does not hold");
        }
    }

    private async Task<string> GivenAnEmployeeAsync(string code, string nameEn)
    {
        string id = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            id = await EmployeeScenario.ActiveEmployeeAsync(services, code, nameEn: nameEn);
        });

        return id;
    }
}
