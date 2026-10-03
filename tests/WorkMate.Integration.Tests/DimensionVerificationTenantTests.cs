using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Verification and rebuild: the two commands architecture section 4 says are both needed, "the
/// rebuild for recovery, the verification as a scheduled check and as a test assertion".
/// </summary>
/// <remarks>
/// Verification walks the links from scratch on each date and compares the answer to the index.
/// That is deliberately the slow, obviously-correct computation the index exists to avoid —
/// which is exactly what makes it worth checking the index against.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionVerificationTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionVerificationTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// A structure with enough history to be worth checking: three levels, two reorganisations
    /// and a future-dated one.
    /// </summary>
    private static async Task<(string Structure, DateOnly[] Changes)> ScriptedScenarioAsync(
        IServiceProvider services,
        string code)
    {
        var types = await DimensionGraphScenario.TypesAsync(services);
        var structure = await DimensionGraphScenario.StructureAsync(services, code);
        var graph = services.GetRequiredService<IDimensionGraphService>();

        var division = await DimensionGraphScenario.RecordAsync(services, types.Division, $"{code}-div", Opened);
        var other = await DimensionGraphScenario.RecordAsync(services, types.Division, $"{code}-oth", Opened);
        var department = await DimensionGraphScenario.RecordAsync(services, types.Department, $"{code}-dep", Opened);
        var section = await DimensionGraphScenario.RecordAsync(services, types.Section, $"{code}-sec", Opened);
        var deeper = await DimensionGraphScenario.RecordAsync(services, types.Section, $"{code}-sub", Opened);

        var joined = new DateOnly(2024, 6, 1);
        var reorganised = new DateOnly(2025, 7, 1);
        var future = new DateOnly(2030, 1, 1);

        (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();
        (await graph.MoveAsync(structure, section, department, joined)).Succeeded.Should().BeTrue();
        (await graph.MoveAsync(structure, deeper, section, joined)).Succeeded.Should().BeTrue();
        (await graph.MoveAsync(structure, department, other, reorganised)).Succeeded.Should().BeTrue();
        (await graph.MoveAsync(structure, section, other, future)).Succeeded.Should().BeTrue();

        return (structure, [Opened, joined, reorganised, future]);
    }

    [Fact]
    public async Task AScriptedScenarioVerifiesCleanAfterEveryMutation() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "ver-stepwise");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "vs-div", Opened);
            var other = await DimensionGraphScenario.RecordAsync(services, types.Division, "vs-oth", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "vs-dep", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "vs-sec", Opened);

            // Architecture section 8's index-integrity gate: "After every mutation in the test
            // suite, the closure index is verified against the links."
            await AssertConsistent("after the records were created");

            (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();
            await AssertConsistent("after placing the department");

            (await graph.MoveAsync(structure, section, department, new DateOnly(2024, 6, 1))).Succeeded.Should().BeTrue();
            await AssertConsistent("after placing the section");

            (await graph.MoveAsync(structure, department, other, new DateOnly(2025, 7, 1))).Succeeded.Should().BeTrue();
            await AssertConsistent("after reorganising the department");

            (await graph.MoveAsync(structure, section, null, new DateOnly(2026, 1, 1))).Succeeded.Should().BeTrue();
            await AssertConsistent("after detaching the section to a root");

            async Task AssertConsistent(string when)
            {
                var report = await graph.VerifyAsync(structure);

                report.IsConsistent.Should().BeTrue(
                    "the closure must agree with the links {0}. Divergences: {1}",
                    when,
                    string.Join("; ", report.Divergences.Select(Describe)));
            }
        });

    [Fact]
    public async Task VerificationChecksTheDayBeforeAndAfterEveryChange() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, changes) = await ScriptedScenarioAsync(services, "ver-dates");

            var report = await services.GetRequiredService<IDimensionGraphService>().VerifyAsync(structure);

            report.IsConsistent.Should().BeTrue(
                string.Join("; ", report.Divergences.Select(Describe)));

            // Boundaries are where dated logic goes wrong, so the default date set has to
            // include them rather than leaving the caller to think of it.
            foreach (var change in changes)
            {
                report.DatesChecked.Should().Contain(change, "the day a link changes");
                report.DatesChecked.Should().Contain(change.AddDays(-1), "the day before a change");
                report.DatesChecked.Should().Contain(change.AddDays(1), "the day after a change");
            }
        });

    [Fact]
    public async Task RebuildThenVerifyReportsNoDivergence() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, _) = await ScriptedScenarioAsync(services, "ver-rebuild");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var rebuilt = await graph.RebuildAsync(structure);

            rebuilt.Should().BeGreaterThan(0, "the structure has records to rebuild");

            var report = await graph.VerifyAsync(structure);

            report.IsConsistent.Should().BeTrue(
                "a rebuild reconstructs the index from the links, so verifying straight after "
                + "it must find nothing. Divergences: {0}",
                string.Join("; ", report.Divergences.Select(Describe)));
        });

    [Fact]
    public async Task RebuildIsIdempotent() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, _) = await ScriptedScenarioAsync(services, "ver-idempotent");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            await graph.RebuildAsync(structure);

            var afterFirst = await SnapshotAsync(graph, structure);

            await graph.RebuildAsync(structure);

            var afterSecond = await SnapshotAsync(graph, structure);

            afterSecond.Should().BeEquivalentTo(
                afterFirst,
                "a rebuild that changed the answer the second time would mean the first one was "
                + "wrong, or that rebuilding is not safe to run on a schedule");
        });

    [Fact]
    public async Task VerificationFindsADivergenceWhenTheIndexIsWrong() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // A verification that cannot fail proves nothing. There is deliberately no public
            // way to corrupt the closure, so this reaches past the service to write a pair the
            // links do not support — which is exactly the silent drift architecture section 4
            // calls the worst failure mode in the design.
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "ver-detects");
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var session = services.GetRequiredService<YesSql.ISession>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "vd-div", Opened);
            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "vd-dep", Opened);
            var unrelated = await DimensionGraphScenario.RecordAsync(services, types.Division, "vd-other", Opened);

            (await graph.MoveAsync(structure, department, division, Opened)).Succeeded.Should().BeTrue();
            (await graph.VerifyAsync(structure)).IsConsistent.Should().BeTrue("the starting point is clean");

            var corrupted = await YesSql.QueryExtensions
                .Query<WorkMate.Dimensions.Internal.Graph.DimensionClosureDocument,
                       WorkMate.Dimensions.Internal.Graph.DimensionClosureIndex>(
                    session,
                    index => index.StructureId == structure && index.DescendantId == department)
                .FirstOrDefaultAsync();

            corrupted.Should().NotBeNull();

            corrupted!.Ancestors =
            [
                .. corrupted.Ancestors,
                new WorkMate.Dimensions.Internal.Graph.ClosureAncestor(
                    unrelated, 1, new WorkMate.Core.EffectiveRange(Opened, null)),
            ];

            await session.SaveAsync(corrupted);
            await session.FlushAsync();

            var report = await graph.VerifyAsync(structure);

            report.IsConsistent.Should().BeFalse("the index now claims a parent the links never gave it");

            report.Divergences.Should().Contain(divergence =>
                divergence.Kind == ClosureDivergenceKind.InClosureButNotInLinks &&
                divergence.AncestorId == unrelated &&
                divergence.DescendantId == department);

            // And the rebuild is the stated recovery, so it has to actually recover.
            await graph.RebuildAsync(structure);

            (await graph.VerifyAsync(structure)).IsConsistent.Should().BeTrue(
                "a rebuild reconstructs the index from the links, which is what makes it the "
                + "answer to a divergence rather than just a way to notice one");
        });

    private static async Task<List<string>> SnapshotAsync(IDimensionGraphService graph, string structure)
    {
        var report = await graph.VerifyAsync(structure);
        var lines = new List<string>();

        foreach (var date in report.DatesChecked)
        {
            lines.Add($"{date:yyyy-MM-dd}: {report.Divergences.Count(d => d.AsAt == date)} divergences");
        }

        return lines;
    }

    private static string Describe(ClosureDivergence divergence) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{divergence.AsAt:yyyy-MM-dd} {divergence.Kind} {divergence.AncestorId}->{divergence.DescendantId} "
            + $"(closure {divergence.ClosureDepth}, links {divergence.LinkDepth})");
}
