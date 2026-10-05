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
