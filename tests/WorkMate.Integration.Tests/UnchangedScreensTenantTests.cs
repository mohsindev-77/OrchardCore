using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The two screens decisions D23, D24 and D25 all say must <b>not</b> change: the organisation
/// designer, and the "All <em>plural</em>" list behind a dimension's menu entry.
/// </summary>
/// <remarks>
/// <b>Why a test rather than a note.</b> Three briefs running have added something to the value
/// page, and each time the cheapest way to build it would have been to put a little of it on one of
/// these two screens as well — a view switch here, a chart there. Nothing stops that happening by
/// accident except an assertion that says what these screens are, so that adding to them is a
/// decision somebody has to come here and make rather than a side-effect nobody notices.
///
/// Deliberately shallow. It is not a second copy of
/// <c>OrganisationDesignerAdminTenantTests</c> or of the list screen's own tests, which cover what
/// those screens <em>do</em>; it checks only that the things the value page grew have not leaked
/// into them, and that what they already had is still there.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class UnchangedScreensTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public UnchangedScreensTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("unchanged screens test"))
            {
                await work(services);
            }
        });

    /// <summary>
    /// The "All <em>plural</em>" screen is the ordinary dimension list, with nothing of the value
    /// page on it.
    /// </summary>
    /// <remarks>
    /// D25: <em>the "All &lt;plural&gt;" screens stay exactly as they are now (no new views
    /// there)</em>. It keeps its own Tree/List toggle, which is a different control answering a
    /// different question — one dimension's values arranged two ways, rather than one value's
    /// subtree drawn three ways — and that is precisely why the two must not be confused.
    /// </remarks>
    [Fact]
    public async Task TheAllPluralScreenHasNoneOfTheValuePagesViews()
    {
        var html = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, "/Admin/Dimensions/Records/Index?group=cost%20centre");

        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelector("#record-list-heading").Should().NotBeNull("it is still the dimension list");

        foreach (var id in new[]
        {
            "value-units-views", "value-hierarchy", "value-list", "value-tree",
            "value-zoom-controls", "value-tree-controls",
        })
        {
            document.QuerySelector("#" + id).Should().BeNull($"'{id}' belongs to a value's own page");
        }

        document.QuerySelectorAll(".designer-card").Should().BeEmpty("no chart was added to the list screen");
        document.QuerySelectorAll(".value-list-sort").Should().BeEmpty("and no sortable headings");
    }

    /// <summary>
    /// The dimension list's own Tree/List toggle and its toolbar are exactly the five controls it
    /// already had.
    /// </summary>
    [Fact]
    public async Task TheAllPluralScreenKeepsItsOwnToggleAndToolbar()
    {
        var html = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, "/Admin/Dimensions/Records/Index?group=cost%20centre");

        var document = new HtmlParser().ParseDocument(html);

        foreach (var id in new[]
        {
            "record-view-tree", "record-view-list", "record-filter-toggle",
            "record-new", "record-edit-selected", "record-delete-selected", "record-refresh",
        })
        {
            document.QuerySelector("#" + id).Should().NotBeNull($"'{id}' is what this screen already had");
        }
    }

    /// <summary>
    /// The four Position columns appear on no other dimension's list. Decisions D25 and D26 E.
    /// </summary>
    /// <remarks>
    /// <b>The one place slice F5+F7 was allowed to add to an "All <em>plural</em>" screen, and the
    /// bound on it.</b> A position has an approved headcount, a filled count, a vacancy and a set of
    /// grades; a cost centre has none of those, and four empty columns on every list would be
    /// exactly the change D25 rules out. So the columns are conditioned on the dimension's declared
    /// role, and this asserts the condition rather than the feature — which is the half that can be
    /// lost by somebody simplifying an <c>@if</c>.
    ///
    /// Asserted on <b>Cost centre</b>, which the base recipe ships on every tenant, so this runs
    /// without needing a position to exist at all.
    /// </remarks>
    [Fact]
    public async Task ThePositionColumnsAppearOnNoOtherDimensionsList()
    {
        var html = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, "/Admin/Dimensions/Records/Index?group=cost%20centre");

        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelector("#record-list-heading").Should().NotBeNull("it is still the dimension list");

        foreach (var cell in new[] { "record-approved", "record-filled", "record-vacant", "record-allowed-grades" })
        {
            document.QuerySelectorAll("." + cell).Should().BeEmpty(
                "'{0}' belongs to the Positions list, and a cost centre has no establishment", cell);
        }

        var headings = document.QuerySelectorAll("#record-list thead th")
            .Select(heading => heading.TextContent.Trim())
            .ToList();

        headings.Should().NotContain("Approved");
        headings.Should().NotContain("Allowed grades");
    }

    /// <summary>
    /// The organisation designer still renders editable cards with their action menus.
    /// </summary>
    /// <remarks>
    /// The value page's hierarchy reuses the designer's <em>layout</em> — the same
    /// <c>.designer-node</c> markup, so the same connector lines and the same RTL mirroring — and
    /// reuses none of its behaviour. This is the half of that bargain that could be broken without
    /// anybody noticing: a change made for the read-only chart that quietly removed the designer's
    /// own ⋯ menu would leave both screens rendering and one of them unusable.
    /// </remarks>
    [Fact]
    public async Task TheDesignerStillRendersItsEditableCards()
    {
        var structureId = await GivenAStructureAsync();

        var html = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        var document = new HtmlParser().ParseDocument(html);

        document.QuerySelector(".designer-surface").Should().NotBeNull();
        document.QuerySelector("#designer-tree").Should().NotBeNull();

        document.QuerySelectorAll(".designer-actions").Should().NotBeEmpty(
            "the designer's cards carry the action menu the value page's chart deliberately has none of");

        document.QuerySelectorAll("[data-designer-action]")
            .Select(element => element.GetAttribute("data-designer-action"))
            .Distinct()
            .Should().Contain(["add", "rename", "retire"], "editing lives here");

        // And its own zoom controls, which are the designer's and not the value page's.
        document.QuerySelector("#designer-zoom-controls").Should().NotBeNull();
        document.QuerySelector("#value-zoom-controls").Should().BeNull();
    }

    /// <summary>
    /// The designer's card actions are the nine it has always had, by name.
    /// </summary>
    /// <remarks>
    /// Declared once in <c>DesignerCardActions.All</c> and read by the surface's URL map, the card
    /// partial's menu and the script that points a fetched card's links at the right record.
    /// Asserting the list is what makes "unchanged" a statement rather than a hope: a slice that
    /// adds one has to come here and say so.
    /// </remarks>
    [Fact]
    public void TheDesignerCardActionsAreStillTheSameNine() =>
        DesignerCardActions.All.Select(action => action.Name).Should().Equal(
            ["add", "rename", "move", "cancelmove", "merge", "sethead", "placeemployee", "viewemployees", "retire"]);

    private async Task<string> GivenAStructureAsync()
    {
        var structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var type = await types.CreateAsync(
                "unchanged-division", new BilingualText("Unchanged Division", "قطاع"), [], allowsSelfNesting: false);

            type.Succeeded.Should().BeTrue(string.Join("; ", type.Errors.Select(e => e.Message.Value)));

            var structure = await structures.CreateAsync(
                "unchanged-org",
                new BilingualText("Unchanged organisation", "منظمة"),
                [type.Value!.DimensionTypeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false);

            structure.Succeeded.Should().BeTrue(string.Join("; ", structure.Errors.Select(e => e.Message.Value)));
            structureId = structure.Value!.StructureId;

            var root = await records.AddUnitAsync(
                structureId, null, type.Value.DimensionTypeId, "unchanged-div",
                new BilingualText("Unchanged One", "قطاع واحد"), new DateOnly(2024, 1, 1));

            root.Succeeded.Should().BeTrue(string.Join("; ", root.Errors.Select(e => e.Message.Value)));
        });

        return structureId;
    }
}
