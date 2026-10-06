using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.Entities;
using OrchardCore.ContentFields.Fields;
using OrchardCore.Security;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Fields;
using WorkMate.Platform.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The designer's three mutations — add a unit, rename one, retire one — against a real tenant:
/// what they do to the effective-dated tree, what they refuse, and who may do them.
/// </summary>
/// <remarks>
/// Dated assertions are made on the day before and the day after the change, never only on the
/// day itself. A dated model that is right on the effective date and wrong on either side of it
/// is the failure mode this engine exists to prevent, and a test that only looks at one date
/// cannot see it.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DesignerActionsTenantTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);

    private readonly BaseTenantFixture _fixture;

    public DesignerActionsTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    // ---- add a unit ---------------------------------------------------------------------

    [Fact]
    public async Task AddingAUnitPutsItUnderItsParentFromItsEffectiveDateAndNotTheDayBefore()
    {
        var effective = new DateOnly(2026, 4, 1);

        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "addunit");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var added = await records.AddUnitAsync(
                scenario.StructureId,
                scenario.RootRecordId,
                scenario.ChildTypeId,
                "addunit-dept-1",
                new BilingualText("Added Department", "إدارة مضافة"),
                effective);

            added.Succeeded.Should().BeTrue(Because(added.Errors));

            var dayBefore = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.RootRecordId, effective.AddDays(-1));

            dayBefore.Should().NotContain(child => child.RecordId == added.Value!.RecordId,
                "the unit does not exist before the day it starts");

            var dayOf = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.RootRecordId, effective);

            dayOf.Should().ContainSingle(child => child.RecordId == added.Value!.RecordId);

            var dayAfter = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.RootRecordId, effective.AddDays(1));

            dayAfter.Should().ContainSingle(child => child.RecordId == added.Value!.RecordId,
                "and it keeps existing after it");
        });
    }

    /// <summary>
    /// Adding a unit needs <c>ManageDimensionRecords</c> and nothing else — notably not
    /// <c>MoveDimensionRecords</c>, which exists to govern reparenting an existing unit.
    /// </summary>
    /// <remarks>
    /// This is the permission split the HR administrator role depends on: the stock stereotype
    /// grants <c>ManageDimensionRecords</c> without <c>MoveDimensionRecords</c>, so if a first
    /// placement needed the move permission, that role could create a department and never put it
    /// anywhere.
    /// </remarks>
    [Fact]
    public async Task AddingAUnitUnderAParentDoesNotNeedThePermissionToMoveUnits()
    {
        Scenario scenario = null!;

        await InTenantAsSystemAsync(async services =>
        {
            scenario = await GivenAStructureAsync(services, "addperm");
        });

        // Through the screen, as a user holding exactly ManageDimensionRecords: a shell scope has
        // no signed-in user for the service to authorise, so the only honest way to assert what
        // one permission allows is to make the request with it and nothing else.
        var editor = await CreateClientAsync("designer-editor", "Designer Editor Only",
            nameof(WorkMate.Dimensions.Permissions.ManageDimensionRecords));

        var form = await BaseTenantFixture.GetPageAsync(
            editor,
            $"/Admin/Dimensions/Designer/AddUnit?structureId={scenario.StructureId}&parentId={scenario.RootRecordId}");

        var fields = RenderedForm.FieldsOf(form)
            .With("Code", "addperm-dept-1")
            .With("NameEn", "Permission Department")
            .With("NameAr", "إدارة")
            .With("EffectiveFrom", Start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var response = await editor.PostAsync("/Admin/Dimensions/Designer/AddUnit", new FormUrlEncodedContent(fields));

        response.StatusCode.Should().Be(HttpStatusCode.Found,
            "a successful add redirects back to the designer");

        await InTenantAsSystemAsync(async services =>
        {
            var added = await services.GetRequiredService<IDimensionService>().GetByCodeAsync("addperm-dept-1");

            added.Should().NotBeNull("the unit was created without MoveDimensionRecords");

            var children = await services.GetRequiredService<IDimensionGraphService>()
                .GetChildrenAsync(scenario.StructureId, scenario.RootRecordId, Start);

            children.Should().ContainSingle(child => child.Code == "addperm-dept-1",
                "and it was placed under its parent, not left unplaced");
        });
    }

    [Fact]
    public async Task AddingAUnitStoresTheCustomAttributesItsTypeDeclares()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "attrs", withAttributes: true);
            var records = services.GetRequiredService<IDimensionService>();

            var added = await records.AddUnitAsync(
                scenario.StructureId,
                scenario.RootRecordId,
                scenario.ChildTypeId,
                "attrs-dept-1",
                new BilingualText("Attributed Department", "إدارة"),
                Start,
                [
                    new DimensionAttributeValue("costCode", "CC-42"),
                    new DimensionAttributeValue("headcount", "17"),
                    new DimensionAttributeValue("isFrontOffice", "true"),
                    new DimensionAttributeValue("openedOn", "2024-03-05"),
                    new DimensionAttributeValue("strapline", "Front of house", "واجهة"),
                ]);

            added.Succeeded.Should().BeTrue(Because(added.Errors));

            var item = await services.GetRequiredService<IContentManager>().GetAsync(added.Value!.RecordId);
            var part = item!.Get<ContentPart>(scenario.ChildContentTypeName);

            part.Should().NotBeNull("the attributes live on the part the dimension type generated");

            part!.Get<TextField>("costCode")!.Text.Should().Be("CC-42");
            part.Get<NumericField>("headcount")!.Value.Should().Be(17m);
            part.Get<BooleanField>("isFrontOffice")!.Value.Should().BeTrue();
            part.Get<DateField>("openedOn")!.Value!.Value.Date.Should().Be(new DateTime(2024, 3, 5));
            part.Get<BilingualTextField>("strapline")!.En.Should().Be("Front of house");
            part.Get<BilingualTextField>("strapline")!.Ar.Should().Be("واجهة");
        });
    }

    [Fact]
    public async Task AUnitIsRefusedWhenARequiredAttributeIsMissingOrAValueIsNotItsKind()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "attrbad", withAttributes: true);
            var records = services.GetRequiredService<IDimensionService>();

            var missing = await records.AddUnitAsync(
                scenario.StructureId, scenario.RootRecordId, scenario.ChildTypeId,
                "attrbad-dept-1", new BilingualText("No Cost Code", "بلا"), Start,
                [new DimensionAttributeValue("headcount", "3")]);

            missing.Succeeded.Should().BeFalse("costCode is declared required on this type");
            missing.Errors.Should().Contain(error => error.Subject == "costCode");

            var wrongKind = await records.AddUnitAsync(
                scenario.StructureId, scenario.RootRecordId, scenario.ChildTypeId,
                "attrbad-dept-2", new BilingualText("Bad Number", "بلا"), Start,
                [
                    new DimensionAttributeValue("costCode", "CC-1"),
                    new DimensionAttributeValue("headcount", "not a number"),
                ]);

            wrongKind.Succeeded.Should().BeFalse();
            wrongKind.Errors.Should().Contain(error => error.Subject == "headcount");

            var unknown = await records.AddUnitAsync(
                scenario.StructureId, scenario.RootRecordId, scenario.ChildTypeId,
                "attrbad-dept-3", new BilingualText("Unknown Attribute", "بلا"), Start,
                [
                    new DimensionAttributeValue("costCode", "CC-1"),
                    new DimensionAttributeValue("notOnThisType", "x"),
                ]);

            unknown.Succeeded.Should().BeFalse("the type declares no such attribute");

            // Nothing was written by any of the three.
            (await records.GetByCodeAsync("attrbad-dept-1")).Should().BeNull();
            (await records.GetByCodeAsync("attrbad-dept-2")).Should().BeNull();
            (await records.GetByCodeAsync("attrbad-dept-3")).Should().BeNull();
        });
    }

    // ---- rename -------------------------------------------------------------------------

    /// <summary>
    /// The distinction architecture section 6 insists the designer asks about: a renaming leaves
    /// earlier periods reading as they did, a correction does not.
    /// </summary>
    [Fact]
    public async Task ASubstantiveRenameKeepsTheOldNameOnEarlierPeriodsAndACorrectionDoesNot()
    {
        var renamedFrom = new DateOnly(2026, 7, 1);

        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "rename");
            var records = services.GetRequiredService<IDimensionService>();

            var renamed = await records.RenameAsync(
                scenario.RootRecordId, new BilingualText("Renamed Division", "قسم جديد"), renamedFrom);

            renamed.Succeeded.Should().BeTrue(Because(renamed.Errors));

            (await records.GetNameAsync(scenario.RootRecordId, renamedFrom.AddDays(-1)))!.En
                .Should().Be("Rename Division One", "last year's reports keep the name it had then");

            (await records.GetNameAsync(scenario.RootRecordId, renamedFrom))!.En
                .Should().Be("Renamed Division");

            // A correction to the period before the rename rewrites that period only.
            var corrected = await records.CorrectNameAsync(
                scenario.RootRecordId,
                new BilingualText("Corrected Division", "قسم مصحح"),
                renamedFrom.AddDays(-1));

            corrected.Succeeded.Should().BeTrue(Because(corrected.Errors));

            (await records.GetNameAsync(scenario.RootRecordId, renamedFrom.AddDays(-1)))!.En
                .Should().Be("Corrected Division", "a correction applies retrospectively");

            (await records.GetNameAsync(scenario.RootRecordId, renamedFrom))!.En
                .Should().Be("Renamed Division", "and leaves the later period alone");
        });
    }

    // ---- retire -------------------------------------------------------------------------

    [Fact]
    public async Task RetiringAUnitClosesItFromTheDateWhileItStillResolvesTheDayBefore()
    {
        var retireFrom = new DateOnly(2026, 9, 1);

        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "retire");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var child = await records.AddUnitAsync(
                scenario.StructureId, scenario.RootRecordId, scenario.ChildTypeId,
                "retire-dept-1", new BilingualText("Doomed Department", "إدارة"), Start);

            child.Succeeded.Should().BeTrue(Because(child.Errors));

            var retired = await records.RetireAsync(scenario.StructureId, child.Value!.RecordId, retireFrom);

            retired.Succeeded.Should().BeTrue(Because(retired.Errors));

            var dayBefore = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.RootRecordId, retireFrom.AddDays(-1));

            dayBefore.Should().ContainSingle(node => node.RecordId == child.Value.RecordId,
                "the last day it applies is the day before the retirement date");

            var dayOf = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.RootRecordId, retireFrom);

            dayOf.Should().NotContain(node => node.RecordId == child.Value.RecordId);
        });
    }

    /// <summary>
    /// The dry run says what is still attached before anything happens, and says it without
    /// refusing: closing a branch from the top is legitimate, and retirement is what the
    /// architecture prescribes for a unit history still refers to.
    /// </summary>
    [Fact]
    public async Task ThePlanReportsTheUnitsStillUnderneathBeforeAnythingIsWritten()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "retireplan");
            var records = services.GetRequiredService<IDimensionService>();

            var child = await records.AddUnitAsync(
                scenario.StructureId, scenario.RootRecordId, scenario.ChildTypeId,
                "retireplan-dept-1", new BilingualText("Child Department", "إدارة"), Start);

            child.Succeeded.Should().BeTrue(Because(child.Errors));

            var plan = await records.PlanRetireAsync(scenario.StructureId, scenario.RootRecordId, new DateOnly(2026, 10, 1));

            plan.Succeeded.Should().BeTrue(Because(plan.Errors));
            plan.Value!.DescendantCount.Should().Be(1, "one unit still sits under the root");
            plan.Value.DirectChildren.Should().ContainSingle()
                .Which.RecordId.Should().Be(child.Value!.RecordId, "and the plan names it");
            plan.Value.HasChildrenToDecide.Should().BeTrue();

            // And nothing was written: the root is still on the tree.
            var root = await records.GetAsync(scenario.RootRecordId);
            root!.IsActive.Should().BeTrue();
        });
    }

    // ---- retiring a unit that still has units under it ------------------------------------

    /// <summary>
    /// A parent cannot close over live children by accident. The whole point of the three
    /// dispositions is that leaving units stranded is a decision somebody takes, not something
    /// that happens to them while they were retiring something else.
    /// </summary>
    [Fact]
    public async Task RetiringAUnitWithChildrenIsRefusedUntilSomebodySaysWhatHappensToThem()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAParentWithAChildAsync(services, "needchoice");
            var records = services.GetRequiredService<IDimensionService>();

            var refused = await records.RetireAsync(
                scenario.StructureId, scenario.ParentRecordId, RetireDay);

            refused.Succeeded.Should().BeFalse("no disposition was given and a unit sits underneath");
            refused.Errors.Should().ContainSingle()
                .Which.Rule.Should().Be(DimensionRule.ChildrenNeedDisposition);

            // Refused means untouched, including the parent.
            (await records.GetAsync(scenario.ParentRecordId))!.IsActive.Should().BeTrue();
        });
    }

    /// <summary>(a) The children move to another parent on the same day.</summary>
    [Fact]
    public async Task TheChildrenCanBeMovedToAnotherParentOnTheDayTheirOwnCloses()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAParentWithAChildAsync(services, "movechild");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // The plan offers the sibling, because the structure's levels permit a department
            // under either division — and offers neither the unit closing nor anything inside it.
            var plan = await records.PlanRetireAsync(scenario.StructureId, scenario.ParentRecordId, RetireDay);

            plan.Value!.ValidMoveTargets.Select(target => target.RecordId)
                .Should().Contain(scenario.SiblingRecordId)
                .And.NotContain(scenario.ParentRecordId)
                .And.NotContain(scenario.ChildRecordId);

            var retired = await records.RetireAsync(
                scenario.StructureId,
                scenario.ParentRecordId,
                RetireDay,
                ChildrenDisposition.MoveTo(scenario.SiblingRecordId));

            retired.Succeeded.Should().BeTrue(Because(retired.Errors));

            var dayBefore = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.ParentRecordId, RetireDay.AddDays(-1));

            dayBefore.Should().ContainSingle(node => node.RecordId == scenario.ChildRecordId,
                "on its parent's last day the child is still under it");

            var dayOf = await graph.GetChildrenAsync(
                scenario.StructureId, scenario.SiblingRecordId, RetireDay);

            dayOf.Should().ContainSingle(node => node.RecordId == scenario.ChildRecordId,
                "and from the day the parent closes it is under the new one");

            // Not stranded on the way across: there is no day on which it belongs nowhere.
            (await graph.GetUnplacedAsync(scenario.StructureId, RetireDay))
                .Should().NotContain(node => node.RecordId == scenario.ChildRecordId);

            (await records.GetAsync(scenario.ChildRecordId))!.IsActive.Should().BeTrue();
        });
    }

    /// <summary>(b) The children close with their parent, and so does everything under them.</summary>
    [Fact]
    public async Task TheChildrenCanBeRetiredWithTheirParentAndSoCanTheWholeBranch()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAParentWithAChildAsync(services, "cascade", withGrandchild: true);
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var plan = await records.PlanRetireAsync(scenario.StructureId, scenario.ParentRecordId, RetireDay);

            plan.Value!.Subtree.Select(node => node.RecordId).Should().BeEquivalentTo(
                [scenario.ChildRecordId, scenario.GrandchildRecordId!],
                "the preview shows the whole branch, not only the children");

            var retired = await records.RetireAsync(
                scenario.StructureId, scenario.ParentRecordId, RetireDay, ChildrenDisposition.Cascade);

            retired.Succeeded.Should().BeTrue(Because(retired.Errors));

            foreach (var recordId in new[]
            {
                scenario.ParentRecordId, scenario.ChildRecordId, scenario.GrandchildRecordId!,
            })
            {
                var record = await records.GetAsync(recordId, RetireDay.AddDays(-1));

                record.Should().NotBeNull("every unit in the branch still resolves on its last day");
                record!.EffectiveRange.To.Should().Be(RetireDay.AddDays(-1));

                (await records.GetAsync(recordId, RetireDay)).Should().BeNull(
                    "and none of them resolves from the day they closed");
            }

            // The day before, the branch is still a branch.
            (await graph.GetChildrenAsync(scenario.StructureId, scenario.ParentRecordId, RetireDay.AddDays(-1)))
                .Should().ContainSingle(node => node.RecordId == scenario.ChildRecordId);

            // Nothing is left stranded: a cascade retires, it does not unplace.
            (await graph.GetUnplacedAsync(scenario.StructureId, RetireDay))
                .Should().NotContain(node =>
                    node.RecordId == scenario.ChildRecordId || node.RecordId == scenario.GrandchildRecordId);
        });
    }

    /// <summary>(c) The children are left with no parent, and the designer says why.</summary>
    [Fact]
    public async Task TheChildrenCanBeLeftUnplacedAndAreMarkedAsHavingLostTheirParent()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAParentWithAChildAsync(services, "orphan");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var retired = await records.RetireAsync(
                scenario.StructureId, scenario.ParentRecordId, RetireDay, ChildrenDisposition.Unplaced);

            retired.Succeeded.Should().BeTrue(Because(retired.Errors));

            (await graph.GetChildrenAsync(scenario.StructureId, scenario.ParentRecordId, RetireDay.AddDays(-1)))
                .Should().ContainSingle(node => node.RecordId == scenario.ChildRecordId,
                    "on the day before, the child is still where it was");

            (await graph.GetUnplacedAsync(scenario.StructureId, RetireDay))
                .Should().Contain(node => node.RecordId == scenario.ChildRecordId,
                    "and from the day the parent closes it has nowhere to sit");

            (await records.GetAsync(scenario.ChildRecordId))!.IsActive.Should().BeTrue(
                "left unplaced is not the same as retired");

            // The part that was missing entirely: the record of why.
            var reasons = await graph.GetOrphanedByParentRetirementAsync(
                scenario.StructureId, [scenario.ChildRecordId]);

            reasons.Should().ContainKey(scenario.ChildRecordId);
            reasons[scenario.ChildRecordId].FormerParentId.Should().Be(scenario.ParentRecordId);
            reasons[scenario.ChildRecordId].RetiredOn.Should().Be(RetireDay);
        });
    }

    /// <summary>
    /// A unit that was never placed is not an orphan, and must not be labelled as one. The
    /// unplaced panel holds both and has to tell them apart.
    /// </summary>
    [Fact]
    public async Task AUnitThatWasNeverPlacedIsNotReportedAsHavingLostItsParent()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "neverplaced");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // Created without ever being put anywhere, which is what an import does.
            var loose = await records.CreateAsync(
                scenario.ChildTypeId,
                "neverplaced-dept-1",
                new BilingualText("Never Placed", "لم توضع"),
                new EffectiveRange(Start, null));

            loose.Succeeded.Should().BeTrue(Because(loose.Errors));

            (await graph.GetUnplacedAsync(scenario.StructureId, Start))
                .Should().Contain(node => node.RecordId == loose.Value!.RecordId);

            (await graph.GetOrphanedByParentRetirementAsync(scenario.StructureId, [loose.Value!.RecordId]))
                .Should().BeEmpty("it never had a parent to lose");
        });
    }

    /// <summary>
    /// The mark is about now, not forever. Once the unit has a parent again the badge would be
    /// describing something that stopped being true.
    /// </summary>
    [Fact]
    public async Task PlacingAnOrphanedUnitAgainClearsTheMark()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAParentWithAChildAsync(services, "reclaim");
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            (await records.RetireAsync(
                scenario.StructureId, scenario.ParentRecordId, RetireDay, ChildrenDisposition.Unplaced))
                .Succeeded.Should().BeTrue();

            (await graph.GetOrphanedByParentRetirementAsync(scenario.StructureId, [scenario.ChildRecordId]))
                .Should().ContainKey(scenario.ChildRecordId);

            (await graph.MoveAsync(
                scenario.StructureId, scenario.ChildRecordId, scenario.SiblingRecordId, RetireDay.AddDays(7)))
                .Succeeded.Should().BeTrue();

            (await graph.GetOrphanedByParentRetirementAsync(scenario.StructureId, [scenario.ChildRecordId]))
                .Should().BeEmpty("it has somewhere to be again");
        });
    }

    // ---- audit --------------------------------------------------------------------------

    /// <summary>
    /// Each of the three leaves an audit entry naming the operation, the unit and the date it
    /// takes effect. "Every action audited" is a constraint of this slice, and an operation that
    /// changes the organisation without a trace is the one kind of bug nobody can reconstruct
    /// afterwards.
    /// </summary>
    [Fact]
    public async Task EachActionRecordsAnAuditEntryNamingWhatItDid()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "audit");
            var records = services.GetRequiredService<IDimensionService>();

            var added = await records.AddUnitAsync(
                scenario.StructureId, scenario.RootRecordId, scenario.ChildTypeId,
                "audit-dept-1", new BilingualText("Audited Department", "إدارة"), Start);
            added.Succeeded.Should().BeTrue(Because(added.Errors));

            var renamed = await records.RenameAsync(
                added.Value!.RecordId, new BilingualText("Audited Renamed", "جديد"), new DateOnly(2026, 2, 1));
            renamed.Succeeded.Should().BeTrue(Because(renamed.Errors));

            var retired = await records.RetireAsync(scenario.StructureId, added.Value.RecordId, new DateOnly(2026, 3, 1));
            retired.Succeeded.Should().BeTrue(Because(retired.Errors));

            var entries = await EventsForAsync(services, added.Value.RecordId);

            entries.Select(entry => entry.Operation).Should().BeEquivalentTo(
            [
                DimensionRecordOperation.Added,
                DimensionRecordOperation.Renamed,
                DimensionRecordOperation.Retired,
            ],
                "each action records what it was, not merely that something happened");

            var addEvent = entries.Single(entry => entry.Operation == DimensionRecordOperation.Added);
            addEvent.Code.Should().Be("audit-dept-1");
            addEvent.EffectiveFrom.Should().Be(Start);
            addEvent.StructureId.Should().Be(scenario.StructureId);
            addEvent.ParentRecordId.Should().Be(scenario.RootRecordId);

            var renameEvent = entries.Single(entry => entry.Operation == DimensionRecordOperation.Renamed);
            renameEvent.Before!.NameEn.Should().Be("Audited Department", "the entry says what it was called before");
            renameEvent.After!.NameEn.Should().Be("Audited Renamed");
            renameEvent.EffectiveFrom.Should().Be(new DateOnly(2026, 2, 1));
        });
    }

    /// <summary>
    /// The audit entries this module recorded against one record, read the way the admin screen
    /// reads them: the stored documents, through the audit trail's own index.
    /// </summary>
    private static async Task<IReadOnlyList<DimensionRecordAuditEvent>> EventsForAsync(
        IServiceProvider services,
        string recordId)
    {
        var session = services.GetRequiredService<ISession>();

        // The entries were written in this scope and are not queryable until the session has put
        // them in front of the database.
        await session.FlushAsync();

        // The audit trail lives in its own YesSql collection, so its tables are not the ones a
        // plain query looks in; naming the collection is what finds them.
        var entries = await session
            .Query<OrchardCore.AuditTrail.Models.AuditTrailEvent, OrchardCore.AuditTrail.Indexes.AuditTrailEventIndex>(
                index => index.Name == DimensionAuditTrail.DimensionRecordChanged
                    && index.CorrelationId == recordId,
                collection: OrchardCore.AuditTrail.Models.AuditTrailEvent.Collection)
            .ListAsync();

        var payloads = new List<DimensionRecordAuditEvent>();

        foreach (var entry in entries)
        {
            // TryGet, not the obsolete As: an entry of another shape should be skipped, not
            // conjured into an empty payload that then fails an assertion for the wrong reason.
            if (entry.TryGet<DimensionRecordAuditEvent>(out var payload))
            {
                payloads.Add(payload);
            }
        }

        return payloads;
    }

    // ---- permissions, in the UI and on the endpoints -------------------------------------

    /// <summary>
    /// A reader sees the chart and none of the actions, in the screen and on the endpoints alike.
    /// Hiding the menu is not the control; refusing the request is.
    /// </summary>
    [Fact]
    public async Task AReaderSeesTheChartWithNoActionMenusAndCannotReachTheActionsDirectly()
    {
        string structureId = string.Empty;
        string recordId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var scenario = await GivenAStructureAsync(services, "readonly");
            structureId = scenario.StructureId;
            recordId = scenario.RootRecordId;
        });

        var reader = await CreateClientAsync("designer-reader", "Designer Reader",
            nameof(WorkMate.Dimensions.Permissions.ViewDimensionHistory));

        var page = await BaseTenantFixture.GetPageAsync(
            reader, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        page.Should().Contain("Rename Division One", "a reader can see the tree");
        page.Should().NotContain("designer-actions", "and is offered no action menu on any card");

        foreach (var url in new[]
        {
            $"/Admin/Dimensions/Designer/AddUnit?structureId={structureId}&parentId={recordId}",
            $"/Admin/Dimensions/Designer/Rename?structureId={structureId}&recordId={recordId}",
            $"/Admin/Dimensions/Designer/Retire?structureId={structureId}&recordId={recordId}",
        })
        {
            var response = await reader.GetAsync(url);

            response.StatusCode.Should().NotBe(HttpStatusCode.OK,
                "a hand-typed URL must get no further than the hidden menu would have");
        }
    }

    [Fact]
    public async Task AnEditorIsOfferedTheActionsOnEveryCard()
    {
        string structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            structureId = (await GivenAStructureAsync(services, "editor")).StructureId;
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Designer/Index?structureId={structureId}");

        page.Should().Contain("designer-actions");
        page.Should().Contain("Designer/AddUnit");
        page.Should().Contain("Designer/Rename");
        page.Should().Contain("Designer/Retire");
    }

    // ---- scaffolding --------------------------------------------------------------------

    private sealed record Scenario(
        string StructureId, string RootRecordId, string ChildTypeId, string ChildContentTypeName);

    /// <summary>The day the retirement tests close a unit on. Far enough out to be unambiguous.</summary>
    private static readonly DateOnly RetireDay = new(2026, 11, 2);

    /// <param name="SiblingRecordId">
    /// A second division, so the move disposition has somewhere valid to send the child that is
    /// not inside the branch being closed.
    /// </param>
    private sealed record ParentAndChild(
        string StructureId,
        string ParentRecordId,
        string ChildRecordId,
        string SiblingRecordId,
        string? GrandchildRecordId);

    /// <summary>
    /// Two divisions, a department under the first, and optionally a section under that: the
    /// smallest shape in which all three dispositions mean something different.
    /// </summary>
    private static async Task<ParentAndChild> GivenAParentWithAChildAsync(
        IServiceProvider services,
        string prefix,
        bool withGrandchild = false)
    {
        var types = services.GetRequiredService<IDimensionTypeService>();
        var structures = services.GetRequiredService<IStructureService>();
        var records = services.GetRequiredService<IDimensionService>();

        var division = await types.CreateAsync(
            $"{prefix}-division", new BilingualText($"{prefix} Division", "قسم"), [], allowsSelfNesting: false);
        division.Succeeded.Should().BeTrue(Because(division.Errors));

        var department = await types.CreateAsync(
            $"{prefix}-department", new BilingualText($"{prefix} Department", "إدارة"), [], allowsSelfNesting: false);
        department.Succeeded.Should().BeTrue(Because(department.Errors));

        var section = await types.CreateAsync(
            $"{prefix}-section", new BilingualText($"{prefix} Section", "شعبة"), [], allowsSelfNesting: false);
        section.Succeeded.Should().BeTrue(Because(section.Errors));

        var structure = await structures.CreateAsync(
            $"{prefix}-structure",
            new BilingualText($"{prefix} Structure", "هيكل"),
            [division.Value!.DimensionTypeId, department.Value!.DimensionTypeId, section.Value!.DimensionTypeId],
            allowSkipLevel: false,
            isStrict: true,
            isPrimaryOrganisation: false);
        structure.Succeeded.Should().BeTrue(Because(structure.Errors));

        var structureId = structure.Value!.StructureId;

        var parent = await records.AddUnitAsync(
            structureId, null, division.Value.DimensionTypeId,
            $"{prefix}-div-1", new BilingualText($"{prefix} Closing Division", "واحد"), Start);
        parent.Succeeded.Should().BeTrue(Because(parent.Errors));

        var sibling = await records.AddUnitAsync(
            structureId, null, division.Value.DimensionTypeId,
            $"{prefix}-div-2", new BilingualText($"{prefix} Surviving Division", "اثنان"), Start);
        sibling.Succeeded.Should().BeTrue(Because(sibling.Errors));

        var child = await records.AddUnitAsync(
            structureId, parent.Value!.RecordId, department.Value.DimensionTypeId,
            $"{prefix}-dept-1", new BilingualText($"{prefix} Stranded Department", "ثلاثة"), Start);
        child.Succeeded.Should().BeTrue(Because(child.Errors));

        string? grandchildId = null;

        if (withGrandchild)
        {
            var grandchild = await records.AddUnitAsync(
                structureId, child.Value!.RecordId, section.Value.DimensionTypeId,
                $"{prefix}-sec-1", new BilingualText($"{prefix} Deep Section", "أربعة"), Start);
            grandchild.Succeeded.Should().BeTrue(Because(grandchild.Errors));

            grandchildId = grandchild.Value!.RecordId;
        }

        return new ParentAndChild(
            structureId, parent.Value.RecordId, child.Value!.RecordId, sibling.Value!.RecordId, grandchildId);
    }

    /// <summary>
    /// A two-level structure with one root on it: Division over Department, optionally with a
    /// custom attribute of every kind declared on the Department type.
    /// </summary>
    private static async Task<Scenario> GivenAStructureAsync(
        IServiceProvider services,
        string prefix,
        bool withAttributes = false)
    {
        var types = services.GetRequiredService<IDimensionTypeService>();
        var structures = services.GetRequiredService<IStructureService>();
        var records = services.GetRequiredService<IDimensionService>();

        var division = await types.CreateAsync(
            $"{prefix}-division", new BilingualText($"{prefix} Division", "قسم"), [], allowsSelfNesting: false);
        division.Succeeded.Should().BeTrue(Because(division.Errors));

        IReadOnlyList<DimensionAttributeDefinition> schema = withAttributes
            ?
            [
                new DimensionAttributeDefinition(
                    "costCode", new BilingualText("Cost code", "رمز التكلفة"), DimensionAttributeKind.Text, IsRequired: true),
                new DimensionAttributeDefinition(
                    "headcount", new BilingualText("Headcount", "عدد الموظفين"), DimensionAttributeKind.Number),
                new DimensionAttributeDefinition(
                    "isFrontOffice", new BilingualText("Front office", "واجهة"), DimensionAttributeKind.Boolean),
                new DimensionAttributeDefinition(
                    "openedOn", new BilingualText("Opened on", "تاريخ الافتتاح"), DimensionAttributeKind.Date),
                new DimensionAttributeDefinition(
                    "strapline", new BilingualText("Strapline", "الشعار"), DimensionAttributeKind.BilingualText),
            ]
            : [];

        var department = await types.CreateAsync(
            $"{prefix}-department", new BilingualText($"{prefix} Department", "إدارة"), schema, allowsSelfNesting: false);
        department.Succeeded.Should().BeTrue(Because(department.Errors));

        var structure = await structures.CreateAsync(
            $"{prefix}-structure",
            new BilingualText($"{prefix} Structure", "هيكل"),
            [division.Value!.DimensionTypeId, department.Value!.DimensionTypeId],
            allowSkipLevel: false,
            isStrict: true,
            isPrimaryOrganisation: false);
        structure.Succeeded.Should().BeTrue(Because(structure.Errors));

        var root = await records.AddUnitAsync(
            structure.Value!.StructureId,
            parentRecordId: null,
            division.Value.DimensionTypeId,
            $"{prefix}-div-1",
            new BilingualText("Rename Division One", "واحد"),
            Start);
        root.Succeeded.Should().BeTrue(Because(root.Errors));

        return new Scenario(
            structure.Value.StructureId,
            root.Value!.RecordId,
            department.Value.DimensionTypeId,
            department.Value.ContentTypeName);
    }

    private static string Because(IReadOnlyList<DimensionError> errors) =>
        string.Join("; ", errors.Select(error => error.Message.Value));

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                await work(services);
            }
        });

    private async Task<HttpClient> CreateClientAsync(string userName, string roleName, params string[] permissions)
    {
        await _fixture.InTenantAsync(async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<IRole>>();

            if (await roleManager.FindByNameAsync(roleName) is not null)
            {
                return;
            }

            await roleManager.CreateAsync(new Role
            {
                RoleName = roleName,
                RoleClaims =
                [
                    // Without this Orchard's own AdminFilter challenges before any module
                    // permission is consulted, and the test would pass for the wrong reason.
                    PermissionClaim(OrchardCore.Admin.AdminPermissions.AccessAdminPanel.Name),
                    .. permissions.Select(PermissionClaim),
                ],
            });
        });

        return await _fixture.CreateSignedInClientAsync(
            userName, "Workmate!Integration1", roleName, allowAutoRedirect: false);
    }

    private static RoleClaim PermissionClaim(string permissionName) => new()
    {
        ClaimType = OrchardCore.Security.Permissions.Permission.ClaimType,
        ClaimValue = permissionName,
    };
}
