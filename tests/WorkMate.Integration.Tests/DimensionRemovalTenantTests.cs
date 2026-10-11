using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// <see cref="IDimensionGraphService.RemoveAsync"/>: taking a unit's links and closure rows off
/// every structure it is on.
/// </summary>
/// <remarks>
/// <b>Why a tenant with more than one structure.</b> This method crashed on exactly that shape and
/// only on it — YesSql's "The object to update was not found in identity map", raised not by the
/// deletes but by the <em>next</em> query after them, which is the second structure's. A tenant with
/// one structure never runs a query after a delete and so never saw it, and the test that first met
/// it looked like a flake: it passed alone and failed when another class had added a structure
/// first. Every test here builds its own second structure so the shape is the test's, not the run
/// order's.
///
/// <b>Why it has tests at all now.</b> It had one caller, <c>IDimensionService.AddUnitAsync</c>,
/// undoing a record whose placement had been refused after it was created. That caller is gone:
/// the placement is validated before anything is written, so there is nothing to undo. What is left
/// is a published method on the only sanctioned way into the link and closure tables, which the
/// first real "delete a unit" feature will call — so it is fixed and covered rather than left
/// broken behind a caller that no longer exists.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionRemovalTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DimensionRemovalTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// A unit on two structures is taken off both, and its children are left as roots.
    /// </summary>
    [Fact]
    public async Task RemovingAUnitTakesItOffEveryStructureAndFreesItsChildren()
    {
        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var first = await DimensionGraphScenario.StructureAsync(services, "rm-both-1");
            var second = await DimensionGraphScenario.StructureAsync(services, "rm-both-2");

            var division = await DimensionGraphScenario.RecordAsync(
                services, types.Division, "rm-both-div", Opened);

            var department = await DimensionGraphScenario.RecordAsync(
                services, types.Department, "rm-both-dept", Opened);

            var section = await DimensionGraphScenario.RecordAsync(
                services, types.Section, "rm-both-section", Opened);

            foreach (var structureId in new[] { first, second })
            {
                (await graph.PlaceAsync(structureId, division, null, Opened)).Succeeded.Should().BeTrue();
                (await graph.PlaceAsync(structureId, department, division, Opened)).Succeeded.Should().BeTrue();
                (await graph.PlaceAsync(structureId, section, department, Opened)).Succeeded.Should().BeTrue();
            }

            await graph.RemoveAsync(department);

            foreach (var structureId in new[] { first, second })
            {
                (await graph.GetAncestorsAsync(structureId, department, Opened))
                    .Should().BeEmpty("the removed unit is on no tree on either structure");

                (await graph.GetChildrenAsync(structureId, division, Opened))
                    .Select(child => child.RecordId)
                    .Should().NotContain(department, "its link to its parent is gone");

                var sectionAncestors = await graph.GetAncestorsAsync(structureId, section, Opened);

                sectionAncestors.Select(ancestor => ancestor.RecordId)
                    .Should().BeEmpty("the child was rewired to a root, not left pointing at a unit that is gone");
            }

            // The graph rows are half of a deletion; the record itself is the other half, and the
            // closure is only consistent once both are gone — a record whose type is on a
            // structure's levels is expected to have a self pair there, whether or not it is placed.
            var contentManager = services.GetRequiredService<IContentManager>();
            await contentManager.RemoveAsync((await contentManager.GetAsync(department))!);

            foreach (var structureId in new[] { first, second })
            {
                var report = await graph.VerifyAsync(structureId);

                report.Divergences.Should().BeEmpty(
                    await DimensionGraphScenario.DescribeAsync(services, report));
            }
        });
    }

    /// <summary>
    /// Removing a unit created moments earlier, in the same session, on a tenant with two
    /// structures.
    /// </summary>
    /// <remarks>
    /// The exact shape that crashed. Creating a record writes a closure self-pair per structure
    /// whose levels include its type, and those documents are still in the session's saved set when
    /// the removal starts — so the first query the removal runs flushes them, the deletes follow,
    /// and the second structure's query flushes again over an entity that is no longer in the
    /// identity map.
    /// </remarks>
    [Fact]
    public async Task RemovingAUnitCreatedInTheSameSessionDoesNotDisturbTheSession()
    {
        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var first = await DimensionGraphScenario.StructureAsync(services, "rm-fresh-1");
            var second = await DimensionGraphScenario.StructureAsync(services, "rm-fresh-2");

            var division = await DimensionGraphScenario.RecordAsync(
                services, types.Division, "rm-fresh-div", Opened);

            (await graph.PlaceAsync(first, division, null, Opened)).Succeeded.Should().BeTrue();

            // Created and removed without anything in between, the way the undone create used to.
            var fresh = await DimensionGraphScenario.RecordAsync(
                services, types.Department, "rm-fresh-dept", Opened);

            (await graph.PlaceAsync(first, fresh, division, Opened)).Succeeded.Should().BeTrue();

            await graph.RemoveAsync(fresh);

            (await graph.GetChildrenAsync(first, division, Opened))
                .Should().BeEmpty("the unit was taken back off the tree");

            // The session still works afterwards, which is the half the crash took out: it threw
            // from the next query, not from the removal, so what it broke was everything after it.
            (await records.GetByCodeAsync("rm-fresh-div")).Should().NotBeNull();
            (await graph.GetRootsAsync(second, Opened)).Should().NotBeNull();
        });
    }
}
