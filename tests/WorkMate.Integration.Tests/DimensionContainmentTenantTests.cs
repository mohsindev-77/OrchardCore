using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// ADR-0010: containment as an explicit per-structure map, and the two customer shapes a chain of
/// level ordinals could not express.
/// </summary>
/// <remarks>
/// Both shapes are ordinary and both were impossible. Zenith wants a Division to contain either a
/// Department or a Project; Crescent wants one Division to contain Departments while its sibling
/// contains Regions, and Departments again under a Branch three levels further down. On a chain
/// the only way to allow either was to switch on level skipping, which allowed everything below
/// anything above — so the tests that matter most here are the refusals, not the acceptances.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionContainmentTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionContainmentTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    // ---- Zenith: two types at one level -------------------------------------------------

    [Fact]
    public async Task ADivisionCanContainEitherADepartmentOrAProjectAndATeamOnlyAProject() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await ZenithAsync(services, "zen");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await RecordAsync(services, world.Division, "zen-div", Opened);
            var department = await RecordAsync(services, world.Department, "zen-dept", Opened);
            var project = await RecordAsync(services, world.Project, "zen-proj", Opened);
            var team = await RecordAsync(services, world.Team, "zen-team", Opened);

            (await graph.MoveAsync(world.StructureId, department, division, Opened))
                .Succeeded.Should().BeTrue("a Division may contain a Department");

            (await graph.MoveAsync(world.StructureId, project, division, Opened))
                .Succeeded.Should().BeTrue("a Division may also contain a Project, which is the whole point");

            (await graph.MoveAsync(world.StructureId, team, project, Opened))
                .Succeeded.Should().BeTrue("a Project contains Teams");

            // The one a chain with skipping switched on could never refuse.
            var refused = await graph.MoveAsync(world.StructureId, team, division, Opened);

            refused.Succeeded.Should().BeFalse("a Team belongs to a Project, never straight to a Division");
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });

    // ---- Crescent: ragged, and one type at two depths ------------------------------------

    [Fact]
    public async Task OneDivisionCanHoldDepartmentsWhileItsSiblingHoldsRegionsAndBranches() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await CrescentAsync(services, "cre");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var headOffice = await RecordAsync(services, world.Division, "cre-head", Opened);
            var branchNetwork = await RecordAsync(services, world.Division, "cre-network", Opened);
            var creditRisk = await RecordAsync(services, world.Department, "cre-credit-risk", Opened);
            var punjab = await RecordAsync(services, world.Region, "cre-punjab", Opened);
            var lahore = await RecordAsync(services, world.Branch, "cre-lahore", Opened);
            var branchCredit = await RecordAsync(services, world.Department, "cre-lahore-credit", Opened);

            (await graph.MoveAsync(world.StructureId, creditRisk, headOffice, Opened))
                .Succeeded.Should().BeTrue("Head Office holds departments directly");

            (await graph.MoveAsync(world.StructureId, punjab, branchNetwork, Opened))
                .Succeeded.Should().BeTrue("its sibling holds regions instead");

            (await graph.MoveAsync(world.StructureId, lahore, punjab, Opened))
                .Succeeded.Should().BeTrue();

            (await graph.MoveAsync(world.StructureId, branchCredit, lahore, Opened))
                .Succeeded.Should().BeTrue("a Department is reachable again under a Branch, four levels down");

            // The ragged tree: two depths for the same type, on one axis, with no skipping.
            (await graph.GetAncestorsAsync(world.StructureId, creditRisk, Opened)).Count
                .Should().Be(1, "Head Office > Credit Risk");

            (await graph.GetAncestorsAsync(world.StructureId, branchCredit, Opened)).Count
                .Should().Be(3, "Branch Network > Punjab > Lahore > Credit");
        });

    [Fact]
    public async Task ABranchIsRefusedDirectlyUnderTheDivisionThatHoldsDepartments() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await CrescentAsync(services, "crefuse");
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var headOffice = await RecordAsync(services, world.Division, "crefuse-head", Opened);
            var branch = await RecordAsync(services, world.Branch, "crefuse-branch", Opened);

            var refused = await graph.MoveAsync(world.StructureId, branch, headOffice, Opened);

            refused.Succeeded.Should().BeFalse(
                "a Branch belongs to a Region; allowing it here is exactly what turning on skipping used to do");
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });

    // ---- self-nesting is now per axis -----------------------------------------------------

    [Fact]
    public async Task ATypeCanNestInsideItselfOnOneStructureAndNotOnAnother() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structures = services.GetRequiredService<IStructureService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // Which axis allows it is a property of the axis, and of nothing else: the dimension
            // type is not consulted at all since ADR-0010's addendum.
            var nesting = await StructureAsync(
                services, "nest-yes", [types.Division, types.Section],
                roots: [types.Division],
                containment: [(types.Division, types.Section), (types.Section, types.Section)]);

            var flat = await StructureAsync(
                services, "nest-no", [types.Division, types.Section],
                roots: [types.Division],
                containment: [(types.Division, types.Section)]);

            var outer = await RecordAsync(services, types.Section, "nest-outer", Opened);
            var inner = await RecordAsync(services, types.Section, "nest-inner", Opened);

            (await graph.MoveAsync(nesting, inner, outer, Opened))
                .Succeeded.Should().BeTrue("this axis declares Section inside Section");

            var refused = await graph.MoveAsync(flat, inner, outer, Opened);

            refused.Succeeded.Should().BeFalse("the other one does not declare the pairing");
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);

            structures.Should().NotBeNull();
        });

    /// <summary>
    /// A type that has never declared self-nesting nests inside itself wherever a structure's grid
    /// says so — and only there.
    /// </summary>
    /// <remarks>
    /// This is the assertion ADR-0010's addendum reversed. The dimension type used to hold a veto
    /// no axis could grant past, which meant the one screen that asks "what may sit under what"
    /// could not answer it: the diagonal was greyed out and the reason was a checkbox on another
    /// screen entirely. The scenario's Department carries <c>AllowsSelfNesting = false</c> to this
    /// day, which is exactly the point — nothing reads it.
    /// </remarks>
    [Fact]
    public async Task ATypeThatNeverDeclaredSelfNestingStillNestsWhereTheStructureSaysSo() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await services.GetRequiredService<IDimensionTypeService>().GetAsync(types.Department))!
                .AllowsSelfNesting.Should().BeFalse("the stored flag is still off, and still ignored");

            var granted = await StructureAsync(
                services, "subdept-yes", [types.Division, types.Department],
                roots: [types.Division],
                containment: [(types.Division, types.Department), (types.Department, types.Department)]);

            var withheld = await StructureAsync(
                services, "subdept-no", [types.Division, types.Department],
                roots: [types.Division],
                containment: [(types.Division, types.Department)]);

            var outer = await RecordAsync(services, types.Department, "subdept-outer", Opened);
            var inner = await RecordAsync(services, types.Department, "subdept-inner", Opened);

            (await graph.MoveAsync(granted, inner, outer, Opened))
                .Succeeded.Should().BeTrue("this structure's grid ticks Department → Department");

            // And the grant is this structure's alone. Two axes, one pair of records, opposite
            // answers — which is the whole argument of ADR-0010 applied to the diagonal.
            var refused = await graph.MoveAsync(withheld, inner, outer, Opened);

            refused.Succeeded.Should().BeFalse("the other structure does not tick that cell");
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });

    /// <summary>Every cell of the grid, the diagonal included, is offered to be ticked.</summary>
    /// <remarks>
    /// The screen's half of the same decision. A cell the editor will not let anyone tick is a
    /// containment map that cannot express what the engine now permits, and the greyed-out
    /// diagonal was the bug report: people could not find the setting that explained it.
    /// </remarks>
    [Fact]
    public async Task EveryDiagonalCellOfTheGridCanBeTicked() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            foreach (var typeId in new[] { types.Division, types.Department, types.Section })
            {
                var structureId = await StructureAsync(
                    services, $"diag-{typeId[..6]}", [typeId],
                    roots: [typeId],
                    containment: [(typeId, typeId)]);

                var outer = await RecordAsync(services, typeId, $"diag-{typeId[..6]}-outer", Opened);
                var inner = await RecordAsync(services, typeId, $"diag-{typeId[..6]}-inner", Opened);

                (await graph.MoveAsync(structureId, inner, outer, Opened))
                    .Succeeded.Should().BeTrue("the structure ticks this type inside itself");
            }
        });

    // ---- the picker and the validator agree ----------------------------------------------

    /// <summary>
    /// For every ordered pair of a structure's types, what the picker offers is exactly what the
    /// validator will not refuse — on a strict axis and on one that is not.
    /// </summary>
    /// <remarks>
    /// The property that made <c>ContainmentRules</c> a single function rather than two readings
    /// of one rule. Before ADR-0010 these two disagreed on every non-strict axis: the validator
    /// accepted an undeclared placement and the picker never offered one, so the only structures
    /// where the flag did anything were the ones where the screen pretended it did not.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThePickerOffersExactlyWhatTheValidatorWillAccept(bool isStrict) =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await ZenithAsync(services, $"agree{isStrict}", isStrict);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // Two records of every type, not one. The diagonal has to be attempted with a parent
            // and a child that are genuinely different records, or the cycle rule refuses it for a
            // reason that has nothing to do with containment and the property reads as broken.
            // That was masked while the type's veto also refused it — two wrongs agreeing.
            var parents = new Dictionary<string, string>(StringComparer.Ordinal);
            var children = new Dictionary<string, string>(StringComparer.Ordinal);
            var allTypes = new[] { world.Division, world.Department, world.Project, world.Team };

            foreach (var typeId in allTypes)
            {
                parents[typeId] = await RecordAsync(services, typeId, $"agree{isStrict}-p{parents.Count}", Opened);
                children[typeId] = await RecordAsync(services, typeId, $"agree{isStrict}-c{children.Count}", Opened);
            }

            var disagreements = new List<string>();

            foreach (var parentTypeId in allTypes)
            {
                var offered = await graph.GetPermittedChildTypeIdsAsync(
                    world.StructureId, parents[parentTypeId], Opened);

                foreach (var childTypeId in allTypes)
                {
                    // Asked rather than written: ValidatePlacementAsync is the authority, and the
                    // question is whether the picker's forwards reading of it agrees.
                    var errors = await services.GetRequiredService<IDimensionValidator>()
                        .ValidatePlacementAsync(
                            world.StructureId, children[childTypeId], parents[parentTypeId], Opened);

                    var wouldRefuse = errors.Any(error => !error.IsAdvisory);
                    var isOffered = offered.Contains(childTypeId, StringComparer.Ordinal);

                    if (wouldRefuse == isOffered)
                    {
                        disagreements.Add(
                            $"parent={world.NameOf(parentTypeId)} child={world.NameOf(childTypeId)} " +
                            $"offered={isOffered} refused={wouldRefuse}");
                    }
                }
            }

            disagreements.Should().BeEmpty(
                "a picker that offers what the write refuses, or hides what it would accept, is the defect ADR-0010 set out to fix");
        });

    // ---- scaffolding ----------------------------------------------------------------------

    private sealed record Shape(
        string StructureId,
        string Division,
        string Department,
        string Project,
        string Team,
        string Region = "",
        string Branch = "")
    {
        public string NameOf(string typeId) =>
            typeId == Division ? "Division"
            : typeId == Department ? "Department"
            : typeId == Project ? "Project"
            : typeId == Team ? "Team"
            : typeId == Region ? "Region"
            : typeId == Branch ? "Branch"
            : typeId;
    }

    /// <summary>Zenith: Division contains Department or Project; Project contains Team.</summary>
    private static async Task<Shape> ZenithAsync(IServiceProvider services, string prefix, bool isStrict = true)
    {
        var division = await TypeAsync(services, $"{prefix}-division");
        var department = await TypeAsync(services, $"{prefix}-department");
        var project = await TypeAsync(services, $"{prefix}-project");
        var team = await TypeAsync(services, $"{prefix}-team");

        var structureId = await StructureAsync(
            services,
            $"{prefix}-org",
            [division, department, project, team],
            roots: [division],
            containment: [(division, department), (division, project), (project, team)],
            isStrict: isStrict);

        return new Shape(structureId, division, department, project, team);
    }

    /// <summary>
    /// Crescent: Division contains Department or Region; Region contains Branch; Branch contains
    /// Department again.
    /// </summary>
    private static async Task<Shape> CrescentAsync(IServiceProvider services, string prefix)
    {
        var division = await TypeAsync(services, $"{prefix}-division");
        var department = await TypeAsync(services, $"{prefix}-department");
        var region = await TypeAsync(services, $"{prefix}-region");
        var branch = await TypeAsync(services, $"{prefix}-branch");

        var structureId = await StructureAsync(
            services,
            $"{prefix}-org",
            [division, region, branch, department],
            roots: [division],
            containment:
            [
                (division, department),
                (division, region),
                (region, branch),
                (branch, department),
            ]);

        return new Shape(structureId, division, department, string.Empty, string.Empty, region, branch);
    }

    private static async Task<string> TypeAsync(
        IServiceProvider services, string code, bool allowsSelfNesting = false)
    {
        var types = services.GetRequiredService<IDimensionTypeService>();

        if (await types.GetByCodeAsync(code) is { } existing)
        {
            return existing.DimensionTypeId;
        }

        var created = await types.CreateAsync(
            code, new BilingualText(code, $"{code}-ar"), [], allowsSelfNesting);

        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

        return created.Value!.DimensionTypeId;
    }

    private static async Task<string> StructureAsync(
        IServiceProvider services,
        string code,
        IReadOnlyList<string> levelTypeIds,
        IReadOnlyList<string> roots,
        IReadOnlyList<(string Parent, string Child)> containment,
        bool isStrict = true)
    {
        var created = await services.GetRequiredService<IStructureService>().CreateAsync(
            code,
            new BilingualText(code, $"{code}-ar"),
            levelTypeIds,
            allowSkipLevel: false,
            isStrict: isStrict,
            isPrimaryOrganisation: false,
            new StructureShape(
                roots,
                [.. containment.Select(pair => new StructureContainment(pair.Parent, pair.Child))]));

        created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));

        return created.Value!.StructureId;
    }

    private static Task<string> RecordAsync(
        IServiceProvider services, string dimensionTypeId, string code, DateOnly from) =>
        DimensionGraphScenario.RecordAsync(services, dimensionTypeId, code, from);
}
