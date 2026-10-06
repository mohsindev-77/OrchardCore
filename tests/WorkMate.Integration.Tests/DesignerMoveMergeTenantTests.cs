using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Security;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Stage C's three operations against a real tenant: moving a unit, folding one into another, and
/// taking a move back.
/// </summary>
/// <remarks>
/// The writes themselves were built in prompt 2 and are covered by the closure, merge and
/// cancel-move suites. What is new here is the preview each one now shows before it commits, and
/// the dated behaviour the designer promises on the strength of it — so these assert the plan and
/// the result against each other, on the day before and the day of.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DesignerMoveMergeTenantTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly MoveDay = new(2026, 7, 1);

    private readonly BaseTenantFixture _fixture;

    public DesignerMoveMergeTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    // ---- move ---------------------------------------------------------------------------

    [Fact]
    public async Task TheMovePreviewNamesBothPathsAndWhatTravelsWithTheUnit()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "moveplan", withGrandchild: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var plan = await graph.PlanMoveAsync(
                world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay);

            plan.Succeeded.Should().BeTrue(Because(plan.Errors));

            plan.Value!.CurrentPath.Select(node => node.RecordId)
                .Should().Equal([world.FirstDivisionId], "it sits under the first division today");

            plan.Value.NewPath.Select(node => node.RecordId)
                .Should().Equal([world.SecondDivisionId], "and would sit under the second");

            plan.Value.DescendantsMoving.Should().Be(1, "the section under it travels with it");
            plan.Value.HasViolations.Should().BeFalse();
            plan.Value.SplitsHistory.Should().BeFalse("nothing is recorded after this date");

            // A plan writes nothing.
            (await graph.GetChildrenAsync(world.StructureId, world.FirstDivisionId, MoveDay))
                .Should().ContainSingle(node => node.RecordId == world.DepartmentId);
        });
    }

    [Fact]
    public async Task AMoveTakesEffectOnItsDateAndNotTheDayBefore()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "movedo", withGrandchild: true);
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await records.MoveAsync(world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay))
                .Succeeded.Should().BeTrue();

            (await graph.GetChildrenAsync(world.StructureId, world.FirstDivisionId, MoveDay.AddDays(-1)))
                .Should().ContainSingle(node => node.RecordId == world.DepartmentId,
                    "the day before, it is still where it was");

            (await graph.GetChildrenAsync(world.StructureId, world.SecondDivisionId, MoveDay))
                .Should().ContainSingle(node => node.RecordId == world.DepartmentId,
                    "and on the day, it is under the new parent");

            // The branch came too, and still resolves under the new parent.
            (await graph.IsUnderAsync(world.StructureId, world.SectionId!, world.SecondDivisionId, MoveDay))
                .Should().BeTrue();

            (await graph.IsUnderAsync(world.StructureId, world.SectionId!, world.FirstDivisionId, MoveDay.AddDays(-1)))
                .Should().BeTrue("and resolved under the old one before that");
        });
    }

    /// <summary>
    /// ADR-0005's addendum: a backdated move claims only up to the day before whatever was
    /// recorded after it, and the designer has to say so rather than let it look like a
    /// replacement.
    /// </summary>
    [Fact]
    public async Task ABackdatedMoveReportsThatItStopsWhereTheLaterOneBegins()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "backdated");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // A move already on record, later than the one about to be entered.
            (await records.MoveAsync(world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay))
                .Succeeded.Should().BeTrue();

            var backdated = MoveDay.AddMonths(-3);

            var plan = await graph.PlanMoveAsync(
                world.StructureId, world.DepartmentId, world.ThirdDivisionId, backdated);

            plan.Succeeded.Should().BeTrue(Because(plan.Errors));
            plan.Value!.SplitsHistory.Should().BeTrue("a later move already claims the period after this one");
            plan.Value.ClaimedUntil.Should().Be(MoveDay.AddDays(-1),
                "it runs to the day before the move already on record");
            plan.Value.SupersededByParentName.Should().Be("backdated Second Division",
                "and names the parent that takes over");

            // And that is exactly what applying it does: split, not overwrite.
            (await records.MoveAsync(world.StructureId, world.DepartmentId, world.ThirdDivisionId, backdated))
                .Succeeded.Should().BeTrue();

            (await graph.GetChildrenAsync(world.StructureId, world.ThirdDivisionId, backdated))
                .Should().ContainSingle(node => node.RecordId == world.DepartmentId);

            (await graph.GetChildrenAsync(world.StructureId, world.SecondDivisionId, MoveDay))
                .Should().ContainSingle(node => node.RecordId == world.DepartmentId,
                    "the later move still stands");
        });
    }

    [Fact]
    public async Task MovingAUnitOffTheTreeAndBackAgainClearsTheParentRetiredMark()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "reclaimed");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // Strand the department by closing its parent and leaving it.
            (await records.RetireAsync(
                world.FirstDivisionId,
                MoveDay,
                new Dictionary<string, ChildrenDisposition>(StringComparer.Ordinal)
                {
                    [world.StructureId] = ChildrenDisposition.Unplaced,
                })).Succeeded.Should().BeTrue();

            (await graph.GetUnplacedAsync(world.StructureId, MoveDay))
                .Should().Contain(node => node.RecordId == world.DepartmentId);

            (await graph.GetOrphanedByParentRetirementAsync(world.StructureId, [world.DepartmentId]))
                .Should().ContainKey(world.DepartmentId);

            // Moving it back into the tree is how a person rescues it, and the badge goes with it.
            (await records.MoveAsync(
                world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay.AddDays(1)))
                .Succeeded.Should().BeTrue();

            (await graph.GetUnplacedAsync(world.StructureId, MoveDay.AddDays(1)))
                .Should().NotContain(node => node.RecordId == world.DepartmentId);

            (await graph.GetOrphanedByParentRetirementAsync(world.StructureId, [world.DepartmentId]))
                .Should().BeEmpty("it has somewhere to be again, so the badge would be a lie");
        });
    }

    [Fact]
    public async Task TheMovePickerOffersOnlyUnitsTheStructureAllowsAndNeverTheUnitsOwnBranch()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "targets", withGrandchild: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var targets = (await graph.GetPlacementTargetsAsync(world.StructureId, world.DepartmentId, MoveDay))
                .Select(target => target.RecordId)
                .ToList();

            targets.Should().Contain(world.SecondDivisionId).And.Contain(world.ThirdDivisionId,
                "a department may sit under any division");

            targets.Should().NotContain(world.DepartmentId, "a unit cannot sit under itself");
            targets.Should().NotContain(world.SectionId!, "nor inside its own branch");
        });
    }

    // ---- merge --------------------------------------------------------------------------

    [Fact]
    public async Task AMergeMovesTheChildrenAcrossRetiresTheSourceAndLeavesHistoryAlone()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "mergeit", withGrandchild: true);
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // A second department to merge the first into.
            var survivor = await records.AddUnitAsync(
                world.StructureId, world.SecondDivisionId, world.DepartmentTypeId,
                "mergeit-dept-2", new BilingualText("Surviving Department", "باقية"), Start);
            survivor.Succeeded.Should().BeTrue(Because(survivor.Errors));

            var plan = await records.PlanMergeAsync(
                world.StructureId, world.DepartmentId, survivor.Value!.RecordId, MoveDay);

            plan.Succeeded.Should().BeTrue(Because(plan.Errors));
            plan.Value!.ChildrenReparented.Should().Contain(world.SectionId!);
            plan.Value.SourceRetired.Should().BeTrue();

            // A dry run writes nothing.
            (await records.GetAsync(world.DepartmentId))!.IsActive.Should().BeTrue();

            var merged = await records.MergeAsync(
                world.StructureId, world.DepartmentId, survivor.Value.RecordId, MoveDay);

            merged.Succeeded.Should().BeTrue(Because(merged.Errors));

            // Day before: everything as it was.
            (await graph.IsUnderAsync(world.StructureId, world.SectionId!, world.DepartmentId, MoveDay.AddDays(-1)))
                .Should().BeTrue("prior periods still resolve through the unit that was merged away");

            // Day of: the child sits under the survivor and the source has closed.
            (await graph.GetChildrenAsync(world.StructureId, survivor.Value.RecordId, MoveDay))
                .Should().Contain(node => node.RecordId == world.SectionId);

            (await records.GetAsync(world.DepartmentId, MoveDay)).Should().BeNull(
                "the source is retired by the merge");
        });
    }

    // ---- cancel move --------------------------------------------------------------------

    [Fact]
    public async Task CancellingAMoveRestoresWhatItDisplacedAndNeedsAReason()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "cancelit");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await records.MoveAsync(world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay))
                .Succeeded.Should().BeTrue();

            // The picker lists the move that was just made, newest first, naming its parent.
            var recorded = await graph.GetRecordedMovesAsync(world.StructureId, world.DepartmentId);

            recorded.Should().HaveCount(2, "the original placement and the move");
            recorded[0].EffectiveFrom.Should().Be(MoveDay);
            recorded[0].ParentNameEn.Should().Be("cancelit Second Division");

            var plan = await records.PlanCancelMoveAsync(world.StructureId, world.DepartmentId, MoveDay);

            plan.Succeeded.Should().BeTrue(Because(plan.Errors));
            plan.Value!.CancelledParentId.Should().Be(world.SecondDivisionId);
            plan.Value.RestoredParentId.Should().Be(world.FirstDivisionId);

            // A dry run writes nothing: the move is still in effect.
            (await graph.IsUnderAsync(world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay))
                .Should().BeTrue();

            var cancelled = await records.CancelMoveAsync(
                world.StructureId, world.DepartmentId, MoveDay, "entered against the wrong unit");

            cancelled.Succeeded.Should().BeTrue(Because(cancelled.Errors));

            (await graph.IsUnderAsync(world.StructureId, world.DepartmentId, world.FirstDivisionId, MoveDay))
                .Should().BeTrue("the placement the move displaced is restored");

            (await graph.IsUnderAsync(world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay))
                .Should().BeFalse("and the cancelled move is gone from the record");
        });
    }

    [Fact]
    public async Task CancellingWithoutAReasonIsRefusedByTheService() =>
        await InTenantAsSystemAsync(async services =>
        {
            var world = await GivenTwoDivisionsAsync(services, "noreason");
            var records = services.GetRequiredService<IDimensionService>();

            (await records.MoveAsync(world.StructureId, world.DepartmentId, world.SecondDivisionId, MoveDay))
                .Succeeded.Should().BeTrue();

            // Not a validation message but an argument contract: a cancellation with no reason is
            // not a thing this service will construct, let alone record.
            var blank = async () => await records.CancelMoveAsync(
                world.StructureId, world.DepartmentId, MoveDay, "   ");

            await blank.Should().ThrowAsync<ArgumentException>();
        });

    // ---- permissions --------------------------------------------------------------------

    /// <summary>
    /// Moving and merging each need their own permission, in the screen and on the endpoint.
    /// Hiding the menu item is not the control; refusing the request is.
    /// </summary>
    [Fact]
    public async Task AUserWhoMayEditButNotMoveIsRefusedTheMoveAndMergeScreens()
    {
        World world = null!;

        await InTenantAsSystemAsync(async services =>
        {
            world = await GivenTwoDivisionsAsync(services, "moveperm");
        });

        var editor = await CreateClientAsync("designer-no-move", "Designer Without Move",
            nameof(WorkMate.Dimensions.Permissions.ManageDimensionRecords));

        var designer = await BaseTenantFixture.GetPageAsync(
            editor, $"/Admin/Dimensions/Designer/Index?structureId={world.StructureId}");

        designer.Should().Contain("Designer/Rename", "this user may still rename");
        designer.Should().NotContain("Designer/Move", "but is offered no move");
        designer.Should().NotContain("Designer/Merge");

        foreach (var url in new[]
        {
            $"/Admin/Dimensions/Designer/Move?structureId={world.StructureId}&recordId={world.DepartmentId}",
            $"/Admin/Dimensions/Designer/Merge?structureId={world.StructureId}&recordId={world.DepartmentId}",
            $"/Admin/Dimensions/Designer/CancelMove?structureId={world.StructureId}&recordId={world.DepartmentId}",
        })
        {
            (await editor.GetAsync(url)).StatusCode.Should().NotBe(System.Net.HttpStatusCode.OK,
                "a hand-typed URL gets no further than the hidden menu item would have");
        }
    }

    // ---- scaffolding --------------------------------------------------------------------

    private sealed record World(
        string StructureId,
        string DepartmentTypeId,
        string FirstDivisionId,
        string SecondDivisionId,
        string ThirdDivisionId,
        string DepartmentId,
        string? SectionId);

    /// <summary>
    /// Three divisions, a department under the first, and optionally a section under that — enough
    /// for a move to have somewhere to go, a backdated move to have somewhere else, and a merge to
    /// have something to carry across.
    /// </summary>
    private static async Task<World> GivenTwoDivisionsAsync(
        IServiceProvider services,
        string prefix,
        bool withGrandchild = false)
    {
        var types = services.GetRequiredService<IDimensionTypeService>();
        var structures = services.GetRequiredService<IStructureService>();
        var records = services.GetRequiredService<IDimensionService>();

        async Task<string> TypeAsync(string suffix)
        {
            var created = await types.CreateAsync(
                $"{prefix}-{suffix}",
                new BilingualText($"{prefix} {suffix}", suffix),
                [],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(Because(created.Errors));

            return created.Value!.DimensionTypeId;
        }

        var divisionType = await TypeAsync("division");
        var departmentType = await TypeAsync("department");
        var sectionType = await TypeAsync("section");

        var structure = await structures.CreateAsync(
            $"{prefix}-structure",
            new BilingualText($"{prefix} Structure", "هيكل"),
            [divisionType, departmentType, sectionType],
            allowSkipLevel: false,
            isStrict: true,
            isPrimaryOrganisation: false);

        structure.Succeeded.Should().BeTrue(Because(structure.Errors));

        var structureId = structure.Value!.StructureId;

        async Task<string> UnitAsync(string? parentId, string typeId, string code, string name)
        {
            var created = await records.AddUnitAsync(
                structureId, parentId, typeId, $"{prefix}-{code}", new BilingualText(name, name), Start);

            created.Succeeded.Should().BeTrue(Because(created.Errors));

            return created.Value!.RecordId;
        }

        var first = await UnitAsync(null, divisionType, "div-1", $"{prefix} First Division");
        var second = await UnitAsync(null, divisionType, "div-2", $"{prefix} Second Division");
        var third = await UnitAsync(null, divisionType, "div-3", $"{prefix} Third Division");
        var department = await UnitAsync(first, departmentType, "dept-1", $"{prefix} Department");

        string? section = null;

        if (withGrandchild)
        {
            section = await UnitAsync(department, sectionType, "sec-1", $"{prefix} Section");
        }

        return new World(structureId, departmentType, first, second, third, department, section);
    }

    private static string Because(IReadOnlyList<DimensionError> errors) =>
        string.Join("; ", errors.Select(error => error.Message.Value));

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    private async Task<HttpClient> CreateClientAsync(string userName, string roleName, params string[] permissions)
    {
        await _fixture.InTenantAsync(async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<IRole>>();

            if (await roleManager.FindByNameAsync(roleName) is not null)
            {
                return;
            }

            await roleManager.CreateAsync(new Role
            {
                RoleName = roleName,
                RoleClaims =
                [
                    new RoleClaim
                    {
                        ClaimType = OrchardCore.Security.Permissions.Permission.ClaimType,
                        ClaimValue = OrchardCore.Admin.AdminPermissions.AccessAdminPanel.Name,
                    },
                    .. permissions.Select(name => new RoleClaim
                    {
                        ClaimType = OrchardCore.Security.Permissions.Permission.ClaimType,
                        ClaimValue = name,
                    }),
                ],
            });
        });

        return await _fixture.CreateSignedInClientAsync(
            userName, "Workmate!Integration1", roleName, allowAutoRedirect: false);
    }
}
