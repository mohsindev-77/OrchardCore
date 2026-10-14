using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// One dimension value's own page: its facts, its two headcounts, and the tree of everything under
/// it. Decisions D23 C and D24.
/// </summary>
/// <remarks>
/// <b>Built against a chart this test places itself</b>, through the same services the mutation
/// screens use, so the page is proven against real placements rather than against its own view
/// model. Three levels below the root, because two is not enough to tell "the tree is nested"
/// from "the tree lists children", and a leaf beside them, because the section has to be absent
/// rather than empty.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionValuePageTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DimensionValuePageTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("value page test"))
            {
                await work(services);
            }
        });

    /// <summary>
    /// A value with three levels under it draws all of them, indented, each linking to its own page.
    /// </summary>
    /// <remarks>
    /// <b>Every dimension, not just this one.</b> The opposite of the dimension's own Tree/List
    /// screen, which Rule 2 scopes to one dimension: a reader who opened <em>Corporate</em> wants
    /// the departments in it and the sections in those, which is the organisation rather than the
    /// dimension.
    ///
    /// The depth is asserted because it is what the indentation is drawn from, and the parent
    /// attribute because it is what the collapse script walks — the same row contract the dimension
    /// list's tree view uses, which is why one script serves both.
    /// </remarks>
    [Fact]
    public async Task AValueWithThreeLevelsBelowItDrawsThemAllAsATree()
    {
        var world = await GivenAThreeLevelChartAsync("tree3");

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}?structureId={world.StructureId}");

        var rows = RowsOf(page);

        rows.Select(row => row.Code).Should().Equal(
            ["tree3-dept", "tree3-sec", "tree3-team", "tree3-team2"],
            "the whole subtree, each node immediately before the nodes under it");

        rows.Select(row => row.Depth).Should().Equal(["1", "2", "3", "3"], "indented by how deep they sit");

        rows[0].ParentRecordId.Should().BeEmpty("an immediate child has nothing above it in this tree");
        rows[1].ParentRecordId.Should().Be(world.DepartmentId);
        rows[2].ParentRecordId.Should().Be(world.SectionId);

        // Each row names the dimension it is of, which is the only thing telling a department from
        // the section inside it once the indentation is also showing containment.
        page.Should().Contain("Tree3 Department");
        page.Should().Contain("Tree3 Section");

        // And each name opens that unit's own page.
        page.Should().Contain($"/Admin/Dimensions/Records/Details/{world.SectionId}");
    }

    /// <summary>A leaf shows no section at all — not an empty table and not a "nothing here" line.</summary>
    /// <remarks>
    /// "This unit contains nothing" is already said by the absence of the section. A reader scanning
    /// a page does not need to be told what is not there, and an empty table is a thing to read
    /// before discovering it says nothing.
    /// </remarks>
    [Fact]
    public async Task ALeafHasNoUnitsBelowSectionAtAll()
    {
        var world = await GivenAThreeLevelChartAsync("leaf");

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.TeamId}?structureId={world.StructureId}");

        page.Should().Contain("value-facts", "the page itself still renders");
        page.Should().NotContain("value-tree-heading");
        page.Should().NotContain("id=\"value-tree\"");
    }

    /// <summary>
    /// The headcount says how many are here and how many are anywhere below, and both are links.
    /// </summary>
    /// <remarks>
    /// <b>The complaint this answers.</b> A division whose people all sit in its departments had a
    /// direct count of zero, and the page said "0 here" — which reads as "this division is empty"
    /// about a division with people in it.
    ///
    /// <b>Distinct, not summed.</b> One person assigned to two units below this one is one person.
    /// D1's <c>DISTINCT EmployeeId</c> rule is exactly about that, and summing the direct counts
    /// would have been one query cheaper and wrong.
    /// </remarks>
    [Fact]
    public async Task TheHeadcountSeparatesWhoIsHereFromWhoIsAnywhereBelow()
    {
        var world = await GivenAThreeLevelChartAsync("heads");

        await InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            // Everybody is on a LEAF, which is the engine's rule rather than this test's choice: a
            // unit with units under it may not hold employees, because somebody placed there would
            // be counted by it and again by every roll-up beneath it. That rule is also what makes
            // this page necessary — the division's own count is zero and always will be.
            await AssignAsync(assignments, world.StructureId, world.TeamId, "heads-emp-1");
            await AssignAsync(assignments, world.StructureId, world.SecondTeamId, "heads-emp-3");

            // One person in two places at once: a split, applied as one operation because the rule
            // that matters — the allocations totalling 100 — is about the set rather than the row.
            var split = await assignments.ReallocateAsync(
                "heads-emp-2",
                world.StructureId,
                [
                    new AssignmentSplitEntry(world.TeamId, 60m, IsPrimary: true),
                    new AssignmentSplitEntry(world.SecondTeamId, 40m, IsPrimary: false),
                ],
                new DateOnly(2024, 1, 1));

            split.Succeeded.Should().BeTrue(string.Join("; ", split.Errors.Select(e => e.Message.Value)));
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}?structureId={world.StructureId}");

        var document = new HtmlParser().ParseDocument(page);

        document.QuerySelector("#value-employees-direct")!.TextContent.Trim()
            .Should().Be(
                "0 directly",
                "this is the complaint: a division's own count is zero and the page used to stop there");

        document.QuerySelector("#value-employees-below")!.TextContent.Trim()
            .Should().Be(
                "3 including units below",
                "three distinct people, not four placement rows: emp-2 is in two units");

        // Each number opens the employee list — the second with sub-units included, which is the
        // filter that makes the list agree with the number beside it.
        var below = document.QuerySelector("#value-employee-list-below")!.GetAttribute("href") ?? string.Empty;

        below.Should().Contain("includeSubUnits=true", "the list has to agree with the number beside it");
        below.Should().Contain(world.DivisionId);

        // Nobody is here directly, so that number is not a link: a link to an empty list is a
        // promise a reader follows once.
        document.QuerySelector("#value-employee-list").Should().BeNull();
    }

    /// <summary>Each row in the tree counts everything beneath it, with its own count on hover.</summary>
    [Fact]
    public async Task EachUnitInTheTreeCountsEverythingBeneathIt()
    {
        var world = await GivenAThreeLevelChartAsync("rollup");

        await InTenantAsSystemAsync(async services =>
        {
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            // Leaves only, which is the engine's rule: a unit with units under it may not hold
            // employees. One person in each of the two teams.
            await AssignAsync(assignments, world.StructureId, world.TeamId, "rollup-emp-1");
            await AssignAsync(assignments, world.StructureId, world.SecondTeamId, "rollup-emp-2");
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}?structureId={world.StructureId}");

        var document = new HtmlParser().ParseDocument(page);

        var cells = document
            .QuerySelectorAll("#value-tree tr.record-row .value-tree-employees")
            .Select(cell => cell.TextContent.Trim())
            .ToList();

        cells.Should().Equal(
            ["2", "2", "1", "1"],
            "the department and the section each roll up both teams; each team holds its own one");

        // The direct count is the hover, because the number a reader scanning a chart wants is the
        // roll-up and the one they occasionally need is how many of those are at this unit itself.
        document.QuerySelectorAll("#value-tree tr.record-row .value-tree-employees")[0]
            .GetAttribute("title")
            .Should().Be("0 directly", "the department holds nobody itself — they are in the teams");
    }

    /// <summary>
    /// Retired units are out of the tree until they are asked for, and the toggle is a plain link.
    /// </summary>
    /// <remarks>
    /// The raw set is what the closure index says is placed here now; the filter is on each
    /// record's own effective range, which is the same Active/All distinction the list screen's
    /// Status filter makes rather than a second meaning of the word.
    /// </remarks>
    [Fact]
    public async Task RetiredUnitsAreHiddenUntilTheToggleAsksForThem()
    {
        var world = await GivenAThreeLevelChartAsync("retired");

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();

            // The leaf, so there is nothing underneath whose fate has to be decided — this test is
            // about the toggle, and RetireAsync's children disposition is its own screen's question.
            var retired = await records.RetireAsync(world.TeamId, new DateOnly(2024, 6, 1));

            retired.Succeeded.Should().BeTrue(string.Join("; ", retired.Errors.Select(e => e.Message.Value)));
        });

        var active = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}?structureId={world.StructureId}");

        RowsOf(active).Select(row => row.Code).Should().NotContain("retired-team");
        RowsOf(active).Select(row => row.Code).Should().Contain("retired-sec", "the live ones are still there");

        var all = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}?structureId={world.StructureId}&includeRetired=true");

        RowsOf(all).Select(row => row.Code).Should().Contain("retired-team");
    }

    /// <summary>
    /// The section offers three views, Tree is the one you get without asking, and each is a URL.
    /// </summary>
    /// <remarks>
    /// <b>Decision D25.</b> The order is the brief's — Hierarchy, Tree, List — and the default is
    /// Tree rather than the first of them, because Tree is what the section already was and a
    /// reader who had learnt it should not have to find it again.
    ///
    /// Each view is a plain link carrying the whole state, which is what makes it bookmarkable and
    /// what makes the back button land where the reader expects.
    /// </remarks>
    [Fact]
    public async Task TheSectionOffersThreeViewsAndDefaultsToTree()
    {
        var world = await GivenAThreeLevelChartAsync("views");

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}?structureId={world.StructureId}");

        var document = new HtmlParser().ParseDocument(page);

        document.QuerySelectorAll("#value-units-views a")
            .Select(link => link.GetAttribute("data-units-view"))
            .Should().Equal(["hierarchy", "tree", "list"], "the order the brief names them in");

        document.QuerySelector("#value-units-view-tree")!.GetAttribute("aria-current")
            .Should().Be("true", "Tree is what the section already was");

        document.QuerySelector("#value-tree").Should().NotBeNull("and Tree is what is drawn");
        document.QuerySelector("#value-hierarchy").Should().BeNull();
        document.QuerySelector("#value-list").Should().BeNull();

        // Every view link carries the whole state, so switching cannot drop the chart or the date.
        document.QuerySelector("#value-units-view-list")!.GetAttribute("href")
            .Should().Contain("view=list").And.Contain(world.StructureId);
    }

    [Theory]
    [InlineData("hierarchy", "value-hierarchy")]
    [InlineData("tree", "value-tree")]
    [InlineData("list", "value-list")]
    public async Task EachViewDrawsItsOwnRenderingAndOnlyThat(string view, string expected)
    {
        var world = await GivenAThreeLevelChartAsync($"draw{view}");

        var document = await ViewAsync(world, view);

        document.QuerySelector("#" + expected).Should().NotBeNull();

        document.QuerySelector($"#value-units-view-{view}")!.GetAttribute("aria-current")
            .Should().Be("true", "the switch highlights the one being shown");

        foreach (var other in ViewContainers.Where(id => id != expected))
        {
            document.QuerySelector("#" + other).Should().BeNull("one rendering at a time");
        }
    }

    /// <summary>The element each of the three views draws, and nothing else does.</summary>
    private static readonly string[] ViewContainers = ["value-hierarchy", "value-tree", "value-list"];

    /// <summary>
    /// The hierarchy is the designer's chart markup, with the value as its root and no way to edit.
    /// </summary>
    /// <remarks>
    /// <b>Reused, not reimplemented.</b> The boxes are <c>li.designer-node &gt; .designer-card</c>
    /// inside a <c>.designer-surface</c>, which is the shape <c>organisation-designer.css</c> draws
    /// the connector lines, the row levelling and the RTL mirroring for — so this chart is already
    /// right in both directions without a second layout existing. Nothing in the designer is
    /// changed to get it.
    ///
    /// <b>And no ⋯ menu.</b> D25 is explicit that editing stays in the designer and on the value
    /// pages. The element is absent rather than hidden, which is how the designer itself treats a
    /// reader without permission.
    /// </remarks>
    [Fact]
    public async Task TheHierarchyIsAReadOnlyChartRootedAtTheValue()
    {
        var world = await GivenAThreeLevelChartAsync("chart");

        var document = await ViewAsync(world, "hierarchy");

        var root = document.QuerySelector("#value-hierarchy .designer-node-structure");

        root.Should().NotBeNull("the value itself is the root box");
        root!.GetAttribute("data-record-id").Should().Be(world.DivisionId);
        root.QuerySelector(".value-hierarchy-name")!.TagName.Should().Be("SPAN",
            "the root is the page the reader is already on, so its name is not a link");

        // One box per descendant, nested, each linking to its own page.
        document.QuerySelectorAll("#value-hierarchy li.designer-node")
            .Select(node => node.GetAttribute("data-record-id"))
            .Should().Contain([world.DepartmentId, world.SectionId, world.TeamId, world.SecondTeamId]);

        var team = document.QuerySelector($"#value-hierarchy li[data-record-id='{world.TeamId}'] a.value-hierarchy-name");

        team.Should().NotBeNull();
        team!.GetAttribute("href").Should().Contain($"/Dimensions/Records/Details/{world.TeamId}");

        // Read-only: the designer's card menu is the thing this must not have.
        document.QuerySelectorAll("#value-hierarchy .designer-actions").Should().BeEmpty();
        document.QuerySelectorAll("#value-hierarchy [data-designer-action]").Should().BeEmpty();
    }

    /// <summary>The list is flat, carries the Parent column, and sorts from the query string.</summary>
    [Fact]
    public async Task TheListIsFlatAndSortsByColumn()
    {
        var world = await GivenAThreeLevelChartAsync("flat");

        var document = await ViewAsync(world, "list");

        document.QuerySelectorAll("#value-list thead th")
            .Select(cell => cell.QuerySelector("a")?.GetAttribute("data-sort"))
            .Should().Equal(["code", "name", "dimension", "parent", "status", "employees"]);

        // Flat: no depth, no toggle, and the Parent column carrying what the indentation carried.
        document.QuerySelectorAll("#value-list .record-toggle").Should().BeEmpty();

        document.QuerySelector($"#value-list tr[data-record-code='flat-sec'] .value-list-parent")!
            .TextContent.Trim().Should().Be("Department One");

        var sorted = await ViewAsync(world, "list", "&sort=name&desc=true");

        sorted.QuerySelectorAll("#value-list tbody tr")
            .Select(row => row.GetAttribute("data-record-code"))
            .Should().Equal(["flat-team2", "flat-team", "flat-sec", "flat-dept"],
                "Team Two, Team One, Section One, Department One");
    }

    /// <summary>
    /// A chart of fifty-odd units across four levels is still a fixed handful of queries.
    /// </summary>
    /// <remarks>
    /// <b>The scale the brief names, on the view that does the most work.</b> The subtree is read
    /// once — one closure read, one batched ancestor-path read, one batched headcount, one read of
    /// the placements — and all three views are shaped from it, so the hierarchy's nesting costs no
    /// query that the tree does not. A regression to a lookup per node would not fail this test by
    /// being wrong; it would fail it by taking minutes, which is why the assertion is on the shape
    /// of the answer and the timing is left to the suite's own patience.
    /// </remarks>
    [Fact]
    public async Task AChartOfFiftyUnitsAcrossFourLevelsRendersInEveryView()
    {
        var world = await GivenAWideChartAsync("wide", branches: 12, perBranch: 4);

        foreach (var (view, container, rowSelector) in new[]
        {
            ("tree", "value-tree", "#value-tree tr.record-row"),
            ("list", "value-list", "#value-list tbody tr"),
            ("hierarchy", "value-hierarchy", "#value-hierarchy li.designer-node"),
        })
        {
            var document = await ViewAsync(world, view);

            document.QuerySelector("#" + container).Should().NotBeNull($"the {view} view renders");

            var drawn = document.QuerySelectorAll(rowSelector).Length;

            // The hierarchy counts the root box as well, which the other two do not draw.
            (view == "hierarchy" ? drawn - 1 : drawn).Should().Be(
                world.Descendants,
                $"every unit under the value is in the {view} view");
        }
    }

    // ---- harness ---------------------------------------------------------------------------

    private async Task<AngleSharp.Html.Dom.IHtmlDocument> ViewAsync(
        Chart world, string view, string extra = "")
    {
        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator,
            $"/Admin/Dimensions/Records/Details/{world.DivisionId}"
            + $"?structureId={world.StructureId}&view={view}{extra}");

        return new HtmlParser().ParseDocument(page);
    }

    /// <summary>
    /// A division with <paramref name="branches"/> departments, each holding
    /// <paramref name="perBranch"/> sections, each holding a team: four levels and fifty-odd units.
    /// </summary>
    private async Task<Chart> GivenAWideChartAsync(string prefix, int branches, int perBranch)
    {
        var world = await GivenAThreeLevelChartAsync(prefix);
        var descendants = 4;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            var department = (await types.GetByCodeAsync($"{prefix}-department"))!.DimensionTypeId;
            var section = (await types.GetByCodeAsync($"{prefix}-section"))!.DimensionTypeId;
            var team = (await types.GetByCodeAsync($"{prefix}-team"))!.DimensionTypeId;

            var from = new DateOnly(2024, 1, 1);

            for (var branch = 0; branch < branches; branch++)
            {
                var added = await records.AddUnitAsync(
                    world.StructureId, world.DivisionId, department,
                    $"{prefix}-d{branch:00}", new BilingualText($"Department {branch:00}", "إدارة"), from);

                added.Succeeded.Should().BeTrue(string.Join("; ", added.Errors.Select(e => e.Message.Value)));
                descendants++;

                for (var child = 0; child < perBranch; child++)
                {
                    var inner = await records.AddUnitAsync(
                        world.StructureId, added.Value!.RecordId, section,
                        $"{prefix}-s{branch:00}{child:00}",
                        new BilingualText($"Section {branch:00}-{child:00}", "شعبة"), from);

                    inner.Succeeded.Should().BeTrue(string.Join("; ", inner.Errors.Select(e => e.Message.Value)));
                    descendants++;

                    if (child != 0)
                    {
                        continue;
                    }

                    // One team under the first section of each branch, so the chart is four levels
                    // deep rather than three everywhere.
                    var leaf = await records.AddUnitAsync(
                        world.StructureId, inner.Value!.RecordId, team,
                        $"{prefix}-t{branch:00}", new BilingualText($"Team {branch:00}", "فريق"), from);

                    leaf.Succeeded.Should().BeTrue(string.Join("; ", leaf.Errors.Select(e => e.Message.Value)));
                    descendants++;
                }
            }
        });

        descendants.Should().BeGreaterThan(50, "the brief asks for a chart of fifty-odd units");

        return world with { Descendants = descendants };
    }

    /// <param name="SecondTeamId">
    /// A sibling of <paramref name="TeamId"/>, because employees may only be placed on a leaf — a
    /// unit with units under it may not hold them, since somebody there would be counted by it and
    /// again by every roll-up beneath it. Two leaves is the smallest chart that can show a person
    /// in two places and a roll-up that is not a sum.
    /// </param>
    /// <param name="Descendants">How many units sit under the division, however deep.</param>
    private sealed record Chart(
        string StructureId,
        string DivisionId,
        string DepartmentId,
        string SectionId,
        string TeamId,
        string SecondTeamId,
        int Descendants = 4);

    /// <summary>Four levels, so the tree under the Division is three deep and the Team is a leaf.</summary>
    private static readonly string[] LevelNames = ["Division", "Department", "Section", "Team"];

    /// <summary>
    /// A Division holding a Department holding a Section holding a Team: four levels, so the tree
    /// under the Division is three deep and the Team is a leaf.
    /// </summary>
    private async Task<Chart> GivenAThreeLevelChartAsync(string prefix)
    {
        string structureId = string.Empty;
        string divisionId = string.Empty, departmentId = string.Empty, sectionId = string.Empty;
        string teamId = string.Empty, secondTeamId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var levels = new List<string>();

            foreach (var level in LevelNames)
            {
                var created = await types.CreateAsync(
                    $"{prefix}-{level.ToLowerInvariant()}",
                    new BilingualText($"{char.ToUpperInvariant(prefix[0])}{prefix[1..]} {level}", level),
                    [],
                    allowsSelfNesting: false);

                created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Message.Value)));
                levels.Add(created.Value!.DimensionTypeId);
            }

            var structure = await structures.CreateAsync(
                $"{prefix}-org",
                new BilingualText($"{prefix} organisation", "منظمة"),
                levels,
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);

            structure.Succeeded.Should().BeTrue(string.Join("; ", structure.Errors.Select(e => e.Message.Value)));
            structureId = structure.Value!.StructureId;

            var from = new DateOnly(2024, 1, 1);

            var division = await records.AddUnitAsync(
                structureId, null, levels[0], $"{prefix}-div", new BilingualText("Division One", "قطاع"), from);

            division.Succeeded.Should().BeTrue(string.Join("; ", division.Errors.Select(e => e.Message.Value)));
            divisionId = division.Value!.RecordId;

            var department = await records.AddUnitAsync(
                structureId, divisionId, levels[1], $"{prefix}-dept", new BilingualText("Department One", "إدارة"), from);

            department.Succeeded.Should().BeTrue(string.Join("; ", department.Errors.Select(e => e.Message.Value)));
            departmentId = department.Value!.RecordId;

            var section = await records.AddUnitAsync(
                structureId, departmentId, levels[2], $"{prefix}-sec", new BilingualText("Section One", "شعبة"), from);

            section.Succeeded.Should().BeTrue(string.Join("; ", section.Errors.Select(e => e.Message.Value)));
            sectionId = section.Value!.RecordId;

            var team = await records.AddUnitAsync(
                structureId, sectionId, levels[3], $"{prefix}-team", new BilingualText("Team One", "فريق"), from);

            team.Succeeded.Should().BeTrue(string.Join("; ", team.Errors.Select(e => e.Message.Value)));
            teamId = team.Value!.RecordId;

            var second = await records.AddUnitAsync(
                structureId, sectionId, levels[3], $"{prefix}-team2", new BilingualText("Team Two", "فريق"), from);

            second.Succeeded.Should().BeTrue(string.Join("; ", second.Errors.Select(e => e.Message.Value)));
            secondTeamId = second.Value!.RecordId;
        });

        return new Chart(structureId, divisionId, departmentId, sectionId, teamId, secondTeamId);
    }

    private static async Task AssignAsync(
        IEmployeeAssignmentService assignments,
        string structureId,
        string recordId,
        string employeeId,
        decimal allocationPercent = 100m,
        bool isPrimary = true)
    {
        // A person in two units at once is a split allocation: the concurrent placements must
        // total 100 and exactly one of them is primary, which is what the engine enforces and what
        // makes "distinct employees" a different number from "assignment rows".
        var placed = await assignments.PlaceAsync(
            employeeId, structureId, recordId, new DateOnly(2024, 1, 1), allocationPercent, isPrimary);

        placed.Succeeded.Should().BeTrue(string.Join("; ", placed.Errors.Select(e => e.Message.Value)));
    }

    private sealed record Row(string Code, string Depth, string ParentRecordId);

    private static List<Row> RowsOf(string html) =>
        [
            .. new HtmlParser().ParseDocument(html)
                .QuerySelectorAll("#value-tree tr.record-row")
                .Select(row => new Row(
                    row.GetAttribute("data-record-code") ?? string.Empty,
                    row.GetAttribute("data-depth") ?? string.Empty,
                    row.GetAttribute("data-parent-record-id") ?? string.Empty)),
        ];
}
