using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// "Place an employee here…" on a designer card: where it appears, where it must not, and that a
/// fetched card decides it the same way a server-rendered one does.
/// </summary>
/// <remarks>
/// <b>Two independent conditions, and the second is the one that is easy to get wrong.</b> Whether
/// the viewer may place anybody is a permission, so it is the same on every card on the page.
/// Whether <em>this</em> unit may hold employees is the structure's
/// <c>employeeAttachableTypeCodes</c> read forwards, so it differs from card to card — and a card
/// the browser builds from a cloned template cannot branch on it in Razor. The node carries the
/// answer and the link declares that it needs it, which is what keeps the two drawings of a menu
/// from diverging. <c>OrganisationDesignerViewModel.PermittedActions</c> records what that
/// divergence cost the last time.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DesignerPlaceEmployeeActionTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DesignerPlaceEmployeeActionTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// A structure that places employees on departments and not on divisions, with one of each.
    /// </summary>
    private async Task<(string StructureId, string Division, string Department)> ConstrainedAsync(string code)
    {
        string structureId = null!, division = null!, department = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);

            var created = await services.GetRequiredService<IStructureService>().CreateAsync(
                code,
                new BilingualText(code, $"{code}-ar"),
                [types.Division, types.Department],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [types.Division],
                    [new StructureContainment(types.Division, types.Department)],
                    [types.Department]));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            structureId = created.Value!.StructureId;

            division = await DimensionGraphScenario.RecordAsync(
                services, types.Division, $"{code}-div", Opened);

            department = await DimensionGraphScenario.RecordAsync(
                services, types.Department, $"{code}-dept", Opened);

            (await services.GetRequiredService<IDimensionGraphService>()
                .PlaceAsync(structureId, department, division, Opened))
                .Succeeded.Should().BeTrue();
        });

        return (structureId, division, department);
    }

    /// <summary>
    /// A division's card does not offer it; the department's, fetched beneath it, does.
    /// </summary>
    /// <remarks>
    /// Both halves in one test on purpose. "The action is never drawn" and "the action is always
    /// drawn" each pass one of these assertions, and the thing being tested is the difference
    /// between the two cards.
    /// </remarks>
    [Fact]
    public async Task TheActionIsOfferedOnAUnitThatHoldsEmployeesAndNotOnAContainer()
    {
        var (structureId, division, _) = await ConstrainedAsync("place-action-constrained");

        var chart = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        // The division is a root, so the server rendered its card. It is a container on this
        // structure, so the entry is absent — not hidden with CSS, absent.
        CardFor(chart, division).Should().NotContain(
            "data-designer-action=\"placeemployee\"",
            "a menu entry that opens a form the validator would refuse is worse than no entry");

        CardFor(chart, division).Should().Contain(
            "data-designer-action=\"sethead\"",
            "a container still has a head, which is the distinction ADR-0012 draws");

        // The department is fetched when the branch is opened, so what the browser gets is the
        // node's own answer.
        var children = await _fixture.Administrator.GetStringAsync(
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={division}");

        using var document = JsonDocument.Parse(children);

        var child = document.RootElement.EnumerateArray().Single();

        child.GetProperty("holdsEmployees").GetBoolean().Should().BeTrue(
            "a department on this structure holds employees, and the card has to be told so");
    }

    /// <summary>
    /// The template every fetched card is cloned from carries the link, and declares its condition.
    /// </summary>
    /// <remarks>
    /// This is the defect that cost "Appoint a head…" a whole session: the template was rendered
    /// from a model whose permission flag defaulted to false, so it carried no link at all and the
    /// script had nothing to point anywhere. A template rendered without this link would leave the
    /// roots correct and every card below them silently short an entry.
    /// </remarks>
    [Fact]
    public async Task TheTemplateCarriesTheLinkAndTheConditionTheScriptHonours()
    {
        var (structureId, _, _) = await ConstrainedAsync("place-action-template");

        var chart = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        var start = chart.IndexOf("<template", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1, "the page carries the template the script clones");

        var template = chart[start..chart.IndexOf("</template>", start, StringComparison.Ordinal)];

        template.Should().Contain("data-designer-action=\"placeemployee\"");

        template.Should().Contain(
            "data-designer-action-requires=\"holdsEmployees\"",
            "the link names what it needs, so the script drops it on a container rather than "
            + "needing to know which actions are conditional");
    }

    /// <summary>
    /// The action is in the URL map the script points fetched cards at, pointing into Records.
    /// </summary>
    /// <remarks>
    /// An Orchard module is an MVC area, so a controller in another module resolves to nothing
    /// without being named — silently: <c>Url.Action</c> returns null, the script drops the entry,
    /// and it reads exactly like a permission the viewer does not have.
    /// </remarks>
    [Fact]
    public async Task TheActionIsInTheUrlMapAndPointsAtTheRecordsModule()
    {
        var (structureId, _, _) = await ConstrainedAsync("place-action-urls");

        var chart = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        var map = Between(chart, "data-action-urls=\"", "\"");

        using var document = JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(map));

        document.RootElement.TryGetProperty("placeemployee", out var action).Should().BeTrue(
            "the script removes any action it has no URL for");

        action.GetProperty("url").GetString().Should().Contain(
            "/Employees/Placement/Place",
            "which is where WorkMate.Records routes the placement screen");
    }

    /// <summary>
    /// It is declared next to "Appoint a head…" and under the same permission.
    /// </summary>
    /// <remarks>
    /// Asserted on the list itself rather than on a rendered page, because "the same single source"
    /// is a claim about the declaration: both entries come from <c>DesignerCardActions.All</c> and
    /// both are gated on <c>CanAssignEmployees</c>, so a change to who may place somebody cannot
    /// move one without the other.
    /// </remarks>
    [Fact]
    public void ItIsDeclaredBesideAppointAHeadAndGatedOnTheSamePermission()
    {
        var names = DesignerCardActions.All.Select(action => action.Name).ToList();
        var place = DesignerCardActions.All.Single(action => action.Name == "placeemployee");

        names.IndexOf("placeemployee").Should().Be(
            names.IndexOf("sethead") + 1, "the two are the same act and read together in the menu");

        var mayAssign = new OrganisationDesignerViewModel { CanAssignEmployees = true };
        var mayNot = new OrganisationDesignerViewModel { CanAssignEmployees = false };

        place.IsPermitted(mayAssign).Should().BeTrue();
        place.IsPermitted(mayNot).Should().BeFalse();

        mayNot.PermittedActions.Should().NotContain(action => action.Name == "placeemployee");
    }

    /// <summary>
    /// On a structure that constrains nothing, every unit may hold employees and every card offers
    /// it.
    /// </summary>
    /// <remarks>
    /// Empty means unconstrained, not "nothing may hold employees" — which is the state every
    /// tenant is in before somebody ticks the boxes, and the demo tenants included. A card that
    /// withheld the action until the constraint was expressed would be a feature nobody ever saw
    /// working.
    /// </remarks>
    [Fact]
    public async Task OnAStructureThatConstrainsNothingEveryCardOffersIt()
    {
        string structureId = null!, division = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            structureId = await DimensionGraphScenario.StructureAsync(services, "place-action-open");

            var types = await DimensionGraphScenario.TypesAsync(services);

            division = await DimensionGraphScenario.RecordAsync(
                services, types.Division, "place-action-open-div", Opened);
        });

        var chart = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        CardFor(chart, division).Should().Contain("data-designer-action=\"placeemployee\"");
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>One card's markup, so an assertion about a unit is about that unit.</summary>
    private static string CardFor(string html, string recordId)
    {
        var start = html.IndexOf($"data-record-id=\"{recordId}\"", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1, "'{0}' is drawn on this chart", recordId);

        var end = html.IndexOf("designer-card-body", start, StringComparison.Ordinal);

        return html[start..(end < 0 ? html.Length : end)];
    }

    private static string Between(string html, string opening, string closing)
    {
        var start = html.IndexOf(opening, StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1, "the page carries '{0}'", opening);

        start += opening.Length;

        var end = html.IndexOf(closing, start, StringComparison.Ordinal);

        return html[start..(end < 0 ? html.Length : end)];
    }
}
