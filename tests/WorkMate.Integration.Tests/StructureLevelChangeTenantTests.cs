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
/// Editing the levels of a structure that already has records placed on it: removing a level
/// shows the impact and requires confirmation before anything changes; reordering levels in a way
/// that would leave an existing placement invalid is refused outright, with nothing saved.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class StructureLevelChangeTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public StructureLevelChangeTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    [Fact]
    public async Task RemovingALevelWithPlacedRecordsShowsImpactAndRequiresConfirmation()
    {
        string divisionTypeId = string.Empty, departmentTypeId = string.Empty, structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await types.CreateAsync(
                "level-change-division", new BilingualText("Level Change Division", "أ"), [], allowsSelfNesting: false);
            division.Succeeded.Should().BeTrue();
            divisionTypeId = division.Value!.DimensionTypeId;

            var department = await types.CreateAsync(
                "level-change-department", new BilingualText("Level Change Department", "ب"), [], allowsSelfNesting: false);
            department.Succeeded.Should().BeTrue();
            departmentTypeId = department.Value!.DimensionTypeId;

            var structure = await structures.CreateAsync(
                "level-change-structure",
                new BilingualText("Level Change Structure", "ج"),
                [divisionTypeId, departmentTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);
            structure.Succeeded.Should().BeTrue();
            structureId = structure.Value!.StructureId;

            var opened = new DateOnly(2024, 1, 1);

            var divisionRecord = await records.CreateAsync(
                divisionTypeId, "level-change-div-1", new BilingualText("Division One", "واحد"), new EffectiveRange(opened, null));
            divisionRecord.Succeeded.Should().BeTrue();

            var departmentRecord = await records.CreateAsync(
                departmentTypeId, "level-change-dept-1", new BilingualText("Department One", "واحد"), new EffectiveRange(opened, null));
            departmentRecord.Succeeded.Should().BeTrue();

            var move = await graph.MoveAsync(
                structureId, departmentRecord.Value!.RecordId, divisionRecord.Value!.RecordId, opened);
            move.Succeeded.Should().BeTrue(string.Join("; ", move.Errors.Select(e => e.Message.Value)));
        });

        // Submitting an edit that drops the Department level must show the impact and must not
        // save anything yet.
        var editPage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, $"/Admin/Dimensions/Structures/Edit/{structureId}");

        var fields = RenderedForm.FieldsOf(editPage)
            .With("LevelDimensionTypeIds[0]", divisionTypeId);

        // Dropping LevelDimensionTypeIds[1] (Department) entirely — the rendered form has two
        // level rows; posting only the first removes the second from what is submitted.
        fields = [.. fields.Where(f => f.Key != "LevelDimensionTypeIds[1]")];

        var confirmResponse = await _fixture.Administrator.PostAsync(
            $"/Admin/Dimensions/Structures/Edit/{structureId}", new FormUrlEncodedContent(fields));

        confirmResponse.EnsureSuccessStatusCode();
        var confirmHtml = HtmlDecode(await confirmResponse.Content.ReadAsStringAsync());

        confirmHtml.Should().Contain("Confirm level change");
        confirmHtml.Should().Contain("Level Change Department");
        confirmHtml.Should().Contain(">1<", "exactly one department record is placed on this axis");

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();
            var unchanged = await structures.GetAsync(structureId);
            unchanged!.Levels.Should().HaveCount(2, "the preview must not have saved anything");
        });

        // Confirming applies exactly what was previewed.
        var confirmFields = RenderedForm.FieldsOf(confirmHtml);

        var applyResponse = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Structures/EditConfirmed", new FormUrlEncodedContent(confirmFields));

        applyResponse.EnsureSuccessStatusCode();

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();
            var updated = await structures.GetAsync(structureId);
            updated!.Levels.Should().ContainSingle().Which.DimensionTypeId.Should().Be(divisionTypeId);
        });
    }

    [Fact]
    public async Task ReorderingLevelsThatWouldInvalidateAPlacementIsRefused()
    {
        string divisionTypeId = string.Empty, departmentTypeId = string.Empty, sectionTypeId = string.Empty, structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await types.CreateAsync(
                "reorder-division", new BilingualText("Reorder Division", "أ"), [], allowsSelfNesting: false);
            division.Succeeded.Should().BeTrue();
            divisionTypeId = division.Value!.DimensionTypeId;

            var department = await types.CreateAsync(
                "reorder-department", new BilingualText("Reorder Department", "ب"), [], allowsSelfNesting: false);
            department.Succeeded.Should().BeTrue();
            departmentTypeId = department.Value!.DimensionTypeId;

            var section = await types.CreateAsync(
                "reorder-section", new BilingualText("Reorder Section", "ج"), [], allowsSelfNesting: false);
            section.Succeeded.Should().BeTrue();
            sectionTypeId = section.Value!.DimensionTypeId;

            var structure = await structures.CreateAsync(
                "reorder-structure",
                new BilingualText("Reorder Structure", "د"),
                [divisionTypeId, departmentTypeId, sectionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);
            structure.Succeeded.Should().BeTrue();
            structureId = structure.Value!.StructureId;

            var opened = new DateOnly(2024, 1, 1);

            var divisionRecord = await records.CreateAsync(
                divisionTypeId, "reorder-div-1", new BilingualText("Division One", "واحد"), new EffectiveRange(opened, null));
            var departmentRecord = await records.CreateAsync(
                departmentTypeId, "reorder-dept-1", new BilingualText("Department One", "واحد"), new EffectiveRange(opened, null));

            // Department is genuinely placed under Division — the fact a reordered axis must
            // either preserve or refuse, never silently accept as still valid.
            var move = await graph.MoveAsync(structureId, departmentRecord.Value!.RecordId, divisionRecord.Value!.RecordId, opened);
            move.Succeeded.Should().BeTrue(string.Join("; ", move.Errors.Select(e => e.Message.Value)));
        });

        var editPage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, $"/Admin/Dimensions/Structures/Edit/{structureId}");

        // Swap Division and Department — Division (currently the parent) would move below
        // Department (currently its child), which the engine must refuse.
        var fields = RenderedForm.FieldsOf(editPage)
            .With("LevelDimensionTypeIds[0]", departmentTypeId)
            .With("LevelDimensionTypeIds[1]", divisionTypeId)
            .With("LevelDimensionTypeIds[2]", sectionTypeId);

        var response = await _fixture.Administrator.PostAsync(
            $"/Admin/Dimensions/Structures/Edit/{structureId}", new FormUrlEncodedContent(fields));

        response.EnsureSuccessStatusCode();
        var html = HtmlDecode(await response.Content.ReadAsStringAsync());

        html.Should().NotContain("Confirm level change", "a violation must be refused outright, not offered for confirmation");
        html.Should().Contain("reorder-dept-1", "the error must name the specific record whose placement is at risk");
        html.Should().Contain("reorder-div-1");

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();
            var unchanged = await structures.GetAsync(structureId);

            unchanged!.Levels.OrderBy(level => level.Ordinal).Select(level => level.DimensionTypeId)
                .Should().Equal(
                    [divisionTypeId, departmentTypeId, sectionTypeId],
                    "a refused change must leave the structure exactly as it was");
        });
    }
}
