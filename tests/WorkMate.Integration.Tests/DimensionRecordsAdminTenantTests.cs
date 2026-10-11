using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Entities;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The per-type record list and editor, exercised through real HTTP.
/// </summary>
/// <remarks>
/// What these answer that a service test cannot: that the screens are routed where the menu says,
/// that the filters narrow what they claim to, that creating a record puts it on a structure in one
/// operation, and that Orchard's own content screens are no longer a second way in.
///
/// <b>A world of its own.</b> The structure built here declares what may contain what and what may
/// sit at the top, because the create screen's parent picker is the containment map read forwards —
/// a test on a structure with no rules would prove nothing about the rule.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionRecordsAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DimensionRecordsAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>
    /// A Division at the top containing Departments, with two divisions and three departments.
    /// </summary>
    /// <remarks>
    /// Built once and reused by code, because the shared tenant is shared: every code is prefixed
    /// so nothing here collides with another test's data, and the fixture is idempotent so the
    /// tests in this class do not have to run in an order.
    /// </remarks>
    private async Task<World> GivenAWorldAsync()
    {
        World world = null!;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();

            var division = await TypeAsync(types, "rl-division", "RL Division");
            var department = await TypeAsync(types, "rl-department", "RL Department");

            var structure = await structures.GetByCodeAsync("rl-org");

            if (structure is null)
            {
                var created = await structures.CreateAsync(
                    "rl-org",
                    new BilingualText("RL Org", "آر إل"),
                    [division.DimensionTypeId, department.DimensionTypeId],
                    allowSkipLevel: false,
                    isStrict: true,
                    isPrimaryOrganisation: false,
                    new StructureShape(
                        [division.DimensionTypeId],
                        [new StructureContainment(division.DimensionTypeId, department.DimensionTypeId)]));

                created.Succeeded.Should().BeTrue(
                    string.Join("; ", created.Errors.Select(error => error.Message.Value)));

                structure = created.Value!;
            }

            var north = await UnitAsync(records, structure.StructureId, null, division.DimensionTypeId,
                "rl-div-north", "RL North");

            var south = await UnitAsync(records, structure.StructureId, null, division.DimensionTypeId,
                "rl-div-south", "RL South");

            await UnitAsync(records, structure.StructureId, north, department.DimensionTypeId,
                "rl-dept-credit", "RL Credit");

            await UnitAsync(records, structure.StructureId, north, department.DimensionTypeId,
                "rl-dept-operations", "RL Operations");

            await UnitAsync(records, structure.StructureId, south, department.DimensionTypeId,
                "rl-dept-support", "RL Support");

            // Retired on a past date, so it is absent from an "active" list as at today and present
            // on a "retired" one. Its own department, so no other assertion here depends on it.
            var closed = await UnitAsync(records, structure.StructureId, south, department.DimensionTypeId,
                "rl-dept-closed", "RL Closed");

            var closedRecord = await records.GetAsync(closed, Opened);

            if (closedRecord is not null && closedRecord.EffectiveRange.To is null)
            {
                (await records.RetireAsync(closed, new DateOnly(2024, 6, 30)))
                    .Succeeded.Should().BeTrue();
            }

            world = new World(
                structure.StructureId,
                division.DimensionTypeId,
                department.DimensionTypeId,
                north,
                south);
        });

        return world;
    }

    // ---- the list and its filters ------------------------------------------------------

    [Fact]
    public async Task TheListShowsEveryActiveRecordOfTheTypeWithItsParentPath()
    {
        var world = await GivenAWorldAsync();
        var html = await GetAsync($"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}");

        var codes = RowCodesIn(html);

        codes.Should().Contain(["rl-dept-credit", "rl-dept-operations", "rl-dept-support"]);
        codes.Should().NotContain("rl-dept-closed", "it was retired before today");

        html.Should().Contain("RL North", "the parent column names the chain the record sits under");
    }

    [Fact]
    public async Task SearchingNarrowsTheListByCodeOrName()
    {
        var world = await GivenAWorldAsync();

        RowCodesIn(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}&search=credit"))
            .Should().Equal(["rl-dept-credit"]);

        RowCodesIn(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}&search=RL Operations"))
            .Should().Equal(["rl-dept-operations"]);
    }

    /// <summary>
    /// "Within" narrows to a container's subtree, through the closure index.
    /// </summary>
    /// <remarks>
    /// Asserted from both ends. Narrowing to North must keep North's two and drop South's, and
    /// narrowing to South must do the reverse — a filter that quietly did nothing would pass a
    /// one-sided test.
    /// </remarks>
    [Fact]
    public async Task TheWithinFilterNarrowsToOneBranch()
    {
        var world = await GivenAWorldAsync();

        var underNorth = RowCodesIn(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}&within={world.NorthRecordId}"));

        underNorth.Should().BeEquivalentTo(["rl-dept-credit", "rl-dept-operations"]);

        var underSouth = RowCodesIn(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}"
            + $"&structureId={world.StructureId}&within={world.SouthRecordId}"));

        underSouth.Should().Contain("rl-dept-support");
        underSouth.Should().NotContain("rl-dept-credit");
    }

    [Fact]
    public async Task TheRetiredFilterShowsWhatTheActiveOneHides()
    {
        var world = await GivenAWorldAsync();

        RowCodesIn(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}&status={DimensionRecordStatus.Retired}"))
            .Should().Contain("rl-dept-closed");

        RowCodesIn(await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={world.DepartmentTypeId}&status={DimensionRecordStatus.All}"))
            .Should().Contain(["rl-dept-credit", "rl-dept-closed"]);
    }

    /// <summary>
    /// An attribute is a column only when the reader asks for it.
    /// </summary>
    /// <remarks>
    /// Off by default because a type with eight attributes makes an unreadable table, and which of
    /// them matter is the reader's question rather than ours.
    /// </remarks>
    [Fact]
    public async Task AnAttributeIsAColumnOnlyWhenAskedFor()
    {
        string typeId = null!, recordId = null!;

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var records = services.GetRequiredService<IDimensionService>();

            var type = await types.GetByCodeAsync("rl-attributed")
                ?? (await types.CreateAsync(
                    "rl-attributed",
                    new BilingualText("RL Attributed", "مسنَد"),
                    [
                        new DimensionAttributeDefinition(
                            "CostCode",
                            new BilingualText("Cost code", "رمز التكلفة"),
                            DimensionAttributeKind.Text),
                    ],
                    allowsSelfNesting: false)).Value!;

            typeId = type.DimensionTypeId;

            var existing = await records.GetByCodeAsync("rl-attr-1");

            if (existing is null)
            {
                var created = await records.CreateAsync(
                    typeId, "rl-attr-1", new BilingualText("RL Attributed One", "واحد"),
                    new EffectiveRange(Opened, null));

                created.Succeeded.Should().BeTrue();
                recordId = created.Value!.RecordId;

                (await records.SetAttributeValuesAsync(
                    recordId, [new DimensionAttributeValue("CostCode", "CC-4242", null)]))
                    .Succeeded.Should().BeTrue();
            }
            else
            {
                recordId = existing.RecordId;
            }
        });

        recordId.Should().NotBeNull();

        var without = await GetAsync($"/Admin/Dimensions/Records/Index?dimensionTypeId={typeId}");

        without.Should().NotContain("CC-4242", "the column is off until somebody ticks it");

        var with = await GetAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={typeId}&columns=CostCode");

        with.Should().Contain("CC-4242");
    }

    // ---- creating one ------------------------------------------------------------------

    /// <summary>
    /// The parent picker offers only what the structure's containment map allows.
    /// </summary>
    /// <remarks>
    /// ADR-0010's rule read forwards. The Department type may sit under a Division and nowhere
    /// else, so the picker offers the two divisions and neither of the departments — offering a
    /// parent the validator would refuse is a form whose only outcome is a refusal.
    /// </remarks>
    [Fact]
    public async Task TheParentPickerOffersOnlyWhatTheContainmentMapAllows()
    {
        var world = await GivenAWorldAsync();

        var html = await GetAsync(
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={world.DepartmentTypeId}&structureId={world.StructureId}");

        var options = ParentOptionsIn(html);

        options.Should().Contain(option => option.Contains("RL North", StringComparison.Ordinal));
        options.Should().Contain(option => option.Contains("RL South", StringComparison.Ordinal));

        options.Should().NotContain(
            option => option.Contains("RL Credit", StringComparison.Ordinal),
            "a Department may not contain a Department on this structure");
    }

    /// <summary>
    /// A root type may be left with no parent, and the picker says so rather than refusing.
    /// </summary>
    [Fact]
    public async Task ARootTypeMayBeLeftWithNoParent()
    {
        var world = await GivenAWorldAsync();

        var html = await GetAsync(
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={world.DivisionTypeId}&structureId={world.StructureId}");

        html.Should().Contain("no parent", "a Division is allowed at the top of this structure");
    }

    /// <summary>
    /// Saving creates the record and its dated link in one operation.
    /// </summary>
    /// <remarks>
    /// Both halves asserted through the engine rather than through the screen: the record exists,
    /// and it resolves under the parent that was chosen. A create that wrote only the first would
    /// leave a record the unplaced panel reveals and every roll-up misses.
    /// </remarks>
    [Fact]
    public async Task CreatingThroughTheScreenPlacesTheRecordUnderItsParent()
    {
        var world = await GivenAWorldAsync();

        var page = await GetAsync(
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={world.DepartmentTypeId}&structureId={world.StructureId}");

        var fields = RenderedForm.FieldsOf(page)
            .With("Code", "rl-dept-created")
            .With("NameEn", "RL Created")
            .With("NameAr", "منشأ")
            .With("EffectiveFrom", "2024-02-01")
            .With("ParentRecordId", world.SouthRecordId);

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Records/Create", new FormUrlEncodedContent(fields));

        response.IsSuccessStatusCode.Should().BeTrue();

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var created = await records.GetByCodeAsync("rl-dept-created", new DateOnly(2024, 2, 1));

            created.Should().NotBeNull("the record was created");

            var ancestors = await graph.GetAncestorsAsync(
                world.StructureId, created!.RecordId, new DateOnly(2024, 2, 1));

            ancestors.Select(ancestor => ancestor.RecordId).Should().Contain(
                world.SouthRecordId,
                "the dated link is written by the same call that created the record");
        });
    }

    /// <summary>
    /// A parent the containment map forbids is refused with the validator's own words.
    /// </summary>
    /// <remarks>
    /// The picker does not offer it, so reaching this takes a hand-built post — which is exactly
    /// what the service being the authority is for. The screen shows what the validator says rather
    /// than restating the rule.
    ///
    /// <b>This test was skipped, and what it was skipped for is the point of it.</b>
    /// <c>AddUnitAsync</c> used to create the record first and validate the placement afterwards,
    /// through <c>PlaceAsync</c>; a refusal then had to delete the record again, and that
    /// compensating path threw YesSql's "The object to update was not found in identity map" on any
    /// tenant with more than one structure. So the ordinary act of typing a parent the structure
    /// forbids came back as a 500 rather than as a sentence. The placement is now validated before
    /// anything is written, and the refusal is a refusal.
    /// </remarks>
    [Fact]
    public async Task AForbiddenParentIsRefusedWithTheValidatorsOwnWords()
    {
        var world = await GivenAWorldAsync();

        string creditId = null!;
        string refusal = null!;

        await InTenantAsSystemAsync(async services =>
        {
            creditId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync("rl-dept-credit"))!.RecordId;

            // What the validator says, asked directly, so the assertion below is against the
            // service's own words rather than against a sentence copied into a test.
            refusal = (await services.GetRequiredService<IDimensionValidator>().ValidateNewPlacementAsync(
                    world.StructureId, world.DepartmentTypeId, "rl-dept-refused", creditId, new DateOnly(2024, 2, 1)))
                .Single(error => !error.IsAdvisory)
                .Message.Value;
        });

        var page = await GetAsync(
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={world.DepartmentTypeId}&structureId={world.StructureId}");

        var fields = RenderedForm.FieldsOf(page)
            .With("Code", "rl-dept-refused")
            .With("NameEn", "RL Refused")
            .With("EffectiveFrom", "2024-02-01")
            .With("ParentRecordId", creditId);

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Records/Create", new FormUrlEncodedContent(fields));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a refusal is the form again, not an error page");

        // Through the parser rather than against the markup: the sentence carries quotation marks
        // around the codes, and which entity Razor encodes those to is not what this is about.
        TextOf(await response.Content.ReadAsStringAsync())
            .Should().Contain(refusal,
                "the screen shows what the validator said rather than restating the rule");

        await InTenantAsSystemAsync(async services =>
        {
            (await services.GetRequiredService<IDimensionService>().GetByCodeAsync("rl-dept-refused"))
                .Should().BeNull("nothing is written when the placement is refused");
        });
    }

    /// <summary>
    /// A refused placement on a tenant with several structures leaves no record, no link and no
    /// audit entry.
    /// </summary>
    /// <remarks>
    /// <b>Several structures on purpose.</b> Creating a record writes a closure self pair on every
    /// structure whose levels include its type, so the number of structures is the number of
    /// documents a half-finished create would leave lying about. It is also the condition the old
    /// crash needed, so a tenant with one structure is the one shape that would have passed either
    /// way.
    ///
    /// <b>All three, separately.</b> The record, the link and the audit entry are written by three
    /// different calls in <c>AddUnitAsync</c>, so "nothing was written" is three assertions and not
    /// one. The audit entry is the one most easily left behind, because it is recorded last and
    /// reads like bookkeeping rather than like data.
    /// </remarks>
    [Fact]
    public async Task ARefusedPlacementOnAMultiStructureTenantLeavesNothingBehind()
    {
        var world = await GivenAWorldAsync();

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var structures = services.GetRequiredService<IStructureService>();

            // A second structure over the same two types, so the tenant has more than one and both
            // would take a self pair for anything created here.
            var secondary = await structures.GetByCodeAsync("rl-org-secondary");

            if (secondary is null)
            {
                var created = await structures.CreateAsync(
                    "rl-org-secondary",
                    new BilingualText("RL Org Secondary", "آر إل ٢"),
                    [world.DivisionTypeId, world.DepartmentTypeId],
                    allowSkipLevel: false,
                    isStrict: true,
                    isPrimaryOrganisation: false,
                    new StructureShape(
                        [world.DivisionTypeId],
                        [new StructureContainment(world.DivisionTypeId, world.DepartmentTypeId)]));

                created.Succeeded.Should().BeTrue(
                    string.Join("; ", created.Errors.Select(error => error.Message.Value)));

                secondary = created.Value!;
            }

            (await structures.ListAsync()).Should().HaveCountGreaterThan(1, "the shape under test");

            var credit = (await records.GetByCodeAsync("rl-dept-credit"))!.RecordId;

            var refused = await records.AddUnitAsync(
                world.StructureId,
                credit,
                world.DepartmentTypeId,
                "rl-dept-nothing-left",
                new BilingualText("RL Nothing Left", "لا شيء"),
                new DateOnly(2024, 2, 1));

            refused.Succeeded.Should().BeFalse("a Department may not sit under a Department here");
            refused.Errors.Should().Contain(error => !error.IsAdvisory);

            // No record.
            (await records.GetByCodeAsync("rl-dept-nothing-left")).Should().BeNull();

            // No link, on either structure: nothing new is under the parent that was refused, and
            // nothing new is a root on the structure that was never reached.
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await graph.GetChildrenAsync(world.StructureId, credit, new DateOnly(2024, 2, 1)))
                .Should().BeEmpty();

            foreach (var structureId in new[] { world.StructureId, secondary.StructureId })
            {
                (await graph.GetUnplacedAsync(structureId, new DateOnly(2024, 2, 1)))
                    .Select(node => node.Code)
                    .Should().NotContain("rl-dept-nothing-left");
            }

            // And no audit entry. Read by code rather than by record id, because the refusal means
            // there is no record id to correlate on — which is exactly how an orphaned entry would
            // survive unnoticed.
            (await AuditedRecordCodesAsync(services))
                .Should().NotContain("rl-dept-nothing-left");
        });
    }

    // ---- Orchard's own screens are closed ----------------------------------------------

    /// <summary>
    /// A dimension record's content type is not creatable, so Orchard offers no "New" button.
    /// </summary>
    /// <remarks>
    /// The same treatment <c>Employee</c> has, and for a sharper reason: the generic screen writes a
    /// content item and no <c>DimensionLink</c>, so the record would be on no structure, under no
    /// parent, and invisible to the closure index and every roll-up built on it.
    /// </remarks>
    [Fact]
    public async Task ADimensionRecordTypeIsNotCreatableThroughOrchardsOwnScreens()
    {
        var world = await GivenAWorldAsync();

        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            var type = await types.GetAsync(world.DepartmentTypeId);
            var definition = await definitions.GetTypeDefinitionAsync(type!.ContentTypeName);

            definition.Should().NotBeNull();

            var settings = definition!.GetSettings<ContentTypeSettings>();

            settings.Creatable.Should().BeFalse("there is one way to create one of these");
            settings.Listable.Should().BeTrue("it still appears in Orchard's content list");
        });
    }

    /// <summary>
    /// The generic create URL is redirected to the screen that knows what a dimension record is.
    /// </summary>
    /// <remarks>
    /// Taking the button away is not the same as closing the route. Somebody with the URL — from a
    /// bookmark, from documentation, from another tenant — would otherwise land on a form that
    /// creates a record on no chart at all.
    /// </remarks>
    [Fact]
    public async Task TheGenericCreateUrlIsRedirectedToOurScreen()
    {
        var world = await GivenAWorldAsync();

        string contentTypeName = null!;

        await InTenantAsSystemAsync(async services =>
        {
            contentTypeName = (await services.GetRequiredService<IDimensionTypeService>()
                .GetAsync(world.DepartmentTypeId))!.ContentTypeName;
        });

        var response = await _fixture.Administrator.GetAsync(
            $"/Admin/Contents/ContentTypes/{contentTypeName}/Create");

        // The fixture's client follows redirects, so the evidence is where it ended up.
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Contain(
            "/Dimensions/Records/Create",
            "a reader who reaches the generic form is sent to the one that asks for a structure");
    }

    /// <summary>The generic edit URL is redirected too, with the record already open.</summary>
    [Fact]
    public async Task TheGenericEditUrlIsRedirectedToOurScreen()
    {
        var world = await GivenAWorldAsync();

        var response = await _fixture.Administrator.GetAsync(
            $"/Admin/Contents/ContentItems/{world.NorthRecordId}/Edit");

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Contain(
            "/Dimensions/Records/Edit",
            "a name here is dated and a placement is a link, neither of which the generic editor knows");
    }

    /// <summary>
    /// A content item that is not a dimension record is left alone.
    /// </summary>
    /// <remarks>
    /// The filter runs on every admin request, so the half that matters most is that it does
    /// nothing to everything else. An employee is the nearest neighbour: another module's content
    /// type, on the same screens, which must still edit normally.
    /// </remarks>
    [Fact]
    public async Task AContentItemThatIsNotADimensionRecordIsLeftAlone()
    {
        var response = await _fixture.Administrator.GetAsync(
            "/Admin/Contents/ContentItems/not-a-real-content-item-id/Edit");

        response.RequestMessage!.RequestUri!.AbsolutePath.Should().NotContain(
            "/Dimensions/Records/",
            "an id that names nothing is Orchard's problem to answer, not ours to redirect");
    }

    // ---- who may use it ----------------------------------------------------------------

    [Fact]
    public async Task WithoutThePermissionTheCreateScreenIsRefused()
    {
        var world = await GivenAWorldAsync();

        using var reader = await _fixture.CreateSignedInClientAsync(
            "records-admin-reader", "Workmate!Integration1", WorkMate.Platform.PlatformRoles.Employee,
            allowAutoRedirect: false);

        var refused = await reader.GetAsync(
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={world.DepartmentTypeId}");

        refused.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    // ---- harness ---------------------------------------------------------------------------

    private sealed record World(
        string StructureId,
        string DivisionTypeId,
        string DepartmentTypeId,
        string NorthRecordId,
        string SouthRecordId);

    private static IReadOnlyList<string> RowCodesIn(string html) =>
        [.. System.Text.RegularExpressions.Regex
            .Matches(html, "<tr class=\"record-row\" data-record-code=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)];

    /// <summary>A page's text, so an assertion is about words rather than about entities.</summary>
    private static string TextOf(string html) =>
        new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html).Body!.TextContent;

    /// <summary>The parent picker's options, read off the control rather than the page.</summary>
    private static IReadOnlyList<string> ParentOptionsIn(string html) =>
        [.. new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html)
            .QuerySelectorAll("select[data-unit-picker] option")
            .Select(option => option.TextContent.Trim())];

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
            code,
            new BilingualText(nameEn, nameEn + " (ar)"),
            [],
            allowsSelfNesting: false);

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
        string nameEn)
    {
        var existing = await records.GetByCodeAsync(code, Opened);

        if (existing is not null)
        {
            return existing.RecordId;
        }

        var created = await records.AddUnitAsync(
            structureId, parentRecordId, dimensionTypeId, code,
            new BilingualText(nameEn, nameEn + " (ar)"), Opened);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.RecordId;
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("records admin test"))
            {
                await work(services);
            }
        });

    /// <summary>
    /// Every record code this module has recorded a change against, read the way the audit screen
    /// reads them: the stored documents, through the audit trail's own index.
    /// </summary>
    private static async Task<IReadOnlyList<string>> AuditedRecordCodesAsync(IServiceProvider services)
    {
        var session = services.GetRequiredService<ISession>();

        // Entries written in this scope are not queryable until the session has put them in front
        // of the database — and an entry that was never written is the thing under test, so this
        // has to be the real query rather than a look at what is pending.
        await session.FlushAsync();

        var entries = await session
            .Query<OrchardCore.AuditTrail.Models.AuditTrailEvent, OrchardCore.AuditTrail.Indexes.AuditTrailEventIndex>(
                index => index.Name == DimensionAuditTrail.DimensionRecordChanged,
                collection: OrchardCore.AuditTrail.Models.AuditTrailEvent.Collection)
            .ListAsync();

        var codes = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.TryGet<DimensionRecordAuditEvent>(out var payload))
            {
                codes.Add(payload.Code);
            }
        }

        return codes;
    }
}
