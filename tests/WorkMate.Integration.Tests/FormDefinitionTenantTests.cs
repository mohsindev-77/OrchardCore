using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentFields.Fields;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Flows.Models;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Slice B3: the form definition model and the compiler. Decision D33.
/// </summary>
/// <remarks>
/// <b>Through the service, because there is no screen.</b> B3 deliberately ships no UI — the model
/// is the part most expensive to get wrong and least useful to build against an unsettled screen —
/// so every assertion here goes through <c>IFormDefinitionService</c>, which is also the one path
/// B4's screens, B6's recipe step and any later API will take.
///
/// <b>What these are really testing.</b> Not "a field appeared": that is the easy half and it
/// would pass against a compiler that quietly deleted the other fields. The claims worth the cost
/// are that a value <em>saves and reloads</em> through the generated definition, that nothing a
/// publish does removes anything, and that each safety rule refuses with a sentence somebody can
/// act on.
/// </remarks>
public sealed class FormDefinitionTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- publishing a new section --------------------------------------------------------------

    /// <summary>
    /// A new section publishes into a type and a bag on <c>Employee</c>, and a value written into
    /// it saves and reloads.
    /// </summary>
    /// <remarks>
    /// <b>The save-and-reload is the test.</b> Asserting the definition exists would pass against
    /// a compiler whose type is welded to nothing — which is exactly what ADR-0006's failure looks
    /// like, and it is silent. Writing a value through <c>IContentManager</c> and reading it back
    /// is the only assertion that cannot pass while the thing is broken.
    /// </remarks>
    [Fact]
    public async Task ANewSectionPublishesIntoATypeAndABagAndHoldsAValue()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            // A list for the choice field to offer. Created here rather than relying on the
            // standard lists, which arrive through a recipe this fixture does not apply.
            var list = await services.GetRequiredService<ILookupListService>().CreateAsync(
                "b3-assets",
                new BilingualText("Asset type", "نوع الأصل"),
                [new LookupListItem("LAPTOP", new BilingualText("Laptop", "حاسوب محمول"))]);

            list.Succeeded.Should().BeTrue(Describe(list.Errors));

            var created = await forms.CreateAsync(
                "b3-assets-form",
                new BilingualText("Company assets", "أصول الشركة"),
                FormTarget.NewSection,
                sectionItemType: "EmployeeAsset",
                sectionName: "Assets",
                sectionLabel: new BilingualText("Assets", "الأصول"));

            created.Succeeded.Should().BeTrue(Describe(created.Errors));

            var fields = await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [
                    Field("AssetTag", FormFieldType.Text, "Asset tag"),
                    Field("IssuedOn", FormFieldType.Date, "Issued on"),
                    Field("AssetType", FormFieldType.Choice, "Asset type") with { ListCode = "b3-assets" },
                ]);

            fields.Succeeded.Should().BeTrue(Describe(fields.Errors));

            var published = await forms.PublishAsync(created.Value!.FormDefinitionId);

            published.Succeeded.Should().BeTrue(Describe(published.Errors));
            published.Value!.Version!.VersionNumber.Should().Be(1, "the first publish is version 1");
        });

        // A SECOND SCOPE, because the first committed its definition change. Reading it back here
        // is the question a customer asks the morning after: is the section actually there.
        await InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            var section = await definitions.GetTypeDefinitionAsync("EmployeeAsset");

            section.Should().NotBeNull("publishing a new section creates its item type");
            section!.GetSettings<ContentTypeSettings>().Stereotype
                .Should().Be(EmployeeSections.Stereotype, "it is a section like the six shipped ones");

            section.GetSettings<ContentTypeSettings>().Creatable.Should().BeFalse(
                "a row of a section is never a thing on its own");

            var part = await definitions.GetPartDefinitionAsync("EmployeeAsset");

            part!.Fields.Select(field => field.Name)
                .Should().BeEquivalentTo(["AssetTag", "IssuedOn", "AssetType"]);

            part.GetSettings<ContentPartSettings>().Attachable.Should().BeFalse(
                "design note §5.3 rule 6: a designer part is not weldable onto anything else");

            // The choice field is a TextField with the lookup editor — §3.3, and the whole reason
            // turning free text into a managed choice is free.
            var choice = part.Fields.Single(field => field.Name == "AssetType");

            choice.FieldDefinition.Name.Should().Be("TextField", "a choice is not a new field type");
            choice.Editor().Should().Be(LookupFieldEditor.Name);
            choice.GetSettings<LookupFieldSettings>().ListCode.Should().Be("b3-assets");

            var employee = await definitions.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

            employee!.Parts.Should().Contain(
                part => part.Name == "Assets",
                "and the bag is welded onto Employee, which is what the employee page loops over");
        });

        // And a value written into it survives a round trip.
        await InTenantAsync(async services =>
        {
            var employeeId = await GivenAnEmployeeAsync(services, "b3-asset-holder");
            var employees = services.GetRequiredService<IEmployeeService>();
            var item = (await employees.GetContentItemAsync(employeeId))!;

            var row = await services.GetRequiredService<IContentManager>().NewAsync("EmployeeAsset");

            row.Alter<ContentPart>("EmployeeAsset", part =>
            {
                part.Alter<TextField>("AssetTag", field => field.Text = "LT-0042");
                part.Alter<TextField>("AssetType", field => field.Text = "LAPTOP");
            });

            item.Alter<BagPart>("Assets", bag => bag.ContentItems.Add(row));

            var saved = await employees.UpdateSectionsAsync(item);

            saved.Succeeded.Should().BeTrue(Describe(saved.Errors));
        });

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var found = (await employees.GetByCodeAsync("b3-asset-holder"))!;
            var item = (await employees.GetContentItemAsync(found.EmployeeId))!;

            var bag = item.Get<BagPart>("Assets");

            bag.Should().NotBeNull("the bag the publish welded is on the record");
            bag!.ContentItems.Should().ContainSingle();

            bag.ContentItems[0].Get<ContentPart>("EmployeeAsset")
                .Get<TextField>("AssetTag")!.Text
                .Should().Be("LT-0042", "a value saved through a designed field reads back");
        });
    }

    // ---- adding to a standard section and to the record ----------------------------------------

    /// <summary>
    /// Fields added to a shipped section and to the record itself both save and reload.
    /// </summary>
    /// <remarks>
    /// The two remaining targets of D33's Q1, in one test because the claim is the same claim: a
    /// published field is a real field on a real part, and <c>EmployeeProfilePart</c> is welded
    /// onto <c>Employee</c> on the first record-level publish.
    /// </remarks>
    [Fact]
    public async Task FieldsOnAStandardSectionAndOnTheRecordBothSaveAndReload()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var onSection = await forms.CreateAsync(
                "b3-jobdetails-extra",
                new BilingualText("Job details additions", "إضافات تفاصيل الوظيفة"),
                FormTarget.Section,
                sectionItemType: EmployeeSections.JobDetailType);

            onSection.Succeeded.Should().BeTrue(Describe(onSection.Errors));

            (await forms.SetFieldsAsync(
                onSection.Value!.FormDefinitionId,
                [Field("ShiftPattern", FormFieldType.Text, "Shift pattern")]))
                .Succeeded.Should().BeTrue();

            (await forms.PublishAsync(onSection.Value!.FormDefinitionId))
                .Succeeded.Should().BeTrue();

            var onRecord = await forms.CreateAsync(
                "b3-profile",
                new BilingualText("Extra details", "تفاصيل إضافية"),
                FormTarget.Record);

            onRecord.Succeeded.Should().BeTrue(Describe(onRecord.Errors));

            (await forms.SetFieldsAsync(
                onRecord.Value!.FormDefinitionId,
                [Field("StaffCardNumber", FormFieldType.Text, "Staff card number")]))
                .Succeeded.Should().BeTrue();

            (await forms.PublishAsync(onRecord.Value!.FormDefinitionId))
                .Succeeded.Should().BeTrue();
        });

        await InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            (await definitions.GetPartDefinitionAsync(EmployeeSections.JobDetailType))!
                .Fields.Select(field => field.Name)
                .Should().Contain("ShiftPattern", "a definition may add to a shipped section");

            // AND THE SHIPPED FIELDS ARE STILL THERE. Publishing is additive, so a definition that
            // lists one field must not leave a section holding only that one.
            (await definitions.GetPartDefinitionAsync(EmployeeSections.JobDetailType))!
                .Fields.Select(field => field.Name)
                .Should().Contain("WorkLocation", "publishing adds and never removes");

            var employee = await definitions.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

            employee!.Parts.Should().Contain(
                part => part.Name == nameof(EmployeeProfilePart),
                "the record-level part is welded on the first record-level publish — D33 Q2");
        });

        await InTenantAsync(async services =>
        {
            var employeeId = await GivenAnEmployeeAsync(services, "b3-profile-holder");
            var employees = services.GetRequiredService<IEmployeeService>();
            var item = (await employees.GetContentItemAsync(employeeId))!;

            item.Alter<EmployeeProfilePart>(nameof(EmployeeProfilePart), part =>
                part.Alter<TextField>("StaffCardNumber", field => field.Text = "CARD-9"));

            (await employees.UpdateSectionsAsync(item)).Succeeded.Should().BeTrue();
        });

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var found = (await employees.GetByCodeAsync("b3-profile-holder"))!;
            var item = (await employees.GetContentItemAsync(found.EmployeeId))!;

            item.Get<EmployeeProfilePart>(nameof(EmployeeProfilePart))
                .Get<TextField>("StaffCardNumber")!.Text
                .Should().Be("CARD-9", "a record-level designed field reads back");
        });
    }

    // ---- the safety rules ----------------------------------------------------------------------

    /// <summary>
    /// Every name the guard closes is refused, with a sentence that says which rule and why.
    /// </summary>
    /// <remarks>
    /// <b>The row the Records README has carried as "Not yet" since prompt 4.</b> Design note
    /// §5.3 rule 5: the compiler calls the guard for every field it defines, so a definition can
    /// neither shadow the fixed core, nor reintroduce the placement vocabulary architecture
    /// section 2 rules out, nor take a name D22 retired.
    /// </remarks>
    [Theory]
    [InlineData("JoinDate", "fixed core")]
    [InlineData("LineManagerEmployeeId", "fixed core")]
    [InlineData("Department", "placement")]
    [InlineData("CostCentre", "placement")]
    [InlineData("Position", "position-shaped")]
    [InlineData("PositionId", "position-shaped")]
    [InlineData("Grade", "position-shaped")]
    [InlineData("GradeId", "position-shaped")]
    [InlineData("ReportsTo", "position-shaped")]
    [InlineData("LineManager", "position-shaped")]
    [InlineData("JobTitle", "retired by D22")]
    [InlineData("GradeCode", "retired by D22")]
    public async Task AFieldNameTheGuardClosesIsRefused(string fieldName, string why)
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                $"b3-guard-{fieldName.ToLowerInvariant()}",
                new BilingualText($"Guard {fieldName}", $"Guard {fieldName}"),
                FormTarget.Record);

            created.Succeeded.Should().BeTrue(Describe(created.Errors));

            var result = await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [Field(fieldName, FormFieldType.Text, fieldName)]);

            result.Succeeded.Should().BeFalse("'{0}' is {1}", fieldName, why);

            result.Errors.Should().ContainSingle()
                .Which.Message.Value.Should().NotBeNullOrWhiteSpace(
                    "a refusal a reader cannot act on is worse than none");
        });
    }

    /// <summary>A definition cannot target the fixed core, by any route.</summary>
    [Fact]
    public async Task ADefinitionCannotTargetTheFixedCore()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var result = await forms.CreateAsync(
                "b3-core",
                new BilingualText("At the core", "At the core"),
                FormTarget.Section,
                sectionItemType: EmployeeFieldNames.PartName);

            result.Succeeded.Should().BeFalse(
                "specification section 5: the fixed core is not extensible");

            // And the message says the core is closed rather than "no section called that", which
            // is true and would tell the reader to go and create one.
            string.Join(" ", result.Errors.Select(error => error.Message.Value))
                .Should().Contain("fixed core");
        });
    }

    /// <summary>A published field's type cannot change, and nor can the list behind a choice.</summary>
    [Fact]
    public async Task APublishedFieldCannotChangeType()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-type-change",
                new BilingualText("Type change", "Type change"),
                FormTarget.Record);

            await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [Field("LockerNumber", FormFieldType.Text, "Locker number")]);

            (await forms.PublishAsync(created.Value!.FormDefinitionId)).Succeeded.Should().BeTrue();

            var changed = await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [Field("LockerNumber", FormFieldType.Number, "Locker number")]);

            changed.Succeeded.Should().BeFalse(
                "every value already stored in it is shaped for the type it has");

            string.Join(" ", changed.Errors.Select(error => error.Message.Value))
                .Should().Contain("cannot change type");
        });
    }

    /// <summary>A choice field naming a retired list is refused. D22's addendum.</summary>
    [Fact]
    public async Task AChoiceFieldCannotNameARetiredList()
    {
        await InTenantAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();

            var list = await lists.CreateAsync(
                "b3-superseded",
                new BilingualText("Superseded", "Superseded"),
                [new LookupListItem("X", new BilingualText("X", "X"))]);

            (await lists.RetireAsync(list.Value!.LookupListId)).Succeeded.Should().BeTrue();

            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-retired-list",
                new BilingualText("Retired list", "Retired list"),
                FormTarget.Record);

            var result = await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [Field("Thing", FormFieldType.Choice, "Thing") with { ListCode = "b3-superseded" }]);

            result.Succeeded.Should().BeFalse(
                "a retired list is not offered for a new binding — the question it answered is "
                + "answered by a dimension now");
        });
    }

    // ---- additive publishing -------------------------------------------------------------------

    /// <summary>
    /// Taking a field out of a definition and republishing keeps the field and its stored value.
    /// </summary>
    /// <remarks>
    /// <b>The hard rule of this slice, and the one a mistake in would be unrecoverable.</b>
    /// Publishing is additive: the compiler has no code that removes a field from a content
    /// definition, removes a part from <c>Employee</c>, or touches a value. A field the definition
    /// no longer lists stops being rendered and nothing else happens to it.
    /// </remarks>
    [Fact]
    public async Task RemovingAFieldFromADefinitionKeepsTheFieldAndItsValue()
    {
        string formId = string.Empty;

        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-additive",
                new BilingualText("Additive", "Additive"),
                FormTarget.Record);

            formId = created.Value!.FormDefinitionId;

            await forms.SetFieldsAsync(
                formId,
                [
                    Field("Keeper", FormFieldType.Text, "Keeper"),
                    Field("Goer", FormFieldType.Text, "Goer"),
                ]);

            (await forms.PublishAsync(formId)).Succeeded.Should().BeTrue();
        });

        await InTenantAsync(async services =>
        {
            var employeeId = await GivenAnEmployeeAsync(services, "b3-additive-holder");
            var employees = services.GetRequiredService<IEmployeeService>();
            var item = (await employees.GetContentItemAsync(employeeId))!;

            item.Alter<EmployeeProfilePart>(nameof(EmployeeProfilePart), part =>
                part.Alter<TextField>("Goer", field => field.Text = "still here"));

            (await employees.UpdateSectionsAsync(item)).Succeeded.Should().BeTrue();
        });

        // The field is taken out of the definition and the form republished.
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            (await forms.SetFieldsAsync(formId, [Field("Keeper", FormFieldType.Text, "Keeper")]))
                .Succeeded.Should().BeTrue(
                    "taking a field out of a definition hides it; it is not a destructive edit");

            (await forms.PublishAsync(formId)).Succeeded.Should().BeTrue();
        });

        await InTenantAsync(async services =>
        {
            (await services.GetRequiredService<IContentDefinitionManager>()
                .GetPartDefinitionAsync(nameof(EmployeeProfilePart)))!
                .Fields.Select(field => field.Name)
                .Should().Contain("Goer", "the compiler removes nothing from a content definition");

            var employees = services.GetRequiredService<IEmployeeService>();
            var found = (await employees.GetByCodeAsync("b3-additive-holder"))!;
            var item = (await employees.GetContentItemAsync(found.EmployeeId))!;

            item.Get<EmployeeProfilePart>(nameof(EmployeeProfilePart))
                .Get<TextField>("Goer")!.Text
                .Should().Be("still here", "and the value is exactly where it was");
        });
    }

    // ---- versions and the no-op publish ---------------------------------------------------------

    /// <summary>
    /// A version is written per real publish, and publishing an unchanged definition does nothing.
    /// </summary>
    /// <remarks>
    /// The no-op half is what ADR-0008's re-run rule depends on: a recipe applied twice must not
    /// fail on the second pass, and a history full of identical entries is a history nobody reads.
    /// </remarks>
    [Fact]
    public async Task AVersionIsWrittenPerRealPublishAndPublishingTwiceIsANoOp()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-versions",
                new BilingualText("Versions", "Versions"),
                FormTarget.Record);

            var formId = created.Value!.FormDefinitionId;

            await forms.SetFieldsAsync(formId, [Field("One", FormFieldType.Text, "One")]);

            var first = await forms.PublishAsync(formId);

            first.Value!.WasAlreadyPublished.Should().BeFalse();
            first.Value.Version!.VersionNumber.Should().Be(1);

            // AGAIN, WITH NOTHING CHANGED.
            var again = await forms.PublishAsync(formId);

            again.Succeeded.Should().BeTrue("asking twice is not an error — ADR-0008");
            again.Value!.WasAlreadyPublished.Should().BeTrue();
            again.Value.Definition.PublishedVersion.Should().Be(1, "no number was allocated");

            // A real change, and the number moves by one.
            await forms.SetFieldsAsync(
                formId,
                [
                    Field("One", FormFieldType.Text, "One"),
                    Field("Two", FormFieldType.Text, "Two"),
                ]);

            var second = await forms.PublishAsync(formId);

            second.Value!.Version!.VersionNumber.Should().Be(2);

            var versions = await forms.GetVersionsAsync("b3-versions");

            versions.Select(version => version.VersionNumber)
                .Should().Equal([2, 1], "newest first, and exactly one per real publish");

            versions[1].Fields.Should().ContainSingle(
                "a version is frozen: version 1 still has the one field it had");
        });
    }

    /// <summary>Every publish files an audit entry, and a no-op publish files none.</summary>
    /// <remarks>
    /// Specification section 9 names a content-definition change as a WorkMate audit event, and
    /// publishing is the event that produces one. A no-op filing an entry would make the trail
    /// say a form changed on a day nobody touched it.
    /// </remarks>
    [Fact]
    public async Task EveryRealPublishFilesAnAuditEntryAndANoOpFilesNone()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-audit",
                new BilingualText("Audited", "Audited"),
                FormTarget.Record);

            var formId = created.Value!.FormDefinitionId;

            await forms.SetFieldsAsync(formId, [Field("Audited", FormFieldType.Text, "Audited")]);

            var before = await PublishEntryCountAsync(services);

            (await forms.PublishAsync(formId)).Succeeded.Should().BeTrue();

            var afterPublish = await PublishEntryCountAsync(services);

            afterPublish.Should().Be(before + 1, "a publish is an audited content-definition change");

            (await forms.PublishAsync(formId)).Succeeded.Should().BeTrue();

            (await PublishEntryCountAsync(services)).Should().Be(
                afterPublish, "a no-op publish changed nothing and says nothing");
        });
    }

    // ---- ADR-0006 ---------------------------------------------------------------------------------

    /// <summary>
    /// Publishing a section and creating an employee in the SAME scope still welds the new bag.
    /// ADR-0006.
    /// </summary>
    /// <remarks>
    /// <b>The failure this pins is silent, and that is why it is worth a test of its own.</b>
    /// Inside the scope that altered <c>Employee</c>'s definition, Orchard's <em>cached</em> read
    /// returns the definition as it was. <c>IContentManager.NewAsync</c> reads that cache, so the
    /// employee it builds carries the OLD parts — correct in every respect except the section the
    /// publish just added — and every value written into that section is discarded with nothing
    /// thrown.
    ///
    /// <b>It is not the null case the ADR describes</b>, which is why the repair needed a second
    /// half. <c>Employee</c> has existed since the migration, so nothing is null and no null check
    /// can see the problem. See <c>ContentDefinitionChangesInThisScope</c>.
    ///
    /// <b>This is exactly what B6's recipe step will do</b> — publish definitions, then import
    /// employees, in one execution — so the sequence is not hypothetical.
    /// </remarks>
    [Fact]
    public async Task PublishingAndThenCreatingAnEmployeeInOneScopeStillWeldsTheNewSection()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-same-scope",
                new BilingualText("Same scope", "Same scope"),
                FormTarget.NewSection,
                sectionItemType: "EmployeeTraining",
                sectionName: "Training",
                sectionLabel: new BilingualText("Training", "التدريب"));

            created.Succeeded.Should().BeTrue(Describe(created.Errors));

            await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [Field("CourseName", FormFieldType.Text, "Course name")]);

            (await forms.PublishAsync(created.Value!.FormDefinitionId))
                .Succeeded.Should().BeTrue();

            // SAME SCOPE, no commit in between. This is the line that used to produce an employee
            // with no Training bag at all.
            var employeeId = await GivenAnEmployeeAsync(services, "b3-same-scope-holder");
            var employees = services.GetRequiredService<IEmployeeService>();
            var item = (await employees.GetContentItemAsync(employeeId))!;

            item.Has("Training").Should().BeTrue(
                "the bag the publish welded must be on an item built in the same scope — "
                + "ADR-0006, and without the repair this is false and nothing says so");

            var row = await services.GetRequiredService<IContentManager>().NewAsync("EmployeeTraining");

            row.Alter<ContentPart>("EmployeeTraining", part =>
                part.Alter<TextField>("CourseName", field => field.Text = "First aid"));

            item.Alter<BagPart>("Training", bag => bag.ContentItems.Add(row));

            (await employees.UpdateSectionsAsync(item)).Succeeded.Should().BeTrue();
        });

        // And the value is really there, read in a scope that has no reason to be confused.
        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var found = (await employees.GetByCodeAsync("b3-same-scope-holder"))!;
            var item = (await employees.GetContentItemAsync(found.EmployeeId))!;

            item.Get<BagPart>("Training")!.ContentItems[0]
                .Get<ContentPart>("EmployeeTraining")
                .Get<TextField>("CourseName")!.Text
                .Should().Be("First aid", "the value written in that scope was not discarded");
        });
    }

    // ---- isolation -------------------------------------------------------------------------------

    /// <summary>A definition published on one tenant does not reach another.</summary>
    /// <remarks>
    /// CLAUDE.md rule 4: every service here is tenant-scoped by construction and takes no tenant
    /// parameter, so this is testing that the construction holds rather than that somebody
    /// remembered to filter. A content definition leaking between tenants would be the whole
    /// multi-tenant promise failing at once.
    /// </remarks>
    [Fact]
    public async Task ADefinitionPublishedOnOneTenantDoesNotReachAnother()
    {
        await InTenantAsync(async services =>
        {
            var forms = services.GetRequiredService<IFormDefinitionService>();

            var created = await forms.CreateAsync(
                "b3-isolated",
                new BilingualText("Isolated", "Isolated"),
                FormTarget.Record);

            await forms.SetFieldsAsync(
                created.Value!.FormDefinitionId,
                [Field("OnlyHere", FormFieldType.Text, "Only here")]);

            (await forms.PublishAsync(created.Value!.FormDefinitionId)).Succeeded.Should().BeTrue();
        });

        using var other = new BaseTenantFixture();

        await other.InitializeAsync();

        await other.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("B3 isolation test"))
            {
                (await services.GetRequiredService<IFormDefinitionService>()
                    .GetByCodeAsync("b3-isolated"))
                    .Should().BeNull("a definition belongs to the tenant it was made in");

                var profile = await services.GetRequiredService<IContentDefinitionManager>()
                    .GetPartDefinitionAsync(nameof(EmployeeProfilePart));

                (profile?.Fields.Any(field => field.Name == "OnlyHere") ?? false)
                    .Should().BeFalse("and so does the content definition it compiled into");
            }
        });

        await other.DisposeAsync();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static FormFieldDefinition Field(string name, FormFieldType type, string label) =>
        new(name, type, new BilingualText(label, label), new BilingualText(string.Empty, string.Empty));

    private static string Describe(IReadOnlyList<RecordError> errors) =>
        string.Join("; ", errors.Select(error => error.Message.Value));

    /// <summary>How many publish entries the audit trail holds for this tenant.</summary>
    /// <remarks>
    /// The audit trail lives in its own YesSql collection, so a plain query looks in the wrong
    /// tables; naming the collection is what finds them. The same read
    /// <c>DesignerActionsTenantTests</c> makes.
    /// </remarks>
    private static async Task<int> PublishEntryCountAsync(IServiceProvider services)
    {
        var session = services.GetRequiredService<YesSql.ISession>();

        var entries = await session
            .Query<OrchardCore.AuditTrail.Models.AuditTrailEvent,
                OrchardCore.AuditTrail.Indexes.AuditTrailEventIndex>(
                index => index.Name == FormsAuditTrail.FormDefinitionPublished,
                collection: OrchardCore.AuditTrail.Models.AuditTrailEvent.Collection)
            .ListAsync();

        return entries.Count();
    }

    private static async Task<string> GivenAnEmployeeAsync(IServiceProvider services, string code)
    {
        var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
            code,
            new BilingualText(code, code),
            new DateOnly(2024, 1, 1),
            new EmployeeDetails());

        created.Succeeded.Should().BeTrue(Describe(created.Errors));

        return created.Value!.EmployeeId;
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("B3 form definition test"))
            {
                await work(services);
            }
        });
}
