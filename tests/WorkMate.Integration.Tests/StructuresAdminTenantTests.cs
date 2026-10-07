using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;
using static System.Net.WebUtility;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The structures admin screen, exercised through real HTTP against the base tenant: the list,
/// creating a structure end to end with ordered levels, and that a user without
/// <c>ManageStructures</c> is forbidden in the UI.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class StructuresAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public StructuresAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnAnonymousVisitorIsNotShownTheList()
    {
        var response = await _fixture.Anonymous.GetAsync("/Admin/Dimensions/Structures/Index");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheAdministratorSeesTheList()
    {
        var html = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Dimensions/Structures/Index");

        html.Should().Contain("Structures");
    }

    [Fact]
    public async Task CreatingAStructureShowsItsOrderedLevelsAndFlags()
    {
        var (divisionId, departmentId) = await GivenTwoDimensionTypesAsync();

        var createPage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Dimensions/Structures/Create");

        // Built from the rendered page's own fields — see RenderedForm's remarks. The two level
        // rows have no server-rendered equivalent on a fresh Create form (the level table starts
        // empty; a browser would add rows via the page's own script), so those two are added
        // rather than replaced; every other field, including any this test does not know about,
        // posts exactly as the page rendered it.
        var fields = RenderedForm.FieldsOf(createPage)
            .With("Code", "structuretest")
            .With("NameEn", "Structure test")
            .With("NameAr", "هيكل تجريبي")
            .With("IsStrict", "true")
            .With("LevelDimensionTypeIds[0]", divisionId)
            .With("LevelDimensionTypeIds[1]", departmentId);

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Structures/Create",
            new FormUrlEncodedContent(fields));

        response.EnsureSuccessStatusCode();

        var html = HtmlDecode(await response.Content.ReadAsStringAsync());

        html.Should().Contain("Structure test");
        html.Should().Contain("هيكل تجريبي");
        html.Should().Contain("dir=\"rtl\"");

        // The levels must render in the order they were submitted — Division before Department —
        // not alphabetically or by id.
        var divisionPosition = html.IndexOf("Structure Test Division", StringComparison.Ordinal);
        var departmentPosition = html.IndexOf("Structure Test Department", StringComparison.Ordinal);

        divisionPosition.Should().BeGreaterThan(-1);
        departmentPosition.Should().BeGreaterThan(-1);
        divisionPosition.Should().BeLessThan(departmentPosition, "the level chain must show the root-first order the form posted");
    }

    [Fact]
    public async Task AUserWithoutManageStructuresCannotSeeOrCreate()
    {
        var restricted = await _fixture.CreateSignedInClientAsync(
            "hr-admin-no-structures",
            "Workmate!Integration1",
            "HR Administrator",
            allowAutoRedirect: false);

        var index = await restricted.GetAsync("/Admin/Dimensions/Structures/Index");
        index.StatusCode.Should().NotBe(HttpStatusCode.OK);

        var create = await restricted.GetAsync("/Admin/Dimensions/Structures/Create");
        create.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The grid, round-tripped through the screen: untick one cell and the designer stops offering
    /// that kind of unit there.
    /// </summary>
    /// <remarks>
    /// The journey the whole of ADR-0010 exists for, and the one no service test can see. It goes
    /// through the editor's own rendered form — so the checkbox names, the model binding and the
    /// shape the controller builds are all exercised — and then asks a different screen, the
    /// designer's Add unit picker, whether it agrees. A map that saves correctly and a picker that
    /// ignores it would pass every test either side of this one.
    /// </remarks>
    [Fact]
    public async Task UntickingACellOfTheGridStopsTheDesignerOfferingThatKindOfUnit()
    {
        var (divisionId, sectionId, structureId, divisionRecordId) = await GivenASkippingStructureAsync();

        // Before: the structure was built from a chain that allowed skipping, so a Section is
        // offered directly under a Division.
        var before = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/AddUnit?structureId={structureId}&parentId={divisionRecordId}");

        before.Should().Contain("Grid Test Section", "the derived map allowed a Section under a Division");

        var editPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Structures/Edit/{structureId}");

        var fields = RenderedForm.FieldsOf(editPage).Without("ContainmentPairs", $"{divisionId}>{sectionId}");

        var response = await _fixture.Administrator.PostAsync(
            $"/Admin/Dimensions/Structures/Edit/{structureId}", new FormUrlEncodedContent(fields));

        response.EnsureSuccessStatusCode();

        var after = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Designer/AddUnit?structureId={structureId}&parentId={divisionRecordId}");

        after.Should().NotContain(
            "Grid Test Section",
            "unticking Division > Section must take it out of the picker, not only out of the validator");

        await _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                var saved = await services.GetRequiredService<IStructureService>().GetAsync(structureId);

                saved!.Permits(divisionId, sectionId).Should().BeFalse();
                saved.Permits(divisionId, saved.DimensionTypeIds[1]).Should().BeTrue(
                    "every other cell was posted exactly as rendered and must survive untouched");
            }
        });
    }

    /// <summary>
    /// A three-type structure built the old way — a chain with skipping switched on — plus one
    /// division record to hang the designer's Add unit screen off.
    /// </summary>
    private async Task<(string DivisionId, string SectionId, string StructureId, string DivisionRecordId)>
        GivenASkippingStructureAsync()
    {
        string divisionId = string.Empty, sectionId = string.Empty;
        string structureId = string.Empty, divisionRecordId = string.Empty;

        await _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                var types = services.GetRequiredService<IDimensionTypeService>();
                var structures = services.GetRequiredService<IStructureService>();
                var records = services.GetRequiredService<IDimensionService>();

                async Task<string> TypeAsync(string code, string nameEn) =>
                    (await types.CreateAsync(code, new BilingualText(nameEn, "نوع"), [], false)).Value!.DimensionTypeId;

                divisionId = await TypeAsync("gridtest-division", "Grid Test Division");
                var departmentId = await TypeAsync("gridtest-department", "Grid Test Department");
                sectionId = await TypeAsync("gridtest-section", "Grid Test Section");

                var structure = await structures.CreateAsync(
                    "gridtest-structure",
                    new BilingualText("Grid Test Structure", "هيكل"),
                    [divisionId, departmentId, sectionId],
                    allowSkipLevel: true,
                    isStrict: true,
                    isPrimaryOrganisation: false);

                structure.Succeeded.Should().BeTrue(string.Join("; ", structure.Errors.Select(e => e.Message.Value)));
                structureId = structure.Value!.StructureId;

                var division = await records.CreateAsync(
                    divisionId,
                    "gridtest-div-1",
                    new BilingualText("Grid Test Division One", "واحد"),
                    new EffectiveRange(new DateOnly(2024, 1, 1), null));

                division.Succeeded.Should().BeTrue(string.Join("; ", division.Errors.Select(e => e.Message.Value)));
                divisionRecordId = division.Value!.RecordId;
            }
        });

        return (divisionId, sectionId, structureId, divisionRecordId);
    }

    private async Task<(string DivisionId, string DepartmentId)> GivenTwoDimensionTypesAsync()
    {
        string divisionId = string.Empty;
        string departmentId = string.Empty;

        await _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                var types = services.GetRequiredService<IDimensionTypeService>();

                var division = await types.CreateAsync(
                    "structuretest-division",
                    new BilingualText("Structure Test Division", "قسم الاختبار"),
                    [],
                    allowsSelfNesting: false);
                division.Succeeded.Should().BeTrue(string.Join("; ", division.Errors.Select(e => e.Message.Value)));
                divisionId = division.Value!.DimensionTypeId;

                var department = await types.CreateAsync(
                    "structuretest-department",
                    new BilingualText("Structure Test Department", "إدارة الاختبار"),
                    [],
                    allowsSelfNesting: false);
                department.Succeeded.Should().BeTrue(string.Join("; ", department.Errors.Select(e => e.Message.Value)));
                departmentId = department.Value!.DimensionTypeId;
            }
        });

        return (divisionId, departmentId);
    }
}
