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

            // The scenario's Section declares self-nesting, so the type's veto is not in the way.
            // Which axis allows it is now a property of the axis.
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

            refused.Succeeded.Should().BeFalse("the other one does not, and the type's flag is a veto, not a grant");
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);

            structures.Should().NotBeNull();
        });

    [Fact]
    public async Task ATypeThatVetoesSelfNestingCannotBeGrantedItByAStructure() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // The scenario's Department does not declare self-nesting. Ticking the cell anyway is
            // not enough, and must not be: no axis can grant past the type's own refusal.
            var structureId = await StructureAsync(
                services, "veto", [types.Division, types.Department],
                roots: [types.Division],
                containment: [(types.Division, types.Department), (types.Department, types.Department)]);

            var outer = await RecordAsync(services, types.Department, "veto-outer", Opened);
            var inner = await RecordAsync(services, types.Department, "veto-inner", Opened);

            var refused = await graph.MoveAsync(structureId, inner, outer, Opened);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.SelfNesting);
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

            // One record of every type, so every ordered pair can actually be attempted.
            var byType = new Dictionary<string, string>(StringComparer.Ordinal);
            var allTypes = new[] { world.Division, world.Department, world.Project, world.Team };

            foreach (var typeId in allTypes)
            {
                byType[typeId] = await RecordAsync(services, typeId, $"agree{isStrict}-{byType.Count}", Opened);
            }

            var disagreements = new List<string>();

            foreach (var parentTypeId in allTypes)
            {
                var offered = await graph.GetPermittedChildTypeIdsAsync(
                    world.StructureId, byType[parentTypeId], Opened);

                foreach (var childTypeId in allTypes)
                {
                    // Asked rather than written: ValidatePlacementAsync is the authority, and the
                    // question is whether the picker's forwards reading of it agrees.
                    var errors = await services.GetRequiredService<IDimensionValidator>()
                        .ValidatePlacementAsync(
                            world.StructureId, byType[childTypeId], byType[parentTypeId], Opened);

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
