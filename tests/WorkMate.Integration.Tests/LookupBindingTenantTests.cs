using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentFields.Fields;
using OrchardCore.Flows.Models;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;
using static System.Net.WebUtility;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The standard lists arrive with the tenant, the standard fields are bound to them, and a value
/// the list has never heard of survives being looked at.
/// </summary>
/// <remarks>
/// <b>The last of those is the one that would be expensive to get wrong.</b> Binding a list to a
/// field that already holds free text is only safe because the control cannot represent the stored
/// value any other way than by offering it — and the day that stops being true is the day opening
/// an employee record and pressing Save quietly replaces somebody's grade. It would not throw, it
/// would not be reported, and it would be found months later by a payroll figure nobody could
/// reconcile.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class LookupBindingTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public LookupBindingTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The base recipe applies the standard lists, through the <c>recipes</c> step that names them.
    /// </summary>
    /// <remarks>
    /// The one thing a unit test on <c>base.recipe.json</c> cannot answer: whether naming another
    /// recipe by name actually resolves it. The lists live in a file of their own so an existing
    /// tenant can be given them by hand, and this is the proof that the indirection works at setup.
    /// </remarks>
    [Fact]
    public async Task TheBaseRecipeBringsTheStandardLists() =>
        await InTenantAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();

            foreach (var code in StandardLookupLists.All)
            {
                var list = await lists.GetByCodeAsync(code);

                list.Should().NotBeNull("the base recipe applies WorkMate.StandardLookupLists, which ships '{0}'", code);
                list!.Items.Should().NotBeEmpty("a list with no entries offers nothing and falls back to free text");
                list.IsSystemDefined.Should().BeTrue("a list that arrives in a recipe is WorkMate's, not the tenant's");
            }
        });

    [Fact]
    public async Task EveryShippedEntryCarriesBothNames() =>
        await InTenantAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();

            foreach (var code in StandardLookupLists.All)
            {
                var list = (await lists.GetByCodeAsync(code))!;

                list.Name.Ar.Should().NotBeNullOrWhiteSpace("'{0}' is a list WorkMate ships and can translate", code);

                list.Items.Should().AllSatisfy(item =>
                {
                    item.Name.En.Should().NotBeNullOrWhiteSpace();
                    item.Name.Ar.Should().NotBeNullOrWhiteSpace();
                });
            }
        });

    /// <summary>
    /// Every binding the module declares is on the live content definition.
    /// </summary>
    /// <remarks>
    /// Asserted against the definitions rather than against the declaration, because the
    /// declaration is the input and the definition is the thing every reader and every editor
    /// actually consults. A binding that was declared and not applied would leave the field
    /// rendering as free text with a list attached to it, which looks like a configuration mistake
    /// rather than a bug.
    /// </remarks>
    [Fact]
    public async Task TheStandardFieldsAreBoundOnTheContentDefinition() =>
        await InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            foreach (var binding in StandardLookupLists.Bindings)
            {
                var part = await definitions.GetPartDefinitionAsync(binding.PartName);

                part.Should().NotBeNull("'{0}' is a section item type this module defines", binding.PartName);

                var field = part!.Fields.SingleOrDefault(field => field.Name == binding.FieldName);

                field.Should().NotBeNull("'{0}' declares the field '{1}'", binding.PartName, binding.FieldName);

                var bound = field!;

                bound.Editor().Should().Be(
                    LookupFieldEditor.Name,
                    "{0}.{1} is bound to '{2}'",
                    binding.PartName,
                    binding.FieldName,
                    binding.ListCode);

                bound.GetSettings<LookupFieldSettings>().ListCode.Should().Be(binding.ListCode);
            }
        });

    /// <summary>
    /// Binding takes nothing away: the field is still a text field holding a plain string.
    /// </summary>
    /// <remarks>
    /// ADR-0016's claim, asserted rather than asserted-by-comment. The whole reason binding is free
    /// — no migration, no downtime, reversible — is that the stored shape never changes. A future
    /// change that turned a bound field into a field type of its own would break every recipe,
    /// every export and every record at once, and this is what stops it landing quietly.
    /// </remarks>
    [Fact]
    public async Task ABoundFieldIsStillATextFieldHoldingAPlainString() =>
        await InTenantAsync(async services =>
        {
            var definitions = services.GetRequiredService<IContentDefinitionManager>();

            foreach (var binding in StandardLookupLists.Bindings)
            {
                var part = await definitions.GetPartDefinitionAsync(binding.PartName);

                part!.Fields.Single(field => field.Name == binding.FieldName)
                    .FieldDefinition.Name.Should().Be(
                        nameof(TextField),
                        "binding changes the editor, never the stored shape");
            }
        });

    /// <summary>
    /// The employee core's nationality is bound without the core becoming editable.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The screen must offer the list — that is the point — and
    /// <c>EmployeePart</c> must still carry no content field called <c>NationalityCode</c>, because
    /// the core is fixed by design and the guard that keeps it fixed would have refused one.
    /// </remarks>
    [Fact]
    public async Task TheCoreNationalityIsBoundByTheScreenAndNotByTheContentDefinition()
    {
        await InTenantAsync(async services =>
        {
            var part = await services.GetRequiredService<IContentDefinitionManager>()
                .GetPartDefinitionAsync(nameof(EmployeePart));

            part?.Fields.Should().NotContain(
                field => field.Name == nameof(EmployeePart.NationalityCode),
                "the core holds no content fields; binding it is the screen's business");
        });

        var create = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Employees/Create");

        create.Should().Contain("workmate-lookup", "the Add employee screen offers the nationality list");
        create.Should().Contain("data-lookup-list=\"nationality\"");
        HtmlDecode(create).Should().Contain("Bahraini", "an entry the standard list ships");
    }

    /// <summary>
    /// An employee whose stored value matches no entry keeps it, and the editor says so.
    /// </summary>
    /// <remarks>
    /// The case the brief names, end to end against a real screen: a record written before the
    /// list existed, read back through the bound control. The value must come back on the page and
    /// must still be there after the form is posted unchanged — the second half being the one that
    /// catches a control that renders the value and then fails to offer it as a selectable option.
    /// </remarks>
    [Fact]
    public async Task AnEmployeeWithAValueInNoEntryKeepsItAndIsShownAsUnmatched()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "bind-unmatched-1",
                new BilingualText("Unmatched Nationality", "جنسية غير مطابقة"),
                new DateOnly(2024, 1, 1),
                // A nationality the standard list has never heard of: what a tenant that typed its
                // own two-letter codes before the list existed would be holding.
                new EmployeeDetails(null, "ZZ", Gender.Unspecified, null, null));

            created.Succeeded.Should().BeTrue(created.Describe());
            employeeId = created.Value!.EmployeeId;
        });

        var edit = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        var decoded = HtmlDecode(edit);

        decoded.Should().Contain("ZZ", "the value the record holds is offered as itself");
        decoded.Should().Contain("not in the list", "and is marked, so a reader knows why it looks odd");
        edit.Should().Contain("workmate-lookup-unmatched", "the hint under the control");

        // Post the form back exactly as rendered — the save a reader makes after looking at
        // something else on the page — and the value must be untouched.
        var fields = RenderedForm.FieldsOf(edit);

        var response = await _fixture.Administrator.PostAsync(
            $"/Admin/Employees/Edit/{employeeId}",
            new FormUrlEncodedContent(fields));

        response.EnsureSuccessStatusCode();

        await InTenantAsync(async services =>
        {
            var employee = await services.GetRequiredService<IEmployeeService>().GetAsync(employeeId);

            employee!.NationalityCode.Should().Be(
                "ZZ",
                "opening a record and saving it must never replace a value the list does not know");
        });
    }

    /// <summary>
    /// A bound section field renders the list in Orchard's own content editor.
    /// </summary>
    /// <remarks>
    /// The sections are edited through <c>/Admin/Contents/ContentItems/{id}/Edit</c>, not through
    /// this module's screens, so this is the only place the alternate editor view is exercised
    /// against the real display pipeline. It is also the assertion that the shape alternate
    /// resolves at all: <c>TextField_Edit__WorkMateLookup</c> is a convention rather than an API,
    /// and if Orchard ever stopped deriving it the field would silently fall back to a text box
    /// with a list attached to it.
    /// </remarks>
    [Fact]
    public async Task ABoundSectionFieldRendersTheListInTheContentEditor()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "bind-section-1",
                new BilingualText("Section Binding", "ربط القسم"),
                new DateOnly(2024, 1, 1),
                new EmployeeDetails(null, null, Gender.Unspecified, null, null));

            created.Succeeded.Should().BeTrue(created.Describe());
            employeeId = created.Value!.EmployeeId;

            // A job detail row holding a grade the list does not have, added the way the content
            // editor would.
            var contentManager = services.GetRequiredService<IContentManager>();
            var employee = await contentManager.GetAsync(employeeId);

            var row = await contentManager.NewAsync(EmployeeSections.JobDetailType);

            row.Alter<ContentPart>(EmployeeSections.JobDetailType, part =>
                part.Alter<TextField>("GradeCode", field => field.Text = "Grade One"));

            employee!.Alter<BagPart>(EmployeeSections.JobDetails, bag => bag.ContentItems.Add(row));

            await contentManager.UpdateAsync(employee);
        });

        var editor = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Contents/ContentItems/{employeeId}/Edit");

        var decoded = HtmlDecode(editor);

        decoded.Should().Contain("data-lookup-list=\"grade\"", "the grade field offers the grade list");
        decoded.Should().Contain("Grade 1 (G1)", "an entry the standard list ships");
        decoded.Should().Contain("Grade One", "and the value this row already held");
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup binding test"))
            {
                await work(services);
            }
        });
}
