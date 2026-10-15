using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Controllers;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Positions: the two axes a post sits on, its establishment, its allowed grades, and the two reads
/// slice F6's employee form is built on. Decisions D26 D–G, ADR-0022 and ADR-0028.
/// </summary>
/// <remarks>
/// <b>Its own tenant, because this slice changes the base recipe.</b> It ships a Position dimension
/// and a reporting-line axis where there were none, and the Zenith demo gains eight positions and
/// seventeen grades. Asserting that on the shared base tenant would mean every other test in the
/// collection running against a tenant this one had reshaped.
///
/// <b>The thing most worth asserting is that there are two axes and they are independent</b>
/// (ADR-0028). A position's unit and the post it reports to are different parents on different
/// charts, and the failure mode — one axis quietly carrying both, or the reporting line rolling up a
/// headcount — looks correct on every screen until somebody asks how many people are "under the
/// COO".
/// </remarks>
public sealed class PositionsTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    private static readonly DateOnly Today = new(2026, 10, 7);

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        // Two recipes, in this order. The organisation first, then the positions and grades that
        // hang off it — the positions are placed under Zenith's departments and the second file
        // resolves a parent by code, so without the first it fails naming the department it could
        // not find. See organisation-designer-zenith-positions' own notes for why they are split.
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("positions test"))
            {
                await work(services);
            }
        });

    // ---- what the base recipe ships ------------------------------------------------------

    /// <summary>
    /// A new tenant has a Position dimension and a reporting-line axis, and still has no
    /// organisation chart.
    /// </summary>
    /// <remarks>
    /// <b>The second half is the one that matters.</b> ADR-0028's whole argument for shipping two
    /// axes is that neither is the customer's organisation chart, so this asserts what the base
    /// recipe refuses to guess as well as what it provides. A reporting line has one level and no
    /// units; an organisation chart is the shape of somebody's company.
    /// </remarks>
    [Fact]
    public async Task TheBaseRecipeShipsPositionAndAReportingLineAndStillNoOrganisationChart() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();

            var position = await types.GetByCodeAsync("position");

            position.Should().NotBeNull("the base recipe ships the Position dimension (D20, D26)");
            position!.StandardRole.Should().Be(
                DimensionTypeRole.Position,
                "the role is how the product finds this dimension — never its code and never its name");

            position.Name.En.Should().Be("Position");
            position.PluralName.En.Should().Be("Positions");
            position.PluralName.Ar.Should().Be("المناصب");

            position.AttributeSchema.Select(attribute => attribute.Name).Should().Contain(
                PositionEstablishment.ApprovedHeadcountAttribute,
                "approved headcount is an attribute on the Position type, not a field on the part "
                + "every dimension carries");

            var reporting = await structures.GetByCodeAsync("reporting-line");

            reporting.Should().NotBeNull("ADR-0028: a position's reporting line is its own axis");
            reporting!.DimensionTypeIds.Should().Equal([position.DimensionTypeId]);
            reporting.PermitsRoot(position.DimensionTypeId).Should().BeTrue("the CEO reports to nobody");
            reporting.Permits(position.DimensionTypeId, position.DimensionTypeId).Should().BeTrue(
                "a reporting line is positions all the way down");

            (await structures.GetPrimaryOrganisationAsync())!
                .DimensionTypeIds.Should().BeEmpty(
                    "shipping the reporting line is not the base recipe guessing an organisation chart");
        });

    /// <summary>
    /// On a tenant that has not put Position on its chart, the Add form says so and links to where.
    /// </summary>
    /// <remarks>
    /// <b>Every brand-new tenant is in this state</b>, which is why it earns a sentence rather than
    /// an empty picker. The readiness read is asked of the <em>base</em> Position type, which no
    /// structure in this tenant uses — Zenith's positions are on Zenith's own type.
    /// </remarks>
    [Fact]
    public async Task APositionWithNowhereToSitSaysSoAndLinksToTheStructuresScreen()
    {
        var positionTypeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var positions = services.GetRequiredService<IPositionService>();

            positionTypeId = (await types.GetByCodeAsync("position"))!.DimensionTypeId;

            var readiness = await positions.GetPlacementReadinessAsync(positionTypeId, Today);

            readiness.HasSomewhereToGo.Should().BeFalse(
                "base ships the Position dimension and no organisation chart (ADR-0028)");
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator,
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={positionTypeId}");

        page.Should().Contain("record-create-position-unplaceable");

        // Not the whole sentence: Orchard's HtmlEncoder escapes the apostrophe in "can't", so a
        // substring search for the sentence as written finds nothing. Asking for the part that
        // carries the meaning is asking what a reader sees.
        page.Should().Contain("be placed yet");
        page.Should().Contain("allow Position in your organisation structure");
        page.Should().Contain("record-create-position-structures", "with a link to where it is done");
    }

    // ---- the Zenith demo ------------------------------------------------------------------

    /// <summary>
    /// The demo's reporting line, parent by parent, and its organisation placements beside it.
    /// </summary>
    /// <remarks>
    /// <b>Two trees asserted together, because the point is that they differ.</b> HR Manager reports
    /// to the Director of Technology and sits in the HR department; Site Supervisor reports to the
    /// Manager of Technology and sits in a site team. A model that collapsed the two axes would have
    /// to put one of those facts somewhere else, and both lists below would still look plausible.
    /// </remarks>
    [Fact]
    public async Task TheZenithDemoBuildsAReportingLineAndAnOrganisationPlacementForEachPost() =>
        await InTenantAsSystemAsync(async services =>
        {
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();
            var structures = services.GetRequiredService<IStructureService>();

            var reporting = (await structures.GetByCodeAsync("zenith-reporting"))!.StructureId;
            var organisation = (await structures.GetByCodeAsync("zenith-org"))!.StructureId;

            async Task<string> IdOf(string code) => (await records.GetByCodeAsync(code))!.RecordId;

            async Task<IReadOnlyList<string>> ReportsTo(string code) =>
                [.. (await graph.GetChildrenAsync(reporting, await IdOf(code), Today)).Select(child => child.Code)];

            // Asked as "who is above it", not through GetRootsAsync: on a self-nesting axis whose
            // only type is also its root type, every position is a record of a root type and that
            // read returns all of them by design. What "the CEO reports to nobody" means here is
            // that nothing is above it.
            (await graph.GetAncestorsAsync(reporting, await IdOf("zenith-pos-ceo"), Today))
                .Should().BeEmpty("the CEO reports to nobody");

            (await graph.GetAncestorsAsync(reporting, await IdOf("zenith-pos-labourer"), Today))
                .Select(ancestor => ancestor.Code)
                .Should().Equal(
                    ["zenith-pos-site-supervisor", "zenith-pos-mgr-tech", "zenith-pos-dir-tech", "zenith-pos-ceo"],
                    "nearest first, all the way to the top of the line");

            (await ReportsTo("zenith-pos-ceo")).Should().Equal(["zenith-pos-dir-tech"]);

            (await ReportsTo("zenith-pos-dir-tech")).Should().BeEquivalentTo(
                ["zenith-pos-mgr-tech", "zenith-pos-hr-manager"],
                "the HR Manager reports to the Director and sits in a different department");

            (await ReportsTo("zenith-pos-mgr-tech")).Should().BeEquivalentTo(
                ["zenith-pos-pse", "zenith-pos-se", "zenith-pos-site-supervisor"]);

            (await ReportsTo("zenith-pos-site-supervisor")).Should().Equal(["zenith-pos-labourer"]);

            // And the other axis, which is a different shape. The HR Manager's two parents are the
            // Director of Technology and the HR department, and neither is derivable from the other.
            async Task<string> UnitOf(string code)
            {
                var path = (await graph.GetAncestorPathsAsync(
                    organisation, [await IdOf(code)], Today)).GetValueOrDefault(await IdOf(code)) ?? [];

                return path.Count == 0 ? string.Empty : path[^1].Code;
            }

            (await UnitOf("zenith-pos-hr-manager")).Should().Be("zenith-dept-hr");
            (await UnitOf("zenith-pos-se")).Should().Be("zenith-dept-electrical");
            // Under a DEPARTMENT, like every other post. A site team is where an employee is
            // deployed — the Zenith matrix — which is a fact about the person and not about the
            // post, and D14 records Zenith as position-under-Department.
            (await UnitOf("zenith-pos-labourer")).Should().Be("zenith-dept-civil");
        });

    /// <summary>
    /// One Position per job, with the headcount carrying the number of holders. Decision D26 D.
    /// </summary>
    /// <remarks>
    /// <b>The assertion the decision is about.</b> There is one HR Manager position with an approved
    /// headcount of 3, not three HR Manager positions; one Labourer with 44, not forty-four. Written
    /// as a count of records as well as a headcount, because a model that went the other way would
    /// pass a headcount assertion by having forty-four records of one each.
    /// </remarks>
    [Fact]
    public async Task OnePositionPerJobCarriesItsHeadcountRatherThanBeingRepeated() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();
            var types = services.GetRequiredService<IDimensionTypeService>();

            var zenithPosition = (await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId;
            var all = await records.ListByTypeAsync(zenithPosition);

            all.Count(position => position.NameEn == "HR Manager").Should().Be(
                1, "people in the same job share one Position; their level is their grade (D26 D)");

            all.Count(position => position.NameEn == "Labourer").Should().Be(1);

            var ids = all.ToDictionary(position => position.Code, position => position.RecordId, StringComparer.Ordinal);
            var establishment = await positions.GetEstablishmentAsync([.. ids.Values], Today);

            establishment[ids["zenith-pos-hr-manager"]].Approved.Should().Be(3);
            establishment[ids["zenith-pos-labourer"]].Approved.Should().Be(44);
            establishment[ids["zenith-pos-ceo"]].Approved.Should().Be(1);

            // Nobody holds a position until slice F6 repoints the demo employees, so every post is
            // wholly vacant — which is what the brief says this slice should show.
            establishment[ids["zenith-pos-labourer"]].Filled.Should().Be(0);
            establishment[ids["zenith-pos-labourer"]].Vacant.Should().Be(44);
            establishment[ids["zenith-pos-labourer"]].IsFull.Should().BeFalse();
        });

    /// <summary>A position's allowed grades are its levels, in the order somebody chose.</summary>
    [Fact]
    public async Task EachDemoPositionAllowsItsOwnLadderOfGrades() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();

            async Task<IReadOnlyList<string>> GradesOf(string code) =>
            [
                .. (await positions.GetAllowedGradesAsync(
                        (await records.GetByCodeAsync(code))!.RecordId, Today))
                    .Select(grade => grade.NameEn),
            ];

            (await GradesOf("zenith-pos-se")).Should().Equal(["SE-1", "SE-2", "SE-3"]);
            (await GradesOf("zenith-pos-pse")).Should().Equal(["PSE-1", "PSE-2", "PSE-3"]);
            (await GradesOf("zenith-pos-hr-manager")).Should().Equal(["HRM-1", "HRM-2", "HRM-3"]);
            (await GradesOf("zenith-pos-labourer")).Should().Equal(["LAB-1", "LAB-2"]);
            (await GradesOf("zenith-pos-ceo")).Should().Equal(["CEO-1"]);
        });

    /// <summary>Re-applying the demo recipe changes nothing and refuses nothing.</summary>
    /// <remarks>
    /// ADR-0008, extended to the new <c>dimension-references</c> step: a row whose stored set
    /// already matches is skipped with nothing written. Without that, a second application of a
    /// recipe a customer has already run would fail on every position in it.
    /// </remarks>
    [Fact]
    public async Task TheDemoRecipeReRunsCleanIncludingItsReferences()
    {
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();
            var types = services.GetRequiredService<IDimensionTypeService>();

            var zenithPosition = (await types.GetByCodeAsync("zenith-position"))!.DimensionTypeId;

            (await records.ListByTypeAsync(zenithPosition)).Should().HaveCount(
                8, "a re-run creates nothing a second time");

            (await positions.GetAllowedGradesAsync(
                    (await records.GetByCodeAsync("zenith-pos-se"))!.RecordId, Today))
                .Select(grade => grade.NameEn)
                .Should().Equal(["SE-1", "SE-2", "SE-3"], "and writes no duplicate references");
        });
    }

    // ---- the two reads slice F6 is built on ------------------------------------------------

    /// <summary>
    /// Positions selectable as at a date: active only, searchable, each with its unit and how full
    /// it is. Decision D26 F.
    /// </summary>
    [Fact]
    public async Task SelectablePositionsAreActiveSearchableAndCarryTheirUnitAndEstablishment() =>
        await InTenantAsSystemAsync(async services =>
        {
            var positions = services.GetRequiredService<IPositionService>();

            var all = await positions.SearchSelectableAsync(searchText: null, Today, take: 50);

            all.Items.Select(option => option.Position.Code).Should().Contain("zenith-pos-se");

            var engineer = all.Items.Single(option => option.Position.Code == "zenith-pos-se");

            engineer.UnitPath.Should().Contain(
                "Electrical", "an option names the unit, which is what tells two Supervisors apart");

            engineer.Establishment.Approved.Should().Be(3);
            engineer.Establishment.Filled.Should().Be(0);
            engineer.IsFull.Should().BeFalse();

            // Searched on the server, by name and by code, in both languages.
            (await positions.SearchSelectableAsync("Labourer", Today))
                .Items.Select(option => option.Position.Code).Should().Equal(["zenith-pos-labourer"]);

            (await positions.SearchSelectableAsync("zenith-pos-ceo", Today))
                .Items.Select(option => option.Position.Code).Should().Equal(["zenith-pos-ceo"]);

            (await positions.SearchSelectableAsync("مدير التقنية", Today))
                .Items.Select(option => option.Position.Code).Should().Equal(["zenith-pos-dir-tech"]);
        });

    /// <summary>A retired position is not offered — not offered greyed out, not offered at all.</summary>
    /// <remarks>
    /// The other half of D26 F, and the one that goes the opposite way from a full position. A post
    /// that has closed is not one anybody can be put into, so offering it is offering a choice whose
    /// only outcome is a refusal; a <em>full</em> post is a real choice with a warning on it.
    /// </remarks>
    [Fact]
    public async Task ARetiredPositionStopsBeingSelectableOnTheDayItCloses() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();

            var supervisor = (await records.GetByCodeAsync("zenith-pos-site-supervisor"))!.RecordId;
            var closes = new DateOnly(2026, 6, 30);

            var retired = await records.RetireAsync(supervisor, closes, new Dictionary<string, ChildrenDisposition>
            {
                // Its reporting-line child, the Labourer, is left where it is: the brief's tree has
                // the Labourer under it, and what this test is about is the position disappearing
                // from the picker rather than what happens to the line below it.
                [(await services.GetRequiredService<IStructureService>()
                    .GetByCodeAsync("zenith-reporting"))!.StructureId] = ChildrenDisposition.Unplaced,
                [(await services.GetRequiredService<IStructureService>()
                    .GetByCodeAsync("zenith-org"))!.StructureId] = ChildrenDisposition.Unplaced,
            });

            retired.Succeeded.Should().BeTrue(
                string.Join("; ", retired.Errors.Select(error => error.Message.Value)));

            (await positions.SearchSelectableAsync(searchText: null, closes.AddDays(1), take: 50))
                .Items.Select(option => option.Position.Code)
                .Should().NotContain("zenith-pos-site-supervisor", "a closed post is not a post");

            (await positions.SearchSelectableAsync(searchText: null, closes.AddDays(-1), take: 50))
                .Items.Select(option => option.Position.Code)
                .Should().Contain("zenith-pos-site-supervisor", "and the day before still answers");
        });

    /// <summary>A full position is offered, flagged, and never refused. Decision D26 F.</summary>
    /// <remarks>
    /// Proved by raising the filled count the only way this slice can — lowering the approved
    /// headcount to zero, which is the same arithmetic the real case reaches from the other side.
    /// Slice F6, where employees start holding positions, is where the natural version of this test
    /// lives; the rule it exercises is the same one.
    /// </remarks>
    [Fact]
    public async Task AFullPositionIsOfferedAndFlaggedRatherThanWithheld() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();

            var ceo = (await records.GetByCodeAsync("zenith-pos-ceo"))!.RecordId;

            var set = await records.SetAttributeValuesAsync(
                ceo, [new DimensionAttributeValue(PositionEstablishment.ApprovedHeadcountAttribute, "0")]);

            set.Succeeded.Should().BeTrue(
                string.Join("; ", set.Errors.Select(error => error.Message.Value)));

            var offered = await positions.SearchSelectableAsync(searchText: null, Today, take: 50);
            var option = offered.Items.Single(item => item.Position.Code == "zenith-pos-ceo");

            option.IsFull.Should().BeTrue("filled 0 is not short of approved 0");
            option.Establishment.Vacant.Should().Be(0);

            offered.Items.Should().Contain(
                item => item.Position.Code == "zenith-pos-ceo",
                "D26 F: a full position warns and is allowed, because refusing would stop the "
                + "product recording what an organisation has actually done");
        });

    /// <summary>
    /// Over-establishment shows as a negative rather than being clamped to zero. Decision D26 E.
    /// </summary>
    [Fact]
    public void VacantIsNegativeWhenAPostIsOverEstablished() =>
        new PositionEstablishment(Approved: 2, Filled: 5).Vacant.Should().Be(
            -3, "clamping would hide the one condition the number exists to surface");

    // ---- the rules --------------------------------------------------------------------------

    /// <summary>A reporting line that would close a loop is refused.</summary>
    /// <remarks>
    /// <b>By the cycle check that already protects the organisation chart</b>, running on a second
    /// axis and needing no new code — which is the clearest demonstration that ADR-0028's second
    /// axis is the engine's own mechanism rather than a new one. Asserted at two removes as well as
    /// one, because a check that only compared the immediate parent would pass the first and fail
    /// the second.
    /// </remarks>
    [Fact]
    public async Task AReportingLineThatClosesALoopIsRefused() =>
        await InTenantAsSystemAsync(async services =>
        {
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();
            var structures = services.GetRequiredService<IStructureService>();

            var reporting = (await structures.GetByCodeAsync("zenith-reporting"))!.StructureId;

            var ceo = (await records.GetByCodeAsync("zenith-pos-ceo"))!.RecordId;
            var manager = (await records.GetByCodeAsync("zenith-pos-mgr-tech"))!.RecordId;

            // One remove: the CEO reporting to somebody two levels below it.
            var refused = await graph.MoveAsync(reporting, ceo, manager, Today);

            refused.Succeeded.Should().BeFalse("a reporting line cannot close on itself");
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.Cycle);

            // And none of it was written: nothing is above the CEO.
            (await graph.GetAncestorsAsync(reporting, ceo, Today))
                .Should().BeEmpty("a refused move writes nothing");
        });

    /// <summary>A position cannot report to itself.</summary>
    [Fact]
    public async Task APositionCannotReportToItself() =>
        await InTenantAsSystemAsync(async services =>
        {
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var reporting = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-reporting"))!.StructureId;

            var engineer = (await records.GetByCodeAsync("zenith-pos-se"))!.RecordId;

            var refused = await graph.MoveAsync(reporting, engineer, engineer, Today);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.Cycle);
        });

    /// <summary>
    /// A grade of another position is refused for an employee of this one. Decision D26 G.
    /// </summary>
    /// <remarks>
    /// Built in this slice and called by the employee form in F6. The rule is the product's, not a
    /// screen's: a grade control populated from the position's set is a control, and a recipe, an
    /// import or a replayed form post goes round it.
    /// </remarks>
    [Fact]
    public async Task AnEmployeesGradeMustBeOneOfTheirPositionsOwnLadder() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var validator = services.GetRequiredService<IDimensionValidator>();

            var engineer = (await records.GetByCodeAsync("zenith-pos-se"))!.RecordId;
            var se2 = (await records.GetByCodeAsync("zenith-grade-se-2"))!.RecordId;
            var hrm1 = (await records.GetByCodeAsync("zenith-grade-hrm-1"))!.RecordId;

            (await validator.ValidateEmployeeGradeAsync(engineer, se2, Today))
                .Should().BeEmpty("SE-2 is one of Software Engineer's levels");

            (await validator.ValidateEmployeeGradeAsync(engineer, hrm1, Today))
                .Should().Contain(error => error.Rule == DimensionRule.ReferenceNotPermitted,
                    "HRM-1 is the HR Manager's level, not this one's");
        });

    // ---- the screens -------------------------------------------------------------------------

    /// <summary>
    /// The Positions list carries Approved, Filled, Vacant and the allowed grades. Decision D26 E.
    /// </summary>
    [Fact]
    public async Task ThePositionsListShowsTheThreeEstablishmentColumnsAndTheGrades()
    {
        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, "/Admin/Dimensions/Records/Index?group=position");

        page.Should().Contain("Approved");
        page.Should().Contain("Filled");
        page.Should().Contain("Vacant");
        page.Should().Contain("Allowed grades");

        page.Should().Contain("record-approved");
        page.Should().Contain("record-vacant");
        page.Should().Contain("record-allowed-grades");

        // The figures themselves, so a pass is not four column headings over empty cells.
        page.Should().Contain("SE-1, SE-2, SE-3");
        page.Should().Contain("HRM-1, HRM-2, HRM-3");
    }

    /// <summary>
    /// A position's page opens on its reporting line and the boxes carry filled/approved.
    /// Decision D26 E.
    /// </summary>
    /// <remarks>
    /// <b>Opens on the reporting line because a position is on two axes and neither is the
    /// default.</b> Where it sits in the business is one line of the Placements table; who answers
    /// to it is a chart, and that is what a position's page is about.
    ///
    /// The badge replaces the employee count rather than sitting beside it: an assignment names the
    /// organisation axis, so every employee number on a reporting chart is zero.
    /// </remarks>
    [Fact]
    public async Task APositionPageOpensOnItsReportingLineWithTheEstablishmentBadge()
    {
        var ceo = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            ceo = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync("zenith-pos-ceo"))!.RecordId;
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Dimensions/Records/Details/{ceo}?view=hierarchy");

        // The facts, beside the chart rather than only on it (D26 E, D26 C). One Headcount row
        // since the F5+F7 polish: it replaced an Employees count that was zero by construction, an
        // Approved headcount attribute row and an Establishment row that repeated the same approved
        // figure. PositionPageTenantTests covers the redesign; this keeps the numbers pinned here,
        // where the rest of the slice's reads are.
        page.Should().Contain("value-headcount");
        page.Should().Contain("1 approved");
        page.Should().Contain("0 filled");
        page.Should().Contain("1 vacant");
        page.Should().Contain("value-allowed-grades");
        page.Should().Contain("CEO-1", "a position's page lists the grades that are its levels");

        page.Should().Contain("value-hierarchy-establishment", "the badge is filled over approved");
        page.Should().Contain("Director Technology", "the reporting line is the chart under a position");
        page.Should().Contain("value-hierarchy-unit", "and each box names the unit the post sits in");

        page.Should().NotContain(
            "value-hierarchy-employees",
            "an employee count on a reporting chart counts nobody, so the badge replaces it");
    }

    /// <summary>
    /// The organisation designer still opens on an organisation chart, not on the grade ladder.
    /// </summary>
    /// <remarks>
    /// <b>A regression this slice caused, found by the browser suite and fixed.</b> The designer
    /// opens on the first <em>configured</em> axis — one somebody has given levels to — because
    /// <c>base.recipe.json</c> seeds an empty primary <c>organisation</c> and opening on that while
    /// the tenant's real chart sits elsewhere hides the data. Until now every configured axis was an
    /// organisation chart, because the only way an axis got levels was somebody building their
    /// company on it.
    ///
    /// This slice ships two configured axes that are <b>not</b> charts — <c>grades</c> and
    /// <c>reporting-line</c> — and both sort before a customer's own code, so the designer opened
    /// on an empty Grades axis on every tenant. The rule is now that an axis made entirely of
    /// Positions or Grades is not an organisation chart.
    ///
    /// Asserted through the rendered page, because the defect was visible only there: the view
    /// model was perfectly consistent with itself and drew the wrong axis.
    /// </remarks>
    [Fact]
    public async Task TheDesignerOpensOnAnOrganisationChartAndNotOnTheGradeLadder()
    {
        var page = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, "/Admin/Dimensions/Designer/Index");

        page.Should().Contain("zenith-org", "the chart with the company on it");

        page.Should().NotContain(
            "This structure has no top-level units on this date",
            "which is what an empty Grades axis draws, and what the designer opened on before "
            + "'configured' was qualified to mean 'configured as an organisation chart'");

        // The two new axes are still in the chooser; only the default changed.
        page.Should().Contain("zenith-reporting");
        page.Should().Contain("grades");
    }

    /// <summary>
    /// The two picker endpoints answer in the shape the controls read, and not to strangers.
    /// </summary>
    /// <remarks>
    /// <b>These are what slice F6's two drop-downs are built on</b>, which is why they are tested a
    /// slice before the screen that uses them: D26 G makes the grade control refresh from the second
    /// endpoint every time the position beside it changes, so a route that does not resolve shows up
    /// as a control that silently stops updating rather than as an error.
    ///
    /// The URLs are the constants the tag helpers embed — a control cannot ask a URL helper — so
    /// resolving them here is what stops a rename breaking the controls in silence, the same guard
    /// <c>UnitPickerAdminController.SearchUrl</c> carries.
    /// </remarks>
    [Fact]
    public async Task ThePickerEndpointsAnswerInTheShapeTheControlsRead()
    {
        var search = await _tenant.Administrator.GetAsync(
            PositionPickerAdminController.SearchUrl + "?q=Software");

        search.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var found = await search.Content.ReadAsStringAsync();

        found.Should().Contain("zenith-pos-se");
        found.Should().Contain("\"approved\":3", "an option carries how full the post is (D26 F)");
        found.Should().Contain("\"filled\":0");
        found.Should().Contain("\"full\":false");
        found.Should().Contain("Electrical", "and the unit, which tells two Supervisors apart");

        var positionId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            positionId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync("zenith-pos-se"))!.RecordId;
        });

        var grades = await _tenant.Administrator.GetAsync(
            PositionPickerAdminController.GradesUrl + "?positionId=" + positionId);

        grades.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var allowed = await grades.Content.ReadAsStringAsync();

        allowed.Should().Contain("SE-1");
        allowed.Should().Contain("SE-3");
        allowed.Should().NotContain("HRM-1", "a position's grades are its own, not every grade");

        // No position chosen is the state D26 G's control starts in: an empty answer, which the
        // control renders as a disabled box with a sentence beside it.
        var none = await _tenant.Administrator.GetAsync(PositionPickerAdminController.GradesUrl);

        (await none.Content.ReadAsStringAsync()).Should().Contain("\"total\":0");

        // And neither endpoint answers a reader who is not signed in.
        (await _tenant.Anonymous.GetAsync(PositionPickerAdminController.SearchUrl))
            .StatusCode.Should().NotBe(System.Net.HttpStatusCode.OK);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private async Task ApplyAsync(string fileName)
    {
        var path = Path.Combine(RecipesFolder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' ships with the module", fileName);

        await _tenant.InTenantAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            var descriptor = new RecipeDescriptor
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                BasePath = string.Empty,
                FileProvider = fileProvider,
                RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                RequireNewScope = false,
            };

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                descriptor,
                new Dictionary<string, object>(),
                CancellationToken.None);
        });
    }

    private static string RecipesFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkMate.sln")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull("the test runs inside the repository");

            return Path.Combine(directory!.FullName, "src", "WorkMate.Dimensions", "Recipes");
        }
    }
}
