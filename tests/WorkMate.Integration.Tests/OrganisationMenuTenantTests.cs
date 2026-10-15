using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Navigation;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.ViewModels;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The Organisation menu: the designer first, one expandable entry per dimension <em>name</em>, and
/// Settings last — FRS v2.5 §1.2, decisions D3 and D23, ADR-0024.
/// </summary>
/// <remarks>
/// <b>This file has asserted three different things, and the history is the design.</b> A menu with
/// one entry per dimension <em>type</em> shipped in prompt 4 session D and the product owner
/// withdrew it on sight: on a real tenant it read <em>Branchs, Cost centres, Departments,
/// Departments, Departments, Divisions, Divisions, Divisions, Projects, Regions, Sections,
/// Teams</em>. The tests were rewritten to assert three fixed entries. FRS v2.5 asked for the
/// per-dimension menu back, and two things had to be fixed first:
///
/// <list type="number">
/// <item><description><b>The plural was guessed.</b> English gained an "s" and Arabic gained
/// nothing, because قسم pluralises to أقسام by changing the word. It is authored now, on the
/// dimension type editor, and a type with none reads as its singular.</description></item>
/// <item><description><b>The tenant really did hold three Department types</b>, one per demo
/// organisation. <b>Decision D17 proposed sharing them and that was built and reverted</b> — a
/// dimension type is a level of every chart that declares it, so a shared Division type put
/// Crescent's divisions on Zenith's chart and <c>DemoCompanyRecipesTenantTests</c> caught it.
/// <b>Decision D23 groups them in the menu instead</b>: types sharing an English singular name are
/// one entry, and where that entry's values span several charts each value names its own.</description></item>
/// </list>
///
/// <b>All four recipes are loaded</b> — base, Zenith, Crescent and the demo sample — which is the
/// exact configuration that produced <em>Departments, Departments, Departments</em>. Fourteen
/// types, ten entries — eight, plus the Position and Grade dimensions the base recipe ships (D26).
///
///
/// <b>Both languages, here rather than only in the browser suite.</b> The order used to come from
/// <c>LocalizedString.PrefixPosition()</c>, which sorts by the caption, so an Arabic reader got a
/// different menu from an English one. The provider resolves captions through
/// <c>CultureInfo.CurrentUICulture</c>, so the ordering half can be proved here by building the menu
/// under each culture; what still needs a real request is what the <em>rendered page</em> says, and
/// <c>OrganisationMenuBrowserTests</c> is that.
/// </remarks>
public sealed class OrganisationMenuTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        // Its own tenant, with all three demo organisations on it — the configuration that produced
        // "Departments, Departments, Departments". The base recipe alone has one dimension type, and
        // a menu that was wrong about twelve would look right on one.
        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-crescent.recipe.json");
        await ApplyAsync("organisation-designer-demo.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    /// <summary>
    /// Fourteen dimension types, ten menu entries, and <em>Departments</em> appears exactly once.
    /// </summary>
    /// <remarks>
    /// <b>This is the tenant that produced the complaint, and this is the assertion that answers
    /// it.</b> Three organisations each declare a Department type and a Division type; the menu
    /// shows one Department entry and one Division entry, without any of the three giving up its own
    /// type — which is what D17's shared-type fix would have cost, and what it broke.
    /// </remarks>
    [Fact]
    public async Task EveryDimensionNameGetsExactlyOneEntry() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = await services.GetRequiredService<IDimensionTypeService>().ListAsync();

            types.Select(type => type.Code).Should().BeEquivalentTo(
                [
                    "position", "grade", "cost-centre",
                    "zenith-division", "zenith-department", "zenith-project", "zenith-team",
                    "crescent-division", "crescent-department", "crescent-region", "crescent-branch",
                    "demo-division", "demo-department", "demo-section",
                ],
                "fourteen types: the base recipe's three — Cost centre, and the Position and Grade "
                + "dimensions slice F5+F7 ships (D26) — Zenith's four, Crescent's four and the "
                + "sample's three. Zenith's OWN Position and Grade types are a second recipe, "
                + "organisation-designer-zenith-positions, which this tenant does not apply");

            var captions = await DimensionCaptionsAsync(services);

            captions.Should().Equal(
                [
                    "Divisions", "Departments", "Projects", "Teams",
                    "Positions", "Grades", "Regions", "Branches", "Cost centres", "Sections",
                ],
                "Division min(10, 60, 110), Department min(20, 70, 120), then 40, 50, 60, 70, 75, 80, 90, 130");

            captions.Should().OnlyHaveUniqueItems("one entry per dimension name, and one name per entry");
        });

    /// <summary>
    /// Every caption is the authored plural, and nothing is pluralised by rule.
    /// </summary>
    /// <remarks>
    /// <b>"Branchs" is the assertion.</b> It is the exact word the withdrawn implementation
    /// produced, from the exact type — Branch — that produced it, on a tenant that has that type. A
    /// regression to a derived plural fails here by name rather than by a count being off.
    /// </remarks>
    [Fact]
    public async Task TheCaptionIsTheAuthoredPluralAndNothingIsPluralisedByRule() =>
        await InTenantAsSystemAsync(async services =>
        {
            var captions = await DimensionCaptionsAsync(services);

            captions.Should().Contain("Branches");
            captions.Should().NotContain("Branchs", "no rule pluralises an English noun correctly");

            var arabic = await DimensionCaptionsAsync(services, new CultureInfo("ar"));

            arabic.Should().Contain("الفروع", "the Arabic plural is authored, not the singular reused");
            arabic.Should().NotContain("فرع", "which is the singular the old rule left unchanged");
        });

    /// <summary>
    /// A dimension with no authored plural reads as its singular, in both languages.
    /// </summary>
    /// <remarks>
    /// The fallback that makes authoring optional. Wrong-looking — <em>Workstream</em> where a
    /// reader expects <em>Workstreams</em> — and never wrong, which is the trade ADR-0024 takes over
    /// inventing a word.
    /// </remarks>
    [Fact]
    public async Task ADimensionWithNoAuthoredPluralReadsAsItsSingular() =>
        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IDimensionTypeService>().CreateAsync(
                "menu-no-plural",
                new BilingualText("Workstream", "مسار العمل"),
                [],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(Why(created.Errors));

            (await DimensionCaptionsAsync(services)).Should().Contain("Workstream");

            (await DimensionCaptionsAsync(services, new CultureInfo("ar"))).Should().Contain("مسار العمل");
        });

    /// <summary>
    /// The order is the tenant's own, and it is the same order in both languages.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> The withdrawn implementation ordered by caption, so the
    /// English menu read alphabetically in English and the Arabic one alphabetically in Arabic — two
    /// different menus for one tenant. The order now comes from a number a customer sets, and
    /// comparing the two sequences <em>by group key</em> is what proves the caption is not deciding
    /// it: the key is the English name lower-cased, which is the same value for both readers.
    /// </remarks>
    [Fact]
    public async Task TheOrderIsTheTenantsOwnAndIsIdenticalInBothLanguages() =>
        await InTenantAsSystemAsync(async services =>
        {
            var english = await DimensionOrderAsync(services, CultureInfo.InvariantCulture);
            var arabic = await DimensionOrderAsync(services, new CultureInfo("ar"));

            english.Should().Equal(
                ["division", "department", "project", "team", "position", "grade", "region", "branch", "cost centre", "section"]);

            arabic.Should().Equal(english, "the order is a number, not an alphabetisation");
        });

    /// <summary>
    /// Each dimension expands to <em>All Departments</em> and then to its own values.
    /// </summary>
    /// <remarks>
    /// Decision D23 C. The entry is a group rather than a link, which is what makes a reader two
    /// clicks from a department instead of a list screen and a search box.
    /// </remarks>
    [Fact]
    public async Task EachDimensionExpandsToAllAndThenItsOwnValues() =>
        await InTenantAsSystemAsync(async services =>
        {
            var departments = await GroupAsync(services, "Departments");
            var items = Captions(departments);

            items[0].Should().Be("All Departments", "first, always");

            RouteValue(departments.Items[0], "group").Should().Be("department");

            // A dimension that fits in the menu lists everything and offers no way to the rest.
            var projects = await GroupAsync(services, "Projects");

            Captions(projects).Should().Equal(["All Projects", "Grid Station", "Motorway Interchange"]);
        });

    /// <summary>
    /// A dimension with more values than the menu lists stops at fifteen and counts the rest.
    /// </summary>
    /// <remarks>
    /// Decision D23 C's cap, on the one dimension of this tenant that reaches it: seventeen
    /// Departments across three companies. The fifteen shown are the first fifteen of the dimension
    /// — sorted and then cut, not cut and then sorted — and the number in brackets is the whole
    /// dimension, which is what the list screen behind it will contain.
    ///
    /// Past about this many the entries stop being a shortcut and start being the thing the list
    /// screen already does better; a sidebar that scrolls for a hundred departments is the
    /// <em>Departments, Departments, Departments</em> complaint in a new costume.
    /// </remarks>
    [Fact]
    public async Task ADimensionWithMoreValuesThanTheCapOffersShowAll() =>
        await InTenantAsSystemAsync(async services =>
        {
            var departments = await GroupAsync(services, "Departments");
            var captions = Captions(departments);

            var total = (await services.GetRequiredService<IDimensionService>()
                .ListByTypeAsync((await services.GetRequiredService<IDimensionTypeService>()
                    .GetByCodeAsync("zenith-department"))!.DimensionTypeId)).Count;

            total.Should().Be(5, "Zenith's five, which is a third of this dimension");

            captions.Should().HaveCount(
                DimensionMenuGroup.MaxValues + 2, "All …, fifteen values, and Show all");

            captions[^1].Should().StartWith("Show all (");

            RouteValue(departments.Items[^1], "group").Should().Be(
                "department", "Show all opens the same list as All Departments");
        });

    /// <summary>
    /// A value's entry opens that value's own page, not a filtered list.
    /// </summary>
    /// <remarks>
    /// Decision D23 C in one assertion: <em>clicking a value opens that value's own page</em>. The
    /// route is <c>Details</c> with the record id, which is the screen that answers "what is Civil"
    /// rather than the one that answers "show me the departments".
    /// </remarks>
    [Fact]
    public async Task ClickingAValueOpensThatValuesOwnPage() =>
        await InTenantAsSystemAsync(async services =>
        {
            var civil = await services.GetRequiredService<IDimensionService>().GetByCodeAsync("zenith-dept-civil");

            civil.Should().NotBeNull();

            var departments = await GroupAsync(services, "Departments");

            var entry = departments.Items
                .FirstOrDefault(item => item.Text.Value.StartsWith("Civil", StringComparison.Ordinal));

            entry.Should().NotBeNull("Civil is one of this tenant's departments");

            RouteValue(entry!, "id").Should().Be(civil!.RecordId);
            RouteValue(entry!, "area").Should().Be("WorkMate.Dimensions");

            // And the page it names actually serves, which is the gap a route value alone leaves.
            var html = await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator, $"/Admin/Dimensions/Records/Details/{civil.RecordId}");

            html.Should().Contain("value-facts");
            html.Should().Contain("zenith-dept-civil");
        });

    /// <summary>
    /// A value whose dimension spans several organisations says which one it is in.
    /// </summary>
    /// <remarks>
    /// This tenant has three Department types and two of them have a department called <em>HR</em>.
    /// Without the bracket the menu would list HR twice with nothing to tell them apart, which is
    /// the <em>Departments, Departments, Departments</em> complaint moved one level down rather than
    /// answered.
    ///
    /// <b>The bracket disambiguates organisations, not values</b>, and this tenant proves the
    /// difference: Crescent has two departments called <em>Credit</em>, one in its Lahore branch and
    /// one in its Karachi branch, so two entries legitimately read <em>Credit (Crescent
    /// Microfinance)</em>. That is a true statement about Crescent — a code is unique across the
    /// tenant, a name is not — and the menu is not the screen that resolves it. The list screen
    /// behind <em>All Departments</em> shows the code, the parent and the organisation, which is
    /// where that question is answered.
    /// </remarks>
    [Fact]
    public async Task AValueNamesItsOrganisationWhenTheDimensionSpansMoreThanOne() =>
        await InTenantAsSystemAsync(async services =>
        {
            var captions = Captions(await GroupAsync(services, "Departments"));

            // The structure's SHORT name (D24): "(Zenith)", not "(Zenith Engineering &
            // Construction)", which is a sentence in a sidebar fifteen characters wide. And the
            // parent beside it, because both companies call their department HR, so the
            // organisation alone leaves two entries reading the same but for a bracket.
            captions.Should().Contain("HR – Corporate (Zenith)");
            captions.Should().Contain("HR – Head Office (Crescent)");

            // Projects exist on one chart only, so there is nothing to disambiguate and no bracket.
            Captions(await GroupAsync(services, "Projects")).Skip(1)
                .Should().NotContain(caption => caption.Contains('(', StringComparison.Ordinal));
        });

    /// <summary>
    /// Two departments of the same name in one company are told apart by their parent.
    /// </summary>
    /// <remarks>
    /// <b>Crescent is the tenant this exists for, and it is real data rather than a contrivance.</b>
    /// It has two departments called <em>Credit</em> — one in the Lahore branch, one in Karachi —
    /// and two called <em>Operations</em> beside them, because a code is unique within a tenant and
    /// a name is not. Before decision D24 the menu listed <em>Credit (Crescent)</em> twice with
    /// nothing to choose between them.
    ///
    /// The parent rather than the code: what tells a reader which Credit is which is <em>Lahore
    /// Main Branch</em>. The code is on the list screen, which is where somebody who wants one is
    /// going anyway.
    /// </remarks>
    [Fact]
    public async Task SameNamedValuesAreToldApartByTheirParent() =>
        await InTenantAsSystemAsync(async services =>
        {
            var captions = Captions(await GroupAsync(services, "Departments"));

            captions.Should().Contain("Credit – Lahore Main Branch (Crescent)");
            captions.Should().Contain("Credit – Karachi Main Branch (Crescent)");
            captions.Should().Contain("Operations – Lahore Main Branch (Crescent)");
            captions.Should().Contain("Operations – Karachi Main Branch (Crescent)");

            // A uniquely named department in the same group keeps its bare name: a disambiguator
            // on something unambiguous is noise, and on a menu it is noise on every row.
            captions.Should().Contain("Civil (Zenith)");
            captions.Should().NotContain(caption => caption.StartsWith("Civil –", StringComparison.Ordinal));
        });

    /// <summary>
    /// A dimension with no authored plural reads as a sentence rather than as broken English.
    /// </summary>
    /// <remarks>
    /// Decision D24. The group's caption falls back to the singular, which is correct on its own
    /// and breaks the moment it is in a sentence: <em>All Workstream</em>. The menu changes the
    /// phrasing instead of inventing a word, which is ADR-0024's whole position.
    /// </remarks>
    [Fact]
    public async Task ADimensionWithNoPluralSaysAllOfItsUnitsRatherThanAllOfIt() =>
        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IDimensionTypeService>().CreateAsync(
                "menu-phrasing-workstream",
                new BilingualText("Workstream", "مسار العمل"),
                [],
                allowsSelfNesting: false);

            created.Succeeded.Should().BeTrue(Why(created.Errors));

            Captions(await GroupAsync(services, "Workstream"))[0].Should().Be("All Workstream units");

            // And a dimension that does have one still reads the short way.
            Captions(await GroupAsync(services, "Departments"))[0].Should().Be("All Departments");
        });

    /// <summary>
    /// The shipped dimensions get their plurals on upgrade, so an existing tenant's menu is right.
    /// </summary>
    /// <remarks>
    /// The end-to-end of decision D24's first half: a type whose plural is blank or is the singular
    /// repeated — which is what every tenant that upgraded through <c>UpdateFrom10Async</c> holds —
    /// is filled from the shipped table by <c>UpdateFrom11Async</c>, and the menu reads
    /// <em>Departments</em> rather than <em>Department</em>. The migration itself is covered
    /// directly in <c>DimensionsMigrationUpgradeTenantTests</c>; this is the menu agreeing.
    /// </remarks>
    [Fact]
    public async Task TheShippedDimensionsReadAsPluralsOnATenantThatNeverAuthoredAny() =>
        await InTenantAsSystemAsync(async services =>
        {
            var captions = await DimensionCaptionsAsync(services);

            captions.Should().NotContain("Department", "a tenant should never read the singular in this menu");
            captions.Should().NotContain("Division");
            captions.Should().Contain("Departments");
            captions.Should().Contain("Divisions");
        });

    /// <summary>
    /// A new department is in the menu immediately — in the same scope that created it.
    /// </summary>
    /// <remarks>
    /// <b>Decision D23 D, and the defect ADR-0019 exists for.</b> The menu is cached per tenant, so
    /// without an invalidation a department added on Tuesday is missing until the process restarts —
    /// and with a <em>deferred-only</em> invalidation it is missing for the rest of the request that
    /// added it, which is the request that then renders the menu. Both halves are covered here: the
    /// write and the read are in one scope, which is the half a bare
    /// <c>ISignal.SignalTokenAsync</c> after the commit cannot serve.
    ///
    /// The invalidation itself is on <c>DimensionRecordHandler</c>'s content hooks rather than on
    /// each of <c>DimensionService</c>'s success paths, so it covers creating, renaming, correcting
    /// and retiring without any of them remembering to.
    /// </remarks>
    [Fact]
    public async Task ANewDepartmentIsInTheMenuImmediately() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();

            var zenith = await services.GetRequiredService<IStructureService>().GetByCodeAsync("zenith-org");
            var department = await services.GetRequiredService<IDimensionTypeService>()
                .GetByCodeAsync("zenith-department");

            var total = (await GroupAsync(services, "Departments")).Items[^1].Text.Value;

            var added = await records.AddUnitAsync(
                zenith!.StructureId,
                (await records.GetByCodeAsync("zenith-div-corporate"))!.RecordId,
                department!.DimensionTypeId,
                "zenith-dept-aardvark",
                // Sorts first in both languages, so it lands inside the fifteen the menu lists
                // whatever the cap does — which is what makes this about the cache rather than
                // about where alphabetically a new department happens to fall.
                new BilingualText("Aardvark", "آردفارك"),
                new DateOnly(2024, 1, 1));

            added.Succeeded.Should().BeTrue(Why(added.Errors));

            var after = await GroupAsync(services, "Departments");

            Captions(after).Should().Contain(
                "Aardvark (Zenith)",
                "the cached menu was dropped the moment the value was written");

            after.Items[^1].Text.Value.Should().NotBe(total, "and the count behind Show all moved with it");
        });

    /// <summary>
    /// A renamed department reads by its new name in the menu, at once.
    /// </summary>
    /// <remarks>
    /// The other half of D23 D, and the one a deferred-only signal gets wrong most visibly: a rename
    /// leaves the record's content item rewritten and the menu still reading the old word.
    /// </remarks>
    [Fact]
    public async Task ARenamedDepartmentReadsByItsNewNameAtOnce() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var treasury = await records.GetByCodeAsync("crescent-dept-treasury");

            treasury.Should().NotBeNull();

            var renamed = await records.RenameAsync(
                treasury!.RecordId,
                new BilingualText("Group Treasury", "خزينة المجموعة"),
                new DateOnly(2026, 1, 1));

            renamed.Succeeded.Should().BeTrue(Why(renamed.Errors));

            Captions(await GroupAsync(services, "Departments"))
                .Should().Contain("Group Treasury (Crescent)");
        });

    /// <summary>
    /// Turning a dimension off takes its entry away and leaves its screens reachable.
    /// </summary>
    /// <remarks>
    /// D3's hide switch. Both halves are asserted: the entry goes, and the per-dimension list is
    /// still served — because hiding a dimension from a menu is not the same as withdrawing it, and
    /// the Dimension types list's "View units" link is still the way in.
    /// </remarks>
    [Fact]
    public async Task TurningADimensionOffTakesItsEntryAwayAndLeavesItsScreensReachable()
    {
        string typeId = null!;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var project = await types.GetByCodeAsync("zenith-project");

            project.Should().NotBeNull();
            typeId = project!.DimensionTypeId;

            (await DimensionCaptionsAsync(services)).Should().Contain("Projects");

            var hidden = await types.UpdateAsync(
                typeId,
                project.Name,
                project.AttributeSchema,
                project.AllowsSelfNesting,
                new DimensionTypeMenu(ShowInMenu: false, project.PluralName, project.MenuOrder));

            hidden.Succeeded.Should().BeTrue(Why(hidden.Errors));

            (await DimensionCaptionsAsync(services)).Should().NotContain("Projects");
        });

        // The screens stay reachable, which is what makes hiding safe rather than destructive.
        var html = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, $"/Admin/Dimensions/Records/Index?dimensionTypeId={typeId}");

        html.Should().Contain("record-list-heading");
    }

    /// <summary>
    /// A save that says nothing about the menu leaves the authored plural alone.
    /// </summary>
    /// <remarks>
    /// The rule <c>IDimensionTypeService.UpdateAsync</c> states for a null menu, and the reason it
    /// is nullable at all: a recipe row or an API call that is not describing the menu must not
    /// silently clear what somebody typed. The same shape as <c>allowsSelfNesting</c> being carried
    /// through a save, and as <c>StructureShape.EmployeeAttachableDimensionTypeIds</c>'s nullable.
    /// </remarks>
    [Fact]
    public async Task AnUpdateThatSaysNothingAboutTheMenuLeavesThePluralAlone() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var team = await types.GetByCodeAsync("zenith-team");

            team!.PluralName.En.Should().Be("Teams");

            var updated = await types.UpdateAsync(
                team.DimensionTypeId,
                new BilingualText("Crew", team.Name.Ar),
                team.AttributeSchema,
                team.AllowsSelfNesting);

            updated.Succeeded.Should().BeTrue(Why(updated.Errors));

            var after = await types.GetByCodeAsync("zenith-team");

            after!.Name.En.Should().Be("Crew", "the name was what the caller described");
            after.PluralName.En.Should().Be("Teams", "and the plural was not");
            after.ShowInMenu.Should().BeTrue();
        });

    /// <summary>
    /// The organisation designer is first, unchanged, and Settings is last.
    /// </summary>
    /// <remarks>
    /// <b>Decision D23 A, and this test exists because slice F2 got it wrong.</b> F2 moved the
    /// designer under Settings — a click path that had already shipped, changed for no reason the
    /// brief asked for. It is back at the top, with the same caption and the same action, and the
    /// shape around it is D3's: the thing you look at, the dimensions, then the things you set up.
    ///
    /// The position reserved between the designer and the dimensions is the Organisation tree's,
    /// which slice F8 builds; nothing is rendered at it — see
    /// <c>Navigation.AdminMenu.OrganisationTreePosition</c> for why pointing it at the editable
    /// designer in the meantime would be worse than an absence.
    /// </remarks>
    [Fact]
    public async Task TheDesignerIsFirstAndSettingsIsLast() =>
        await InTenantAsSystemAsync(async services =>
        {
            var organisation = await OrganisationAsync(services);

            organisation[0].Text.Value.Should().Be("Organisation designer");

            organisation[0].RouteValues.Should().BeEquivalentTo(
                new { area = "WorkMate.Dimensions", controller = "OrganisationDesignerAdmin", action = "Index" },
                options => options.ExcludingMissingMembers());

            organisation[0].Items.Should().BeEmpty("the designer is a screen, not a group");

            organisation[^1].Text.Value.Should().Be("Settings");

            organisation[^1].Items.Select(item => item.Text.Value).Should().Equal(
                ["Dimension types", "Structures"],
                "Settings holds two entries and no third: the Mapping screen is dropped (D27) and "
                + "ADR-0021 is withdrawn, so the containment map on the Structures editor is the "
                + "single authority on what may sit under what");

            organisation.Take(organisation.Count - 1).Select(item => item.Text.Value)
                .Should().NotContain("Settings");
        });

    /// <summary>
    /// The designer's card actions are exactly what they were.
    /// </summary>
    /// <remarks>
    /// The other half of D23 A — <em>every card action unchanged</em>. The list is declared once in
    /// <c>DesignerCardActions.All</c> and read by three things that used to each hold a copy: the
    /// surface's URL map, the card partial's menu and the script that points a fetched card's links
    /// at the right record. Asserting the list by name is what makes "unchanged" a statement rather
    /// than a hope, and it is cheap: a slice that adds one has to come here and say so.
    /// </remarks>
    [Fact]
    public void TheDesignerCardActionsAreUnchanged() =>
        DesignerCardActions.All.Select(action => action.Name).Should().Equal(
            ["add", "rename", "move", "cancelmove", "merge", "sethead", "placeemployee", "viewemployees", "retire"]);

    /// <summary>
    /// The rendered shell agrees with the provider.
    /// </summary>
    /// <remarks>
    /// Everything above asks the provider what it would say. This asks the page, which is what
    /// closes the gap between a provider and a route, a permission or a theme — the gap that let the
    /// lifecycle buttons and the dropped designer menu entry both ship.
    /// </remarks>
    [Fact]
    public async Task TheAdminShellRendersTheDesignerTheGroupsAndSettings()
    {
        var html = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        var hrefs = document
            .QuerySelectorAll("a[href]")
            .Select(link => link.GetAttribute("href") ?? string.Empty)
            .ToList();

        var lists = hrefs
            .Where(href => href.Contains("/Dimensions/Records/Index", StringComparison.OrdinalIgnoreCase))
            .ToList();

        lists.Distinct().Should().HaveCount(10, "one list screen per dimension name");

        lists.Should().HaveCount(
            11,
            "ten 'All …' links plus the one 'Show all' this tenant earns: seventeen Departments. "
            + "Grades earns none here — the seventeen Zenith grades are a second recipe this tenant "
            + "does not apply, so the base dimension's are the only ones and there are none");

        hrefs.Where(href => href.Contains("/Dimensions/Records/Details", StringComparison.OrdinalIgnoreCase))
            .Should().NotBeEmpty("a value in the menu opens its own page");

        hrefs.Should().Contain(href => href.Contains("/Dimensions/Designer/Index", StringComparison.OrdinalIgnoreCase));

        html.Should().Contain("All Departments");

        // Read through the parser rather than off the raw HTML: Orchard's default HtmlEncoder
        // escapes every non-ASCII character, so the en dash in "HR – Corporate" is &#x2013; in the
        // response and a substring search for the character it stands for finds nothing. Asserting
        // on the decoded text is asking what a reader sees. D24.
        var captions = document
            .QuerySelectorAll("a[href*='/Dimensions/Records/Details']")
            .Select(link => link.TextContent.Trim())
            .ToList();

        captions.Should().Contain("HR – Corporate (Zenith)");

        captions.Should().NotContain(
            caption => caption.Contains("Zenith Engineering", StringComparison.Ordinal),
            "the bracket is the structure's short name");
        html.Should().NotContain("Zenith Departments", "the company-qualified captions are gone (D23 B)");
        html.Should().Contain("/Admin/Dimensions/Types/Index");
        html.Should().Contain("/Admin/Dimensions/Structures/Index");
    }

    /// <summary>
    /// Every admin page carries the script that puts a menu caption's full text on its link.
    /// </summary>
    /// <remarks>
    /// <b>Decision D24, and it has to be a script.</b> Orchard Core 3.0.1's <c>MenuItem</c> carries
    /// <c>Text</c>, <c>Href</c>, <c>Id</c>, <c>Classes</c>, <c>Permissions</c> and
    /// <c>RouteValues</c> — no title and no attribute bag, verified against the pinned assemblies
    /// with <c>tools/api-probe</c> — and the admin theme's templates are compiled into
    /// <c>TheAdmin.dll</c>, where a module cannot override them. Decorating the rendered menu is an
    /// extension of Orchard; editing the theme would be a change to it, which CLAUDE.md forbids.
    ///
    /// Asserted on a page this module does not own, because that is the point of requiring it from
    /// a result filter rather than from a view: the menu is on every admin screen and this module
    /// has a view for almost none of them.
    /// </remarks>
    [Fact]
    public async Task TheMenuTooltipScriptIsOnEveryAdminPage()
    {
        foreach (var path in new[] { "/Admin", "/Admin/Dimensions/Types/Index" })
        {
            var html = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, path);

            html.Should().Contain(
                "organisation-menu.js",
                $"the menu is on {path} too, so the script that decorates it has to be");
        }
    }

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>The Organisation group's items, in the order the menu would draw them.</summary>
    /// <remarks>
    /// This module's providers only. Orchard's own <c>OrchardCore.Contents.AdminMenu</c> reads the
    /// ambient HTTP context while it builds and throws outside a request, so asking every provider
    /// would test the harness rather than the menu. Nothing else contributes to this group, and
    /// <see cref="TheAdminShellRendersTheDesignerTheGroupsAndSettings"/> is the check that the real
    /// shell agrees.
    /// </remarks>
    private static async Task<IReadOnlyList<MenuItem>> MenuAsync(IServiceProvider services)
    {
        var builder = new NavigationBuilder();
        var module = typeof(IDimensionTypeService).Assembly;

        foreach (var provider in services.GetServices<INavigationProvider>()
            .Where(provider => provider.GetType().Assembly == module))
        {
            await provider.BuildNavigationAsync("admin", builder);
        }

        return builder.Build();
    }

    private static async Task<IReadOnlyList<MenuItem>> OrganisationAsync(IServiceProvider services)
    {
        var organisation = (await MenuAsync(services))
            .FirstOrDefault(item => item.Items.Count > 0);

        organisation.Should().NotBeNull("this module contributes one group with entries under it");

        return organisation!.Items;
    }

    /// <summary>
    /// The dimension entries, between the designer and Settings.
    /// </summary>
    /// <remarks>
    /// Identified by position rather than by looking for entries with children, because since D23 C
    /// a dimension entry <em>and</em> Settings both have children. The position is itself the
    /// contract — designer first, Settings last — so a slice that broke it would fail
    /// <see cref="TheDesignerIsFirstAndSettingsIsLast"/> by name rather than make this quietly
    /// measure the wrong thing.
    /// </remarks>
    private static async Task<IReadOnlyList<MenuItem>> DimensionsAsync(IServiceProvider services)
    {
        var organisation = await OrganisationAsync(services);

        return [.. organisation.Skip(1).Take(organisation.Count - 2)];
    }

    private static async Task<MenuItem> GroupAsync(IServiceProvider services, string caption)
    {
        var group = (await DimensionsAsync(services))
            .FirstOrDefault(item => string.Equals(item.Text.Value, caption, StringComparison.Ordinal));

        group.Should().NotBeNull($"the menu has a '{caption}' entry");

        return group!;
    }

    /// <summary>
    /// What a group's children read, in menu order, including the "All …" entry at the front.
    /// </summary>
    /// <remarks>
    /// <b><c>Text.Value</c>, never <c>Text.Name</c>.</b> A <c>LocalizedString</c>'s name is the
    /// msgid — <em>All {0}</em> — and its value is the formatted result the page renders. Reading
    /// the name passes for the entries built as <c>new LocalizedString(caption, caption)</c>, where
    /// the two are the same, and silently measures the wrong thing for the two that go through the
    /// localiser.
    /// </remarks>
    private static IReadOnlyList<string> Captions(MenuItem group) =>
        [.. group.Items.Select(item => item.Text.Value)];

    /// <summary>
    /// One route value from a menu entry.
    /// </summary>
    /// <remarks>
    /// <c>NavigationItemBuilder.Action</c> copies the anonymous object into a
    /// <c>RouteValueDictionary</c>, so reflecting over the entry's properties finds nothing and
    /// returns an empty string for everything — which reads as "every entry agrees" rather than as a
    /// broken assertion. Indexing the dictionary is the honest read.
    /// </remarks>
    private static string RouteValue(MenuItem item, string key)
    {
        item.RouteValues.Should().NotBeNull("a menu entry that opens a screen carries its route");

        return item.RouteValues!.TryGetValue(key, out var value)
            ? value as string ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// The dimension captions, in menu order, as a reader of <paramref name="culture"/> sees them.
    /// </summary>
    /// <remarks>
    /// The culture is applied by building the menu under it rather than by passing it in, because
    /// that is how the real thing resolves one: the provider reads
    /// <c>CultureInfo.CurrentUICulture</c>, set by Orchard's localisation middleware from the
    /// request. Swapping the ambient culture is the closest a test outside a request can get, and it
    /// exercises the same code path.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> DimensionCaptionsAsync(
        IServiceProvider services, CultureInfo? culture = null)
    {
        var previous = CultureInfo.CurrentUICulture;

        if (culture is not null)
        {
            CultureInfo.CurrentUICulture = culture;
        }

        try
        {
            return [.. (await DimensionsAsync(services)).Select(item => item.Text.Value)];
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// The dimensions in menu order, named by their <b>group key</b> rather than by caption.
    /// </summary>
    /// <remarks>
    /// Comparing two cultures' orders by caption would compare two different lists of words and
    /// prove nothing. The group key is the English singular name lower-cased — the one value that is
    /// the same for both readers — and it is carried on each entry's "All …" child, which is what
    /// makes "the same order" a statement with content.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> DimensionOrderAsync(
        IServiceProvider services, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;

        try
        {
            // From each entry's "All …" child, which is the one that carries the group key.
            return [.. (await DimensionsAsync(services)).Select(item => RouteValue(item.Items[0], "group"))];
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static string Why(IReadOnlyList<DimensionError> errors) =>
        string.Join("; ", errors.Select(error => error.Message.Value));

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("organisation menu test"))
            {
                await work(services);
            }
        });

    private async Task ApplyAsync(string fileName)
    {
        await InTenantAsSystemAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                new RecipeDescriptor
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                    RequireNewScope = false,
                },
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
