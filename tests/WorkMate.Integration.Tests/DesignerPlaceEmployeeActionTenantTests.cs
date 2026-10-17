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

    /// <summary>
    /// On an axis that has positions, a department's card stops offering it and a position's card
    /// offers it. Decision D30, slice F6b.
    /// </summary>
    /// <remarks>
    /// <b>The defect this was written for.</b> The card asked
    /// <c>StructureDocument.PermitsEmployeesAt</c>, which knows the tenant's attachable set and
    /// nothing about what a Position is. ADR-0029 deliberately <em>keeps</em> the department types
    /// in that set so existing placements stay valid — so the department's card went on offering
    /// <em>Place an employee here…</em>, the form opened, and the write came back <em>"choose a
    /// position instead"</em>. An action that cannot do what it says is worse than no action, which
    /// is the same judgement <see cref="TheActionIsOfferedOnAUnitThatHoldsEmployeesAndNotOnAContainer"/>
    /// already makes about a container.
    ///
    /// Both halves in one test, for that test's own reason: "never drawn" and "always drawn" each
    /// pass one assertion, and what is being tested is the difference between the two cards.
    ///
    /// The rule is read once, through <c>IPositionService.PermitsEmployeesAtAsync</c> — the same
    /// answer the picker and the validator use, so the three cannot disagree.
    /// </remarks>
    [Fact]
    public async Task OnAnAxisWithPositionsOnlyAPositionsCardOffersIt()
    {
        string structureId = null!, division = null!, department = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);

            // A Position level alongside the department one, which is what makes the axis
            // position-based — asked of its shape, never of a code (ADR-0030).
            var typeService = services.GetRequiredService<IDimensionTypeService>();

            var positionType = (await typeService.GetByCodeAsync("place-action-d30-pos"))?.DimensionTypeId;

            if (positionType is null)
            {
                var madeType = await typeService.CreateAsync(
                    "place-action-d30-pos",
                    new BilingualText("Post", "منصب"),
                    [],
                    allowsSelfNesting: false,
                    standardRole: DimensionTypeRole.Position);

                madeType.Succeeded.Should().BeTrue(
                    string.Join("; ", madeType.Errors.Select(error => error.Message.Value)));

                positionType = madeType.Value!.DimensionTypeId;
            }

            var created = await services.GetRequiredService<IStructureService>().CreateAsync(
                "place-action-d30",
                new BilingualText("place-action-d30", "place-action-d30-ar"),
                [types.Division, types.Department, positionType],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [types.Division],
                    [
                        new StructureContainment(types.Division, types.Department),
                        new StructureContainment(types.Department, positionType),
                    ],
                    // BOTH are attachable, which is exactly the state ADR-0029 leaves a tenant in
                    // after the F6 migration. D30 is what narrows it, not this set.
                    [types.Department, positionType]));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            structureId = created.Value!.StructureId;

            var graph = services.GetRequiredService<IDimensionGraphService>();

            division = await DimensionGraphScenario.RecordAsync(
                services, types.Division, "place-action-d30-div", Opened);

            (await graph.PlaceAsync(structureId, division, null, Opened))
                .Succeeded.Should().BeTrue();

            department = await DimensionGraphScenario.RecordAsync(
                services, types.Department, "place-action-d30-dept", Opened);

            (await graph.PlaceAsync(structureId, department, division, Opened))
                .Succeeded.Should().BeTrue();

            var position = await DimensionGraphScenario.RecordAsync(
                services, positionType, "place-action-d30-post", Opened);

            (await graph.PlaceAsync(structureId, position, department, Opened))
                .Succeeded.Should().BeTrue();
        });

        // Both cards are fetched rather than server-rendered — they sit below the root — so the
        // assertion is on the node's own answer, which is what the script builds the menu from.
        // The same property TheActionIsOfferedOnAUnitThatHoldsEmployeesAndNotOnAContainer reads.
        var underTheDivision = await _fixture.Administrator.GetStringAsync(
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={division}");

        using var departments = JsonDocument.Parse(underTheDivision);

        departments.RootElement.EnumerateArray().Single()
            .GetProperty("holdsEmployees").GetBoolean().Should().BeFalse(
                "D30: on an axis that has positions a department is not somewhere an employee goes, "
                + "even though ADR-0029 keeps it in the attachable set");

        var underTheDepartment = await _fixture.Administrator.GetStringAsync(
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={department}");

        using var positions = JsonDocument.Parse(underTheDepartment);

        positions.RootElement.EnumerateArray().Single()
            .GetProperty("holdsEmployees").GetBoolean().Should().BeTrue(
                "and the post is where they go instead");
    }

    // ---- D30, where the rule is applied: a structure that is not a place at all ----------------

    /// <summary>
    /// No card on a reporting line offers <em>Place an employee here…</em>, and no card on a grade
    /// ladder does either. Decision D30, "where the rule is applied".
    /// </summary>
    /// <remarks>
    /// <b>The reporting line is the one that got through, and it got through by being right about
    /// everything else.</b> Every node on it IS a position, so D30's own test — "on an axis with
    /// positions, is this node a position?" — answered yes for every card, and the menu offered the
    /// action on the Zenith reporting chart. The old Place screen then opened, headed <em>"Place an
    /// employee in CEO › Director Technology › Manager Technology"</em>, with an allocation
    /// percentage and a <em>main placement</em> checkbox, for an axis ADR-0028 makes a post's
    /// answer-to chain rather than anywhere somebody sits.
    ///
    /// <b>The grade ladder got there by the opposite door</b>: no Position level at all, so the
    /// rule said nothing about it and the attachable set — unconstrained, as a structure written
    /// before any of this is — said yes.
    ///
    /// Both are now one question asked of the axis's shape, never of its code: a tenant may rename
    /// anything, and a rule written against the word "reporting" would hold on the demo data and
    /// nowhere else.
    /// </remarks>
    [Theory]
    [InlineData(DimensionTypeRole.Position, "place-action-reporting")]
    [InlineData(DimensionTypeRole.Grade, "place-action-ladder")]
    public async Task NoCardOnAReportingLineOrAGradeLadderOffersIt(
        DimensionTypeRole role,
        string code)
    {
        string structureId = null!, root = null!, child = null!;

        await _fixture.InTenantAsSystemAsync(async services =>
        {
            var typeService = services.GetRequiredService<IDimensionTypeService>();
            var typeCode = $"{code}-type";

            var typeId = (await typeService.GetByCodeAsync(typeCode))?.DimensionTypeId;

            if (typeId is null)
            {
                // Self-nesting, because that is the shape both really have: a reporting line is
                // positions under positions, and a grade ladder is grades under grades.
                var madeType = await typeService.CreateAsync(
                    typeCode,
                    new BilingualText(typeCode, typeCode),
                    [],
                    allowsSelfNesting: true,
                    standardRole: role);

                madeType.Succeeded.Should().BeTrue(
                    string.Join("; ", madeType.Errors.Select(error => error.Message.Value)));

                typeId = madeType.Value!.DimensionTypeId;
            }

            var created = await services.GetRequiredService<IStructureService>().CreateAsync(
                code,
                new BilingualText(code, $"{code}-ar"),
                [typeId],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [typeId],
                    [new StructureContainment(typeId, typeId)],

                    // ATTACHABLE, deliberately. This is the state an unconstrained structure is in
                    // and the reason the attachable set could never answer this on its own: it is
                    // a statement about which LEVEL holds staff, and it presumes the axis is a
                    // place. Both structures here say yes to that question and are still not one.
                    [typeId]));

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            structureId = created.Value!.StructureId;

            var graph = services.GetRequiredService<IDimensionGraphService>();

            root = await DimensionGraphScenario.RecordAsync(services, typeId, $"{code}-top", Opened);

            (await graph.PlaceAsync(structureId, root, null, Opened)).Succeeded.Should().BeTrue();

            child = await DimensionGraphScenario.RecordAsync(services, typeId, $"{code}-under", Opened);

            (await graph.PlaceAsync(structureId, child, root, Opened)).Succeeded.Should().BeTrue();
        });

        // The root's card is server-rendered, so the entry is absent from the markup itself.
        var chart = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        CardFor(chart, root).Should().NotContain(
            "data-designer-action=\"placeemployee\"",
            "'{0}' is not somewhere an employee goes, so the menu does not offer to put one there",
            code);

        CardFor(chart, root).Should().Contain(
            "data-designer-action=\"sethead\"",
            "the rest of the card menu is untouched — the designer's layout does not change");

        // And the card below it is fetched, so what the browser is told must agree.
        var children = await _fixture.Administrator.GetStringAsync(
            $"/Admin/Dimensions/Designer/Children?structureId={structureId}&recordId={root}");

        using var document = JsonDocument.Parse(children);

        document.RootElement.EnumerateArray().Single()
            .GetProperty("holdsEmployees").GetBoolean().Should().BeFalse(
                "a fetched card decides it the same way a server-rendered one does, or the two "
                + "drawings of one menu diverge");
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
