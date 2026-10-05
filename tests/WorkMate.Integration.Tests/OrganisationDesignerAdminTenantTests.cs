using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The organisation designer's read-only tree: roots, lazily loaded children, the unplaced panel
/// and search. Every assertion is against a structure this test places records on itself, through
/// the same services the mutation screens use, so the designer is proven against real placements
/// rather than against its own view model.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class OrganisationDesignerAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public OrganisationDesignerAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    [Fact]
    public async Task TreeShowsRootsAndChildrenAndSearchFindsADeeplyPlacedRecord()
    {
        string structureId = string.Empty;
        string divisionId = string.Empty, departmentId = string.Empty, sectionId = string.Empty, orphanDepartmentId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await types.CreateAsync(
                "designer-division", new BilingualText("Designer Division", "أ"), [], allowsSelfNesting: false);
            var department = await types.CreateAsync(
                "designer-department", new BilingualText("Designer Department", "ب"), [], allowsSelfNesting: false);
            var section = await types.CreateAsync(
                "designer-section", new BilingualText("Designer Section", "ج"), [], allowsSelfNesting: false);

            division.Succeeded.Should().BeTrue();
            department.Succeeded.Should().BeTrue();
            section.Succeeded.Should().BeTrue();

            var structure = await structures.CreateAsync(
                "designer-structure",
                new BilingualText("Designer Structure", "د"),
                [division.Value!.DimensionTypeId, department.Value!.DimensionTypeId, section.Value!.DimensionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);
            structure.Succeeded.Should().BeTrue();
            structureId = structure.Value!.StructureId;

            var opened = new DateOnly(2024, 1, 1);

            var divisionRecord = await records.CreateAsync(
                division.Value.DimensionTypeId, "designer-div-1", new BilingualText("Designer Division One", "واحد"), new EffectiveRange(opened, null));
            var departmentRecord = await records.CreateAsync(
                department.Value.DimensionTypeId, "designer-dept-1", new BilingualText("Designer Department One", "واحد"), new EffectiveRange(opened, null));
            var sectionRecord = await records.CreateAsync(
                section.Value.DimensionTypeId, "designer-section-1", new BilingualText("Designer Section One", "واحد"), new EffectiveRange(opened, null));
            var orphanDepartmentRecord = await records.CreateAsync(
                department.Value.DimensionTypeId, "designer-dept-orphan", new BilingualText("Designer Orphan Department", "يتيم"), new EffectiveRange(opened, null));

            divisionId = divisionRecord.Value!.RecordId;
            departmentId = departmentRecord.Value!.RecordId;
            sectionId = sectionRecord.Value!.RecordId;
            orphanDepartmentId = orphanDepartmentRecord.Value!.RecordId;

            (await graph.MoveAsync(structureId, departmentId, divisionId, opened)).Succeeded.Should().BeTrue();
            (await graph.MoveAsync(structureId, sectionId, departmentId, opened)).Succeeded.Should().BeTrue();

            // Deliberately never moved: this is what "unplaced" looks like in practice — a record
            // created outside the designer, with no parent on this axis.
        });

        var indexPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        indexPage.Should().Contain("Designer Division One", "the division is this axis's only root");
        indexPage.Should().NotContain("Designer Department One", "a non-root type is not rendered until its parent is expanded");
        indexPage.Should().Contain("Designer Orphan Department", "the unplaced panel lists it even though the tree does not");

        var divisionChildren = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={divisionId}");

        divisionChildren.Should().Contain("Designer Department One");
        divisionChildren.Should().NotContain("Designer Orphan Department", "the orphan has no parent, so it is not the division's child");
        divisionChildren.Should().NotContain("Designer Section One", "a grandchild is not returned by the immediate-children endpoint");

        var departmentChildren = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={departmentId}");

        departmentChildren.Should().Contain("Designer Section One");

        var orphanChildren = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={orphanDepartmentId}");

        orphanChildren.Trim().Should().Be("[]", "an unplaced record has no children of its own on this axis");

        var searchResult = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Search?structureId={structureId}&q=section");

        searchResult.Should().Contain("Designer Section One");
        searchResult.Should().Contain(
            $"\"ancestorRecordIds\":[\"{divisionId}\",\"{departmentId}\"]",
            "the search hit must carry the exact path the tree needs to expand down to it, root first");
    }

    [Fact]
    public async Task TheChartIsTheDefaultViewAndTheChosenViewIsRemembered()
    {
        var structureId = await GivenAStructureWithOneRootAsync("chartview", "Chart View Division One");

        var chart = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        chart.Should().Contain("designer-chart", "the chart is the default view");
        chart.Should().NotContain("designer-list");
        chart.Should().Contain("Chart View Division One", "a card carries the unit's English name");
        chart.Should().Contain("designer-card", "the chart draws each unit as a card");

        // Switching view is an ordinary link, so it works without script; the controller remembers
        // the choice in a cookie.
        var list = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}&view=List");

        list.Should().Contain("designer-list");
        list.Should().NotContain("designer-chart\"");

        var remembered = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        remembered.Should().Contain("designer-list", "the view chosen last time is remembered without asking for it again");

        // Put the shared client back on the default, so a later test in this collection is not
        // reading this one's leftover preference.
        await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}&view=Chart");
    }

    [Fact]
    public async Task TheTreeShowsTheOrganisationAsItWasOnTheChosenDate()
    {
        string structureId = string.Empty, divisionId = string.Empty, departmentId = string.Empty;

        var placedOn = new DateOnly(2025, 6, 15);

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await types.CreateAsync(
                "dated-division", new BilingualText("Dated Division", "أ"), [], allowsSelfNesting: false);
            var department = await types.CreateAsync(
                "dated-department", new BilingualText("Dated Department", "ب"), [], allowsSelfNesting: false);

            var structure = await structures.CreateAsync(
                "dated-structure",
                new BilingualText("Dated Structure", "ج"),
                [division.Value!.DimensionTypeId, department.Value!.DimensionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);
            structureId = structure.Value!.StructureId;

            var opened = new DateOnly(2024, 1, 1);

            var divisionRecord = await records.CreateAsync(
                division.Value.DimensionTypeId, "dated-div-1", new BilingualText("Dated Division One", "واحد"), new EffectiveRange(opened, null));
            var departmentRecord = await records.CreateAsync(
                department.Value.DimensionTypeId, "dated-dept-1", new BilingualText("Dated Department One", "واحد"), new EffectiveRange(opened, null));

            divisionId = divisionRecord.Value!.RecordId;
            departmentId = departmentRecord.Value!.RecordId;

            // The department exists from 2024 but only joins the division on 15 June 2025: the day
            // before, it is a record with no parent on this axis; the day it moves, it is a child.
            (await graph.MoveAsync(structureId, departmentId, divisionId, placedOn)).Succeeded.Should().BeTrue();
        });

        var dayBefore = placedOn.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dayOf = placedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var childrenBefore = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={divisionId}&asAt={dayBefore}");

        childrenBefore.Trim().Should().Be("[]", "the department had not joined the division the day before the move");

        var childrenOnTheDay = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={divisionId}&asAt={dayOf}");

        childrenOnTheDay.Should().Contain("Dated Department One", "the move is effective from this date");

        // The same date drives the unplaced panel: a record with no parent yet belongs there.
        var pageBefore = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}&asAt={dayBefore}");

        pageBefore.Should().Contain("Dated Department One", "unplaced on that date, so the panel lists it");
        pageBefore.Should().Contain(dayBefore, "the screen says which date it is showing");

        var pageOnTheDay = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}&asAt={dayOf}");

        pageOnTheDay.Should().NotContain("Dated Department One", "placed on that date, so it is in the tree rather than the unplaced panel");
    }

    [Fact]
    public async Task APastDateNeedsViewDimensionHistoryInTheScreenAndInTheEndpoints()
    {
        var structureId = await GivenAStructureWithOneRootAsync("histview", "Hist View Division One");

        var restricted = await CreateDesignerWithoutHistoryClientAsync();

        var page = await BaseTenantFixture.GetPageAsync(
            restricted, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        page.Should().Contain("Hist View Division One", "the designer itself is open to this user");
        page.Should().NotContain("Show the organisation as at", "the date control is not offered without the permission for it");

        // Not merely hidden: asking for the date anyway is refused, on the screen and on the
        // endpoints the two views call, so a hand-built URL gets nowhere either.
        var lastYear = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var url in new[]
        {
            $"/Admin/Dimensions/Designer/Index?structureId={structureId}&asAt={lastYear}",
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId=whatever&asAt={lastYear}",
            $"/Admin/Dimensions/Designer/Search?structureId={structureId}&q=a&asAt={lastYear}",
        })
        {
            var response = await restricted.GetAsync(url);
            response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.OK, $"{url} resolves a past date without ViewDimensionHistory");
        }

        // The administrator holds every permission, so the same date is fine for them.
        var allowed = await _fixture.Administrator.GetAsync(
            $"/Admin/Dimensions/Designer/Index?structureId={structureId}&asAt={lastYear}");

        allowed.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    /// <summary>
    /// A structure with a single root record, for a test that only needs something to render.
    /// </summary>
    private async Task<string> GivenAStructureWithOneRootAsync(string prefix, string rootName)
    {
        var structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var division = await types.CreateAsync(
                $"{prefix}-division", new BilingualText($"{rootName} Type", "أ"), [], allowsSelfNesting: false);
            division.Succeeded.Should().BeTrue(string.Join("; ", division.Errors.Select(e => e.Message.Value)));

            var structure = await structures.CreateAsync(
                $"{prefix}-structure",
                new BilingualText($"{rootName} Structure", "ب"),
                [division.Value!.DimensionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);
            structure.Succeeded.Should().BeTrue(string.Join("; ", structure.Errors.Select(e => e.Message.Value)));
            structureId = structure.Value!.StructureId;

            var record = await records.CreateAsync(
                division.Value.DimensionTypeId,
                $"{prefix}-div-1",
                new BilingualText(rootName, "واحد"),
                new EffectiveRange(new DateOnly(2024, 1, 1), null));
            record.Succeeded.Should().BeTrue(string.Join("; ", record.Errors.Select(e => e.Message.Value)));
        });

        return structureId;
    }

    /// <summary>
    /// A signed-in client holding <c>ManageDimensionRecords</c> and nothing else, which no stock
    /// platform role does: the HR administrator, the closest one, also holds
    /// <c>ViewDimensionHistory</c>, and that is the permission under test here.
    /// </summary>
    private static OrchardCore.Security.RoleClaim PermissionClaim(string permissionName) => new()
    {
        ClaimType = OrchardCore.Security.Permissions.Permission.ClaimType,
        ClaimValue = permissionName,
    };

    private async Task<System.Net.Http.HttpClient> CreateDesignerWithoutHistoryClientAsync()
    {
        const string roleName = "Designer Without History";

        await _fixture.InTenantAsync(async services =>
        {
            var roleManager = services.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<OrchardCore.Security.IRole>>();

            if (await roleManager.FindByNameAsync(roleName) is not null)
            {
                return;
            }

            await roleManager.CreateAsync(new OrchardCore.Security.Role
            {
                RoleName = roleName,
                RoleClaims =
                [
                    // Entering any admin screen at all needs this first; without it Orchard's own
                    // AdminFilter challenges before this module's permission is ever consulted,
                    // and the test would pass for the wrong reason.
                    PermissionClaim(OrchardCore.Admin.AdminPermissions.AccessAdminPanel.Name),
                    PermissionClaim(nameof(WorkMate.Dimensions.Permissions.ManageDimensionRecords)),
                ],
            });
        });

        return await _fixture.CreateSignedInClientAsync(
            "designer-without-history", "Workmate!Integration1", roleName, allowAutoRedirect: true);
    }
}
