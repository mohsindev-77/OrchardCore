using System.Net;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using OrchardCore.Entities;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.ViewModels;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// FRS v2.5 §4.1, slice F1: the Alias field, and a dimension's own tree view.
/// </summary>
/// <remarks>
/// Two features in one file because they are one screen, and because the tree is the only place the
/// alias's reason for existing is visible: the FRS's figures put <em>Head Office</em> against KKT
/// Cayman and <em>Hiring Team</em> against Recruitment, which is what a reader scanning a tree of
/// codes and formal names actually navigates by.
///
/// <b>Rule 2 is the assertion that matters.</b> "A tree view only shows the values that belong to
/// that one dimension." A Department sits under a Division on the organisation chart and under
/// <em>HR</em> on the Department screen — so the world below has both shapes in it, and the tests
/// check that the chart's parent does <em>not</em> become the tree's.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionAliasAndTreeViewTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DimensionAliasAndTreeViewTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// A Division holding Departments, and Departments nesting inside Departments.
    /// </summary>
    /// <remarks>
    /// The second half is the point: the `X → X` diagonal (ADR-0010's addendum) is what a
    /// dimension's own tree is made of, and without it every Department would be a root and the
    /// tree would be a list with extra markup.
    ///
    /// <code>
    ///   chart                          Department screen's own tree
    ///   ─────                          ───────────────────────────
    ///   av-div-head (Division)         av-dept-hr
    ///     av-dept-hr                     av-dept-recruitment
    ///       av-dept-recruitment            av-dept-graduate
    ///         av-dept-graduate           av-dept-finance
    ///     av-dept-finance
    /// </code>
    ///
    /// Every code is prefixed and the builder is idempotent, because the tenant is shared.
    /// </remarks>
    private async Task<World> GivenAWorldAsync()
    {
        World world = null!;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var division = await TypeAsync(types, "av-division", "AV Division");
            var department = await TypeAsync(types, "av-department", "AV Department");

            var structure = await structures.GetByCodeAsync("av-org");

            if (structure is null)
            {
                var created = await structures.CreateAsync(
                    "av-org",
                    new BilingualText("AV Org", "إيه في"),
                    [division.DimensionTypeId, department.DimensionTypeId],
                    allowSkipLevel: false,
                    isStrict: true,
                    isPrimaryOrganisation: false,
                    new StructureShape(
                        [division.DimensionTypeId],
                        [
                            new StructureContainment(division.DimensionTypeId, department.DimensionTypeId),
                            // The diagonal. A Department inside a Department is what makes the
                            // Department screen's tree a tree.
                            new StructureContainment(department.DimensionTypeId, department.DimensionTypeId),
                        ]));

                created.Succeeded.Should().BeTrue(
                    string.Join("; ", created.Errors.Select(error => error.Message.Value)));

                structure = created.Value!;
            }

            var head = await UnitAsync(records, structure.StructureId, null, division.DimensionTypeId,
                "av-div-head", "AV Head Office", alias: new BilingualText("HQ", "الرئيسي"));

            var hr = await UnitAsync(records, structure.StructureId, head, department.DimensionTypeId,
                "av-dept-hr", "AV Human Resources", alias: new BilingualText("People & Culture", string.Empty));

            var recruitment = await UnitAsync(records, structure.StructureId, hr, department.DimensionTypeId,
                "av-dept-recruitment", "AV Recruitment", alias: new BilingualText("Hiring Team", string.Empty));

            await UnitAsync(records, structure.StructureId, recruitment, department.DimensionTypeId,
                "av-dept-graduate", "AV Graduate Intake", alias: null);

            await UnitAsync(records, structure.StructureId, head, department.DimensionTypeId,
                "av-dept-finance", "AV Finance", alias: null);

            world = new World(
                structure.StructureId, division.DimensionTypeId, department.DimensionTypeId, head, hr);
        });

        return world;
    }

    // ---- the alias, through the services ------------------------------------------------

    /// <summary>
    /// An alias given at creation survives the trip through the index, which is the path the list
    /// reads.
    /// </summary>
    /// <remarks>
    /// Read back through <c>GetByCodeAsync</c> rather than off the content item on purpose: that
    /// resolves a record from <c>DimensionRecordPartIndex</c> without loading the item, which is
    /// what the list does for fifty rows a page. A field that was on the part and not on the index
    /// would pass an assertion against the item and leave the column blank on the screen.
    /// </remarks>
    [Fact]
    public async Task AnAliasGivenAtCreationIsReadBackFromTheIndex()
    {
        await GivenAWorldAsync();

        await InTenantAsSystemAsync(async services =>
        {
            var record = await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync("av-dept-hr");

            record.Should().NotBeNull();
            record!.AliasEn.Should().Be("People & Culture");
            record.AliasAr.Should().BeEmpty("the Arabic half is optional, like every other one");
            record.Alias.Should().Be("People & Culture", "the display falls back rather than blanking");
        });
    }

    /// <summary>
    /// Setting an alias is undated, and setting the one already stored writes nothing at all.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The first is the whole reason <c>SetAliasAsync</c> exists rather than a
    /// parameter on a dated write. The second is what stops the edit screen — which posts every
    /// field it shows whether or not the reader touched any of them — from publishing a new version
    /// of the record and leaving an audit entry saying somebody changed the alias, on a form opened
    /// to fix something else.
    /// </remarks>
    [Fact]
    public async Task SettingAnAliasChangesItAndSettingTheSameOneAgainWritesNothing()
    {
        var world = await GivenAWorldAsync();

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();

            var changed = await records.SetAliasAsync(
                world.HrRecordId, new BilingualText("People Team", "فريق الموارد"));

            changed.Succeeded.Should().BeTrue(
                string.Join("; ", changed.Errors.Select(error => error.Message.Value)));

            var after = await records.GetByCodeAsync("av-dept-hr");
            after!.AliasEn.Should().Be("People Team");
            after.AliasAr.Should().Be("فريق الموارد");

            var before = await AliasChangeCountAsync(services, "av-dept-hr");

            // The same value again. Trimmed on the way in, so surrounding whitespace is the same
            // value too — which is what a form field with a stray space posts.
            var again = await records.SetAliasAsync(
                world.HrRecordId, new BilingualText("  People Team  ", "فريق الموارد"));

            again.Succeeded.Should().BeTrue();

            (await AliasChangeCountAsync(services, "av-dept-hr")).Should().Be(
                before, "setting the alias it already holds is not a change and must not be audited");

            // Put it back, so the tests in this class do not have to run in an order.
            await records.SetAliasAsync(world.HrRecordId, new BilingualText("People & Culture", string.Empty));
        });
    }

    /// <summary>
    /// An alias longer than its column is refused, in the same words whatever created the record.
    /// </summary>
    /// <remarks>
    /// Enforced in <c>DimensionRecordPartHandler</c>, which is the one point every path into a
    /// record shares — the admin screens, the API, GraphQL, a recipe and an import. Checked rather
    /// than left to the database because a provider truncates or throws depending on which one it
    /// is, and a silently shortened alias is a wrong answer that looks like a right one.
    /// </remarks>
    [Fact]
    public async Task AnAliasLongerThanItsColumnIsRefused()
    {
        var world = await GivenAWorldAsync();

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();

            var tooLong = new string('x', 256);

            var refused = await records.SetAliasAsync(world.HrRecordId, new BilingualText(tooLong, string.Empty));

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.InvalidValue);

            // And the stored value is untouched, which is the half a refusal has to get right.
            (await records.GetByCodeAsync("av-dept-hr"))!.AliasEn.Should().Be("People & Culture");
        });
    }

    // ---- the list and the tree, through real HTTP ----------------------------------------

    [Fact]
    public async Task TheListCarriesAnAliasColumnWithTheStoredValue()
    {
        var world = await GivenAWorldAsync();

        var html = await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}");

        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelectorAll("#record-list thead th")
            .Select(cell => cell.TextContent.Trim())
            .Should().Contain("Alias", "FRS v2.5 §4.1 names Code, Name, Alias and Status");

        AliasOf(document, "av-dept-recruitment").Should().Be("Hiring Team");
        AliasOf(document, "av-dept-graduate").Should().BeEmpty("a unit without one shows nothing");
    }

    /// <summary>
    /// The five controls FRS v2.5 §4.1 puts in the top-right corner are on the screen.
    /// </summary>
    /// <remarks>
    /// By id rather than by caption, so the assertion holds in both languages — the captions are
    /// localised and the ids are the contract the browser tests and this one share.
    /// </remarks>
    [Fact]
    public async Task TheToolbarCarriesFilterAddEditDeleteAndRefresh()
    {
        var world = await GivenAWorldAsync();

        var document = new HtmlParser().ParseDocument(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}"));

        foreach (var id in new[]
        {
            "record-filter-toggle", "record-new", "record-edit-selected",
            "record-delete-selected", "record-refresh", "record-view-tree", "record-view-list",
        })
        {
            document.QuerySelector($"#{id}").Should().NotBeNull("the toolbar needs {0}", id);
        }
    }

    /// <summary>
    /// Rule 2: the Department tree is built from Departments, and the Division above them is not in
    /// it.
    /// </summary>
    /// <remarks>
    /// The assertion that would fail if the tree were built from the organisation chart instead of
    /// from the dimension's own values. HR sits under a Division on the chart; on this screen it is
    /// a root, because the nearest value <em>of this dimension</em> above it is none.
    /// </remarks>
    [Fact]
    public async Task TheTreeViewNestsADimensionUnderItsOwnValuesAndNothingElse()
    {
        var world = await GivenAWorldAsync();

        var document = new HtmlParser().ParseDocument(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}&view={DimensionRecordView.Tree}"));

        var rows = document.QuerySelectorAll("#record-list tr.record-row")
            .ToDictionary(
                row => row.GetAttribute("data-record-code")!,
                row => (
                    Depth: row.GetAttribute("data-depth"),
                    Parent: row.GetAttribute("data-parent-record-id"),
                    Children: row.GetAttribute("data-child-count")),
                StringComparer.Ordinal);

        rows.Should().ContainKeys("av-dept-hr", "av-dept-recruitment", "av-dept-graduate", "av-dept-finance");
        rows.Should().NotContainKey("av-div-head", "a tree view shows only that one dimension's values");

        rows["av-dept-hr"].Depth.Should().Be("0", "the Division above it is not of this dimension");
        rows["av-dept-hr"].Parent.Should().BeNullOrEmpty();
        rows["av-dept-hr"].Children.Should().Be("1");

        rows["av-dept-recruitment"].Depth.Should().Be("1");
        rows["av-dept-graduate"].Depth.Should().Be("2", "it nests two Departments deep");

        rows["av-dept-finance"].Depth.Should().Be("0", "its only parent on the chart is a Division");

        // Depth-first order, so a child is rendered directly under its own parent rather than
        // after every other root.
        document.QuerySelectorAll("#record-list tr.record-row")
            .Select(row => row.GetAttribute("data-record-code"))
            .SkipWhile(code => code != "av-dept-hr")
            .Take(3)
            .Should().Equal(["av-dept-hr", "av-dept-recruitment", "av-dept-graduate"]);
    }

    /// <summary>
    /// A branch with children gets an expand control; a leaf gets the space where one would be.
    /// </summary>
    /// <remarks>
    /// The spacer is not decoration: without it a leaf's code sits one control-width closer to the
    /// start than its siblings', which reads as a different depth.
    /// </remarks>
    [Fact]
    public async Task OnlyABranchGetsAnExpandControlAndALeafGetsTheSpaceWhereOneWouldBe()
    {
        var world = await GivenAWorldAsync();

        var document = new HtmlParser().ParseDocument(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}&view={DimensionRecordView.Tree}"));

        Row(document, "av-dept-hr")!.QuerySelector(".record-toggle").Should().NotBeNull();
        Row(document, "av-dept-graduate")!.QuerySelector(".record-toggle").Should().BeNull();
        Row(document, "av-dept-graduate")!.QuerySelector(".record-toggle-spacer").Should().NotBeNull();
    }

    /// <summary>
    /// List view is flat, whatever the tree would have been.
    /// </summary>
    [Fact]
    public async Task ListViewIndentsNothing()
    {
        var world = await GivenAWorldAsync();

        var document = new HtmlParser().ParseDocument(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}&view={DimensionRecordView.List}"));

        document.QuerySelectorAll("#record-list tr.record-row")
            .Select(row => row.GetAttribute("data-depth"))
            .Should().AllBe("0");

        document.QuerySelector("#record-list")!.ClassName
            .Should().NotContain("record-tree");
    }

    /// <summary>
    /// A dimension on more than one structure has more than one tree, and the screen says so rather
    /// than drawing one of them.
    /// </summary>
    /// <remarks>
    /// Parentage is scoped to a structure (ADR-0005), which is the whole reason a record can sit on
    /// several axes. Picking an axis here would be the guess this screen already refuses to make
    /// for the Parent column and the headcount.
    /// </remarks>
    [Fact]
    public async Task TreeViewSaysSoWhenTheDimensionIsOnMoreThanOneStructureAndNoneIsChosen()
    {
        var world = await GivenAWorldAsync();

        await InTenantAsSystemAsync(async services =>
        {
            var structures = services.GetRequiredService<IStructureService>();

            if (await structures.GetByCodeAsync("av-org-second") is null)
            {
                var second = await structures.CreateAsync(
                    "av-org-second",
                    new BilingualText("AV Second", "الثاني"),
                    [world.DepartmentTypeId],
                    allowSkipLevel: false,
                    isStrict: true,
                    isPrimaryOrganisation: false,
                    new StructureShape([world.DepartmentTypeId], []));

                second.Succeeded.Should().BeTrue(
                    string.Join("; ", second.Errors.Select(error => error.Message.Value)));
            }
        });

        var html = await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&view={DimensionRecordView.Tree}");

        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelector("#record-tree-needs-structure").Should().NotBeNull();
        document.QuerySelector("#record-list").Should().BeNull("no tree is drawn until one is chosen");

        // And choosing one draws it.
        var chosen = new HtmlParser().ParseDocument(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}&view={DimensionRecordView.Tree}"));

        chosen.QuerySelector("#record-tree-needs-structure").Should().BeNull();
        chosen.QuerySelector("#record-list").Should().NotBeNull();
    }

    // ---- the Delete control ---------------------------------------------------------------

    /// <summary>
    /// Delete takes the reader to the dated screen that ends a unit, and says so on the way.
    /// </summary>
    /// <remarks>
    /// Slice F1 puts the control the FRS asks for on the screen; ADR-0025's "a value nothing has
    /// ever used is really deleted" is slice F9. What must be true now is that the button is
    /// reachable, that it lands somewhere that explains what it will do, and that it does not
    /// quietly do something else.
    /// </remarks>
    [Fact]
    public async Task DeleteLandsOnTheDatedRetirementScreen()
    {
        var world = await GivenAWorldAsync();

        // The administrator's client follows redirects, so this asserts where the reader ends up
        // rather than that a 302 was issued — which is the thing that matters and the thing a
        // status-code assertion would leave open.
        var response = await _fixture.Administrator.GetAsync(
            $"/Admin/Dimensions/Records/Delete?id={world.HrRecordId}"
            + $"&dimensionTypeId={world.DepartmentTypeId}&structureId={world.StructureId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var landed = response.RequestMessage!.RequestUri!;

        landed.AbsolutePath.Should().Contain("Designer/Retire");
        landed.Query.Should().Contain(world.HrRecordId);
    }

    /// <summary>
    /// Pressing a toolbar button with no row ticked says so rather than failing.
    /// </summary>
    /// <remarks>
    /// The toolbar's Edit and Delete act on the row the reader selected, and a form can always be
    /// submitted with nothing selected — a 404 on an empty id would read as a broken product.
    /// </remarks>
    [Theory]
    [InlineData("Edit")]
    [InlineData("Delete")]
    public async Task AToolbarActionWithNoRowChosenComesBackToTheList(string action)
    {
        var world = await GivenAWorldAsync();

        var response = await _fixture.Administrator.GetAsync(
            $"/Admin/Dimensions/Records/{action}?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Contain("Records/Index");

        // And it is the list, not an error page wearing the list's URL.
        (await response.Content.ReadAsStringAsync()).Should().Contain("record-list-heading");
    }

    // ---- harness ---------------------------------------------------------------------------

    private sealed record World(
        string StructureId,
        string DivisionTypeId,
        string DepartmentTypeId,
        string HeadRecordId,
        string HrRecordId);

    private static AngleSharp.Dom.IElement? Row(AngleSharp.Html.Dom.IHtmlDocument document, string code) =>
        document.QuerySelector($"#record-list tr.record-row[data-record-code='{code}']");

    private static string AliasOf(AngleSharp.Html.Dom.IHtmlDocument document, string code) =>
        Row(document, code)?.QuerySelector(".record-alias")?.TextContent.Trim() ?? string.Empty;

    /// <summary>
    /// How many alias changes the audit trail holds for one record code.
    /// </summary>
    private static async Task<int> AliasChangeCountAsync(IServiceProvider services, string code)
    {
        var session = services.GetRequiredService<ISession>();

        // Entries written in this scope are not queryable until the session has put them in front
        // of the database — and an entry that was never written is the thing under test.
        await session.FlushAsync();

        var entries = await session
            .Query<OrchardCore.AuditTrail.Models.AuditTrailEvent, OrchardCore.AuditTrail.Indexes.AuditTrailEventIndex>(
                index => index.Name == DimensionAuditTrail.DimensionRecordChanged,
                collection: OrchardCore.AuditTrail.Models.AuditTrailEvent.Collection)
            .ListAsync();

        var count = 0;

        foreach (var entry in entries)
        {
            if (entry.TryGet<DimensionRecordAuditEvent>(out var payload) &&
                payload.Operation == DimensionRecordOperation.AliasChanged &&
                string.Equals(payload.Code, code, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private async Task<string> GetAsync(string path)
    {
        var response = await _fixture.Administrator.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0}", path);

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<DimensionTypeDocument> TypeAsync(
        IDimensionTypeService types, string code, string nameEn)
    {
        var existing = await types.GetByCodeAsync(code);

        if (existing is not null)
        {
            return existing;
        }

        var created = await types.CreateAsync(
            code, new BilingualText(nameEn, nameEn + " (ar)"), [], allowsSelfNesting: false);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!;
    }

    private static async Task<string> UnitAsync(
        IDimensionService records,
        string structureId,
        string? parentRecordId,
        string dimensionTypeId,
        string code,
        string nameEn,
        BilingualText? alias)
    {
        var existing = await records.GetByCodeAsync(code, Opened);

        if (existing is not null)
        {
            return existing.RecordId;
        }

        var created = await records.AddUnitAsync(
            structureId, parentRecordId, dimensionTypeId, code,
            new BilingualText(nameEn, nameEn + " (ar)"), Opened,
            attributes: null, sortOrder: null, alias);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.RecordId;
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("alias and tree view test"))
            {
                await work(services);
            }
        });
}
