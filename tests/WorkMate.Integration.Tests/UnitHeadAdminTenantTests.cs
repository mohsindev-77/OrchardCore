using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using WorkMate.Platform;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Appointing and clearing a unit's head through the screen, and what the designer's cards say.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class UnitHeadAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public UnitHeadAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Fact]
    public async Task AppointingAHeadThroughTheScreenPutsThemInPost()
    {
        var unit = await GivenAUnitAndAnEmployeeAsync("head-http-appoint");
        var (structureId, recordId, employeeId) = (unit.StructureId, unit.RecordId, unit.EmployeeId);

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Heads/Set?structureId={structureId}&recordId={recordId}&asAt=2024-01-01");

        page.Should().Contain("unit-head-set");

        var fields = RenderedForm.FieldsOf(page)
            .With("EmployeeId", employeeId)
            .With("EffectiveFrom", "2024-01-01");

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Heads/Set", new FormUrlEncodedContent(fields));

        response.IsSuccessStatusCode.Should().BeTrue();

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var head = await services.GetRequiredService<IEmployeeAssignmentService>()
                .GetHeadAsync(structureId, recordId, Opened);

            head.Should().NotBeNull();
            head!.EmployeeId.Should().Be(employeeId);
        });
    }

    [Fact]
    public async Task ClearingAHeadLeavesThePostVacantFromTheDayAfter()
    {
        var unit = await GivenAUnitAndAnEmployeeAsync("head-http-clear");
        var (structureId, recordId, employeeId) = (unit.StructureId, unit.RecordId, unit.EmployeeId);

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            (await services.GetRequiredService<IEmployeeAssignmentService>()
                .SetHeadAsync(structureId, recordId, employeeId, Opened))
                .Succeeded.Should().BeTrue();
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Heads/Clear?structureId={structureId}&recordId={recordId}&asAt=2024-01-01");

        // Said before it happens, for the same reason the exit dry run says it.
        page.Should().Contain("approval that routes to this unit's head");

        var fields = RenderedForm.FieldsOf(page).With("LastDay", "2026-05-31");

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Heads/Clear", new FormUrlEncodedContent(fields));

        response.IsSuccessStatusCode.Should().BeTrue();

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            (await assignments.GetHeadAsync(structureId, recordId, new DateOnly(2026, 5, 31)))
                .Should().NotBeNull("the last day is included");

            (await assignments.GetHeadAsync(structureId, recordId, new DateOnly(2026, 6, 1)))
                .Should().BeNull();
        });
    }

    /// <summary>
    /// The screen shows every term on record, not only the one in force.
    /// </summary>
    /// <remarks>
    /// "Who led this unit last March" is the question dating an appointment exists to answer, and
    /// the person about to change it is the one most likely to want it answered.
    /// </remarks>
    [Fact]
    public async Task TheScreenListsEveryTermOnRecord()
    {
        var unit = await GivenAUnitAndAnEmployeeAsync("head-http-history");
        var (structureId, recordId, first) = (unit.StructureId, unit.RecordId, unit.EmployeeId);
        string second = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            second = await EmployeeScenario.ActiveEmployeeAsync(
                services, "head-http-history-2", nameEn: "Second Head");

            (await assignments.SetHeadAsync(structureId, recordId, first, Opened)).Succeeded.Should().BeTrue();

            (await assignments.SetHeadAsync(structureId, recordId, second, new DateOnly(2026, 4, 1)))
                .Succeeded.Should().BeTrue("a handover is not a clash — it displaces the sitting head");
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Heads/Set?structureId={structureId}&recordId={recordId}&asAt=2026-04-01");

        page.Should().Contain("unit-head-terms");
        page.Should().Contain("Second Head");
    }

    /// <summary>
    /// The designer's card says who heads a unit, and "Vacant" when nobody does.
    /// </summary>
    /// <remarks>
    /// The 5 October backlog note's requirement, and the reason the dash is gone: a dash meant "not
    /// built yet", and once a unit can have a head a dash would be a claim about the organisation
    /// that nothing had checked.
    /// </remarks>
    [Fact]
    public async Task TheDesignerCardNamesTheHeadAndSaysVacantWhenThereIsNone()
    {
        var unit = await GivenAUnitAndAnEmployeeAsync("head-http-card", headName: "Yusuf Al Khalifa");

        // Asserted on the division, which the chart renders on arrival. A department is one branch
        // down and is not in the page until somebody opens it.
        var vacant = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/Index?structureId={unit.StructureId}&asAt=2024-01-01");

        vacant.Should().Contain("Head: Vacant");
        vacant.Should().NotContain("Head: —", "the dash meant 'not built yet' and the feature is built");

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            (await services.GetRequiredService<IEmployeeAssignmentService>()
                .SetHeadAsync(unit.StructureId, unit.DivisionId, unit.EmployeeId, Opened))
                .Succeeded.Should().BeTrue();
        });

        var filled = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/Index?structureId={unit.StructureId}&asAt=2024-01-01");

        filled.Should().Contain("Head: Yusuf Al Khalifa");
    }

    /// <summary>
    /// The card counts the people at a unit, and a head who is not a member is not one of them.
    /// </summary>
    /// <remarks>
    /// ADR-0012's claim, asserted on what the screen is built from rather than only on the service:
    /// an appointment carries no allocation, so it is counted by no headcount.
    ///
    /// Against the <c>Children</c> endpoint, which is JSON — that is what the browser fetches when
    /// a branch is opened and what every card below the roots is rendered from, so it is the honest
    /// place to pin the numbers. The rendered markup is a browser test's job, and
    /// <c>DesignerHeadBrowserTests</c> does it there.
    /// </remarks>
    [Fact]
    public async Task TheDesignerCardCountsPlacementsAndNotHeadships()
    {
        var unit = await GivenAUnitAndAnEmployeeAsync("head-http-count");

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();
            var member = await EmployeeScenario.ActiveEmployeeAsync(services, "head-http-count-member");

            (await assignments.PlaceAsync(member, unit.StructureId, unit.RecordId, Opened))
                .Succeeded.Should().BeTrue();

            // Heads this unit, works somewhere else entirely — and is therefore not counted here.
            (await assignments.SetHeadAsync(unit.StructureId, unit.RecordId, unit.EmployeeId, Opened))
                .Succeeded.Should().BeTrue();
        });

        var json = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/Children?structureId={unit.StructureId}"
            + $"&recordId={unit.DivisionId}&asAt=2024-01-01");

        json.Should().Contain("\"employeeCount\":1", "the member is counted and the head is not");
        json.Should().Contain("\"headDisplayName\":\"head-http-count person\"");
    }

    [Fact]
    public async Task AppointingAHeadNeedsTheAssignEmployeesPermission()
    {
        var unit = await GivenAUnitAndAnEmployeeAsync("head-http-forbidden");
        var (structureId, recordId) = (unit.StructureId, unit.RecordId);

        // The auditor holds ViewDimensionHistory and may see the designer; it does not hold
        // AssignEmployees, so it may not appoint anybody.
        var auditor = await _fixture.CreateSignedInClientAsync(
            "head-auditor", "Workmate!Auditor2", PlatformRoles.Auditor, allowAutoRedirect: false);

        var response = await auditor.GetAsync(
            $"/Admin/Dimensions/Heads/Set?structureId={structureId}&recordId={recordId}");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A structure with a division, a department placed under it, and somebody to appoint.
    /// </summary>
    /// <remarks>
    /// The department is <em>placed</em>, not merely created. A record with no parent is drawn by
    /// the unplaced panel rather than by the chart, and the panel's cards do not carry a headcount —
    /// so a test that skipped the placement would be asserting against a different card from the one
    /// it means, and would have passed the head assertions for the wrong reason.
    /// </remarks>
    private async Task<SeededUnit> GivenAUnitAndAnEmployeeAsync(
        string prefix,
        string? headName = null)
    {
        string structureId = null!;
        string divisionId = null!;
        string recordId = null!;
        string employeeId = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);

            structureId = await DimensionGraphScenario.StructureAsync(services, prefix);

            divisionId = await DimensionGraphScenario.RecordAsync(
                services, types.Division, $"{prefix}-div", Opened);

            recordId = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{prefix}-dep", Opened);

            (await services.GetRequiredService<IDimensionGraphService>()
                .MoveAsync(structureId, recordId, divisionId, Opened))
                .Succeeded.Should().BeTrue();

            employeeId = await EmployeeScenario.ActiveEmployeeAsync(
                services, $"{prefix}-emp", nameEn: headName ?? $"{prefix} person");
        });

        return new SeededUnit(structureId, divisionId, recordId, employeeId);
    }

    /// <summary>
    /// A structure, the division the chart renders on arrival, the department under it, and somebody
    /// to appoint.
    /// </summary>
    /// <param name="DivisionId">
    /// A root of the axis, and therefore the only unit whose card is in the page before anything is
    /// expanded — which is what an assertion about rendered markup has to be written against.
    /// </param>
    private sealed record SeededUnit(string StructureId, string DivisionId, string RecordId, string EmployeeId);
}
