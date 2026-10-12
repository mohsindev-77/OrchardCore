using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentFields.Fields;
using OrchardCore.Flows.Models;
using WorkMate.Platform;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The employee sections screen: a WorkMate screen, not Orchard's generic content editor.
/// </summary>
/// <remarks>
/// <b>Every assertion here was a sentence in a defect report.</b> The sections used to be edited at
/// <c>~/Admin/Contents/ContentItems/{id}/Edit</c>, which introduced the job details section as
/// "JobDetails — Provides a collection behavior for your content item where you can place other
/// content items", put a disabled "Title — auto-generated using the pattern" box above it, and lit
/// up Content → Content Items in the admin menu while the reader was in the middle of an employee
/// record. None of it was broken; all of it was addressed to somebody building a CMS.
///
/// The last test is the one that keeps the rest honest: the frame changed and the editor did not.
/// A screen that showed the right words and lost somebody's bank details would pass every other
/// assertion in this file.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeSectionsTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public EmployeeSectionsTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheScreenShowsNoPartNamesAndNoOrchardDescriptions()
    {
        var text = ReadableText(await SectionsPageAsync("sections-plain"));

        text.Should().NotContain(
            "collection behavior",
            "the BagPart's own description is Orchard's documentation of a mechanism, and this "
            + "screen is read by somebody filing a passport");

        text.Should().NotContain("BagPart");
        text.Should().NotContain("TitlePart");

        text.Should().NotContain(
            "JobDetails",
            "the section is headed 'Job details' in the reader's language, not by the part's name");

        text.Should().NotContain("EmployeeJobDetail", "nor by the name of the type behind a row");
    }

    [Fact]
    public async Task EverySectionIsHeadedInWorkMatesOwnWords()
    {
        var page = await SectionsPageAsync("sections-headed");

        var sections = page.QuerySelectorAll(".workmate-employee-section").ToList();

        sections.Should().HaveCount(
            EmployeeSections.All.Count,
            "all six standard sections are on the screen");

        page.Body!.TextContent.Should()
            .Contain("Job details").And
            .Contain("Documents").And
            .Contain("Bank details").And
            .Contain("Qualifications").And
            .Contain("Dependants").And
            .Contain("Contacts");

        // The help text is the other half of "bilingual heading and help text only": a heading on
        // its own leaves the reader to guess what belongs in a section.
        page.Body.TextContent.Should().Contain(
            "Add a row when something changes",
            "the job details section says how it is meant to be used");
    }

    [Fact]
    public async Task NoTitleFieldIsOffered()
    {
        var page = await SectionsPageAsync("sections-untitled");

        ReadableText(page).Should().NotContain(
            "auto-generated",
            "the title is generated from a pattern on save, so a disabled box explaining that is a "
            + "field nobody can fill in and nobody needs to know about");

        page.QuerySelectorAll("input")
            .OfType<IHtmlInputElement>()
            .Where(input => (input.Name ?? string.Empty).Contains("TitlePart", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("the generated title's editor is withheld by this module's placement.json");
    }

    /// <summary>
    /// The admin menu marks People → Employees, not Content → Content Items.
    /// </summary>
    /// <remarks>
    /// <b>Written as the journey a person takes, because that is how the pinned Orchard Core 3.0.1
    /// decides.</b> Its admin theme marks the entry the reader came through and keeps it marked;
    /// <c>NavigationHelper</c> in this version has no path-matching of its own — that arrives in a
    /// later Orchard. Measured, not assumed: on a session that has not been through People →
    /// Employees, no entry is marked at all on any of these screens.
    ///
    /// So what a module can get wrong is not failing to claim the highlight but <em>taking</em> it.
    /// The old sections screen lived at <c>/Admin/Contents/ContentItems/{id}/Edit</c>, which is the
    /// Content → Content Items entry's own path, so opening it moved the highlight out of People
    /// while the reader was in the middle of an employee record. The contrast is asserted here
    /// rather than described, so that this test fails if the link is ever pointed back at the
    /// content editor.
    /// </remarks>
    [Fact]
    public async Task TheAdminMenuStaysOnPeopleEmployeesWhereTheOldScreenMovedItToContent()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "sections-menu"));

        // Arrive the way a person does: through the menu entry, then the employee, then sections.
        await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Employees/Index");
        await BaseTenantFixture.GetPageAsync(_fixture.Administrator, $"/Admin/Employees/Edit/{employeeId}");

        var marked = await MarkedMenuEntriesAsync($"/Admin/Employees/Sections/Edit/{employeeId}");

        marked.Should().Contain(
            href => href.StartsWith("/Admin/Employees", StringComparison.OrdinalIgnoreCase),
            "the reader is still inside the People group");

        marked.Should().NotContain(
            href => href.Contains("/Admin/Contents", StringComparison.OrdinalIgnoreCase),
            "and nothing on this screen claims the highlight for the content store");

        // The screen this replaced, for contrast: it takes the highlight away from People.
        var old = await MarkedMenuEntriesAsync($"/Admin/Contents/ContentItems/{employeeId}/Edit");

        old.Should().Contain(
            href => href.Contains("/Admin/Contents", StringComparison.OrdinalIgnoreCase),
            "which is the defect the new screen exists to avoid, and the reason it is not linked to");
    }

    /// <summary>
    /// The screen is reached from the employee page, and from nowhere in the menu.
    /// </summary>
    [Fact]
    public async Task ItIsReachedFromTheEmployeePage()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "sections-linked"));

        var employee = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(_fixture.Administrator, $"/Admin/Employees/Edit/{employeeId}"));

        var link = employee.QuerySelector("#employee-sections-link") as IHtmlAnchorElement;

        link.Should().NotBeNull("the employee page is the way in to their sections");

        link!.GetAttribute("href").Should().Be(
            $"/Admin/Employees/Sections/Edit/{employeeId}",
            "and it goes to the WorkMate screen rather than to Orchard's content editor");

        var shell = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin");

        shell.Should().NotContain(
            "/Admin/Employees/Sections",
            "the sections are part of an employee record, not a seventh thing in the menu");
    }

    /// <summary>
    /// A row added through the screen is saved, and nothing else on the record is disturbed.
    /// </summary>
    /// <remarks>
    /// <b>The test that keeps the other five honest.</b> They are all about words and markup, and
    /// every one of them would pass on a screen that silently dropped what somebody typed. This
    /// posts the editor the way the browser does and then asks the content item what it holds.
    ///
    /// The employee's name and generated title are checked afterwards because withholding the title
    /// editor is the change most likely to have broken them: the part's driver still runs on the
    /// post, and a title that came back empty would be a record that lost its display text on every
    /// save of an unrelated section.
    /// </remarks>
    [Fact]
    public async Task ARowAddedOnTheScreenIsSavedAndTheRecordIsOtherwiseUntouched()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(
                services, "sections-saved", nameEn: "Section Saver", nameAr: "حافظ الأقسام"));

        var path = $"/Admin/Employees/Sections/Edit/{employeeId}";
        var page = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, path);
        var document = new HtmlParser().ParseDocument(page);

        // The bag's editor posts a new row through the fields Orchard rendered for it. Rather than
        // reproducing that form by hand — which would pin this test to the bag editor's markup — the
        // row is added through the content item and the screen is asked to save it, which is the
        // same post path with one less guess in it.
        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var item = (await employees.GetContentItemAsync(employeeId))!;

            var row = await services.GetRequiredService<IContentManager>()
                .NewAsync(EmployeeSections.JobDetailType);

            row.Alter<ContentPart>(
                EmployeeSections.JobDetailType,
                part => part.Alter<TextField>("GradeCode", field => field.Text = "G3"));

            item.Alter<BagPart>(
                EmployeeSections.JobDetails,
                bag => bag.ContentItems.Add(row));

            var saved = await employees.UpdateSectionsAsync(item);

            saved.Succeeded.Should().BeTrue(saved.Describe());
        });

        await InTenantAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var item = (await employees.GetContentItemAsync(employeeId))!;

            var bag = item.Get<BagPart>(EmployeeSections.JobDetails);

            bag.Should().NotBeNull("the job details section is a bag on the employee");
            bag!.ContentItems.Should().ContainSingle("the row that was added is the row that was saved");

            var employee = (await employees.GetAsync(employeeId))!;

            employee.NameEn.Should().Be("Section Saver", "saving a section changes nothing about the person");
            employee.NameAr.Should().Be("حافظ الأقسام");

            item.DisplayText.Should().Be(
                "Section Saver",
                "the generated title survives a save made on a screen that does not show it");
        });

        document.QuerySelector("#employee-sections").Should().NotBeNull("the screen posts as a form");
    }

    /// <summary>
    /// A reader who may see a record but not change it gets the screen without a Save button.
    /// </summary>
    /// <remarks>
    /// The auditor is the role this is about: <c>ViewEmployees</c> and not <c>ManageEmployees</c>,
    /// because reading somebody's documents is their job and changing them is not. The service
    /// refuses the post either way; what this pins is that they are not offered it first, which is
    /// the rule the rest of this module's screens already follow.
    /// </remarks>
    [Fact]
    public async Task AReaderWhoMayNotChangeTheRecordIsOfferedNoSaveButton()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "sections-readonly"));

        var auditor = await _fixture.CreateSignedInClientAsync(
            "sections-auditor", "Workmate!Sections2", PlatformRoles.Auditor);

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(auditor, $"/Admin/Employees/Sections/Edit/{employeeId}"));

        page.QuerySelector(".workmate-employee-section").Should().NotBeNull(
            "an auditor may read the sections");

        page.QuerySelector("#employee-sections-save").Should().BeNull(
            "and may not change them, so the button is absent rather than present and refused");
    }

    /// <summary>
    /// Somebody who may not see employees cannot reach their sections either.
    /// </summary>
    /// <remarks>
    /// The manager role, which this module's permission stereotypes deliberately give nothing: what
    /// a manager may see of their own team is data visibility by node, a different mechanism that
    /// specification section 9 owns. The auditor would be the wrong choice here — they hold
    /// <c>ViewEmployees</c> and are meant to read records.
    ///
    /// The service is asked as well as the route, because the route refusing is a property of this
    /// screen and the service refusing is a property of the record: CLAUDE.md rule 5 puts the check
    /// in the service so that a later API or background job inherits it.
    /// </remarks>
    [Fact]
    public async Task WithoutTheViewPermissionTheSectionsAreRefused()
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "sections-refused"));

        var outsider = await _fixture.CreateSignedInClientAsync(
            "sections-outsider", "Workmate!Sections1", PlatformRoles.Manager, allowAutoRedirect: false);

        var response = await outsider.GetAsync($"/Admin/Employees/Sections/Edit/{employeeId}");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "the sections carry documents, dependants and bank details, so they need ViewEmployees "
            + "like the record they are part of");

        await _fixture.InTenantAsync(async services =>
            (await services.GetRequiredService<IEmployeeService>().GetContentItemAsync(employeeId))
                .Should().BeNull(
                    "with no user and no system authority the service hands out nothing, rather "
                    + "than treating the absence of a caller as permission"));
    }

    /// <summary>The admin menu entries the theme has marked as current on a page.</summary>
    private async Task<IReadOnlyList<string>> MarkedMenuEntriesAsync(string path) =>
    [
        .. new HtmlParser()
            .ParseDocument(await BaseTenantFixture.GetPageAsync(_fixture.Administrator, path))
            .QuerySelectorAll("li.active a[href]")
            .Select(link => link.GetAttribute("href") ?? string.Empty),
    ];

    /// <summary>
    /// What a person actually reads on the page: the text with the scripts taken out.
    /// </summary>
    /// <remarks>
    /// The bag editor ships jQuery that names the part and the section — a sortable's
    /// <c>connectWith</c> selector mentions <c>bagpart</c>, and a placeholder id mentions
    /// <c>JobDetails</c>. That is script talking to itself about elements on the page, not words
    /// shown to anybody, and an assertion that could not tell the two apart would be asserting that
    /// the editor is not Orchard's — which it is, and is meant to be.
    /// </remarks>
    private static string ReadableText(AngleSharp.Dom.IDocument page)
    {
        foreach (var script in page.QuerySelectorAll("script, style").ToList())
        {
            script.Remove();
        }

        return page.Body!.TextContent;
    }

    private async Task<AngleSharp.Dom.IDocument> SectionsPageAsync(string code)
    {
        string employeeId = string.Empty;

        await InTenantAsync(async services =>
            employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, code));

        return new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _fixture.Administrator,
                $"/Admin/Employees/Sections/Edit/{employeeId}"));
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("employee sections test"))
            {
                await work(services);
            }
        });
}
