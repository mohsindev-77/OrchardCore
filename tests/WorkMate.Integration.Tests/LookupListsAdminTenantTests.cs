using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Platform;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;
using static System.Net.WebUtility;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The Forms screen and the lookup list screens, over real HTTP against the base tenant.
/// </summary>
/// <remarks>
/// What the unit and service tests cannot answer: whether the screens render, whether the one new
/// menu entry reaches them, whether a reader without <c>ManageLookupLists</c> is refused in the UI
/// as well as in the service, and whether the editor's affordances match the rules — an entry that
/// cannot be removed must not be offered a Remove button.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class LookupListsAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public LookupListsAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnAnonymousVisitorIsNotShownTheForms()
    {
        var forms = await _fixture.Anonymous.GetAsync("/Admin/Forms/Index");
        var lists = await _fixture.Anonymous.GetAsync("/Admin/Forms/Lookups/Index");

        // AdminFilter challenges an unauthenticated request rather than forbidding it outright;
        // either way neither may be the 200 a signed-in administrator gets.
        forms.StatusCode.Should().NotBe(HttpStatusCode.OK);
        lists.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The one new menu entry is on the admin shell, and it goes to the Forms screen.
    /// </summary>
    /// <remarks>
    /// By href rather than by caption so it says the same thing in any language, the way
    /// <c>OrganisationMenuTenantTests</c> does. The product owner's rule is one entry: the count is
    /// asserted in <c>PeopleMenuTenantTests</c>, and this is the half that proves the entry reaches
    /// a real screen rather than a 404.
    /// </remarks>
    [Fact]
    public async Task TheFormsEntryIsOnTheAdminShellAndOpens()
    {
        var shell = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin");

        shell.Should().Contain("/Admin/Forms/Index");

        var forms = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Forms/Index");

        forms.Should().Contain("Forms");
        forms.Should().Contain("forms-open-lookup-lists", "the lookup lists are reached from here");
        forms.Should().Contain("forms-designer-pending", "the screen says plainly what is not here yet");
    }

    /// <summary>
    /// The lookup lists screen is reachable from Forms and not from the menu.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The product owner's constraint is one menu entry, so a second one would
    /// be a regression even though the screen would still work; and a screen with no link to it
    /// passes every test that starts by going there — the lesson
    /// <c>EmployeeLifecycleClickBrowserTests</c> was written for.
    /// </remarks>
    [Fact]
    public async Task TheLookupListsAreReachedFromFormsAndNotFromTheMenu()
    {
        var shell = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin");

        shell.Should().NotContain(
            "/Admin/Forms/Lookups/",
            "the lookup lists are reached from the Forms screen, not from a menu entry of their own");

        var forms = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Forms/Index");

        forms.Should().Contain("/Admin/Forms/Lookups/Index");
    }

    [Fact]
    public async Task CreatingAListThroughTheScreenStoresItsEntries()
    {
        var createPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, "/Admin/Forms/Lookups/Create");

        // Built from the rendered page's own fields, so whatever hidden or display-only field the
        // form carries rides along exactly as a browser would send it.
        var fields = RenderedForm.FieldsOf(createPage)
            .With("Code", "ui-grade")
            .With("NameEn", "Grade")
            .With("NameAr", "الدرجة")
            .With("Items[0].Code", "G1")
            .With("Items[0].NameEn", "Grade 1")
            .With("Items[0].NameAr", "الدرجة ١");

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Forms/Lookups/Create",
            new FormUrlEncodedContent(fields));

        response.EnsureSuccessStatusCode();

        // The Razor HTML encoder escapes non-ASCII to numeric character references; decoded, they
        // are the same Arabic text a browser would show.
        var html = HtmlDecode(await response.Content.ReadAsStringAsync());

        html.Should().Contain("ui-grade");
        html.Should().Contain("الدرجة");
        html.Should().Contain("dir=\"rtl\"");

        await _fixture.InTenantAsync(async services =>
        {
            var list = await services.GetRequiredService<ILookupListService>().GetByCodeAsync("ui-grade");

            list.Should().NotBeNull();
            list!.Items.Should().ContainSingle().Which.Code.Should().Be("G1");
            list.IsSystemDefined.Should().BeFalse("a list invented on the screen belongs to the tenant");
        });
    }

    /// <summary>
    /// A stored entry's row offers no Remove button and a read-only code; a new row offers both.
    /// </summary>
    /// <remarks>
    /// The affordance half of ADR-0014's rule. The service refuses a dropped entry whatever the
    /// screen allows, but a reader who could press Remove and then be refused would reasonably
    /// conclude the product was broken rather than that the rule exists.
    /// </remarks>
    [Fact]
    public async Task AStoredEntryOffersNoRemoveButtonAndANewOneDoes()
    {
        string listId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<ILookupListService>().CreateAsync(
                "ui-affordance",
                new BilingualText("Affordance", "إتاحة"),
                [new LookupListItem("STORED", new BilingualText("Stored", "محفوظ"), SortOrder: 10)]);

            created.Succeeded.Should().BeTrue(created.Describe());
            listId = created.Value!.LookupListId;
        });

        var editPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Forms/Lookups/Edit/{listId}");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(editPage);

        var storedRow = document.QuerySelector("tr.lookup-item-row[data-item-code='STORED']");

        storedRow.Should().NotBeNull();
        storedRow!.GetAttribute("data-item-stored").Should().Be("true");
        storedRow.QuerySelector(".lookup-item-remove").Should().BeNull("a stored entry can never be removed");
        storedRow.QuerySelector("input[name='Items[0].Code']")!
            .HasAttribute("readonly").Should().BeTrue("its code is what records hold");

        // Add a row: the new one is removable, and the stored one still is not.
        var fields = RenderedForm.FieldsOf(editPage);
        fields.Add(new KeyValuePair<string, string>("command", "add"));

        var added = await _fixture.Administrator.PostAsync(
            $"/Admin/Forms/Lookups/Edit/{listId}",
            new FormUrlEncodedContent(fields));

        added.EnsureSuccessStatusCode();

        var afterAdd = new AngleSharp.Html.Parser.HtmlParser()
            .ParseDocument(await added.Content.ReadAsStringAsync());

        afterAdd.QuerySelectorAll("tr.lookup-item-row").Should().HaveCount(2);
        afterAdd.QuerySelectorAll("tr.lookup-item-row[data-item-stored='false'] .lookup-item-remove")
            .Should().ContainSingle("the new row can still be taken off the form");
    }

    /// <summary>
    /// A posted set that drops a stored entry is refused by the service, with the message under the
    /// form rather than as a 500.
    /// </summary>
    /// <remarks>
    /// The screen does not offer this, so the only way to reach it is a hand-made request — which
    /// is exactly what the service-level rule exists for. A validation problem is an answer, not a
    /// failure.
    /// </remarks>
    [Fact]
    public async Task AHandMadePostThatDropsAStoredEntryIsRefusedOnTheForm()
    {
        string listId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<ILookupListService>().CreateAsync(
                "ui-handmade",
                new BilingualText("Hand made", "يدوي"),
                [new LookupListItem("KEEP", new BilingualText("Keep", "إبقاء"), SortOrder: 10)]);

            created.Succeeded.Should().BeTrue(created.Describe());
            listId = created.Value!.LookupListId;
        });

        var editPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Forms/Lookups/Edit/{listId}");

        // Every field except the entry row: a browser cannot produce this, and the service must
        // still refuse it.
        var fields = RenderedForm.FieldsOf(editPage)
            .Where(field => !field.Key.StartsWith("Items[", StringComparison.Ordinal))
            .ToList();

        var response = await _fixture.Administrator.PostAsync(
            $"/Admin/Forms/Lookups/Edit/{listId}",
            new FormUrlEncodedContent(fields));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a refused write redisplays the form");

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("KEEP");
        html.Should().Contain("Retire it instead");

        await _fixture.InTenantAsync(async services =>
        {
            var list = await services.GetRequiredService<ILookupListService>().GetByCodeAsync("ui-handmade");

            list!.Items.Should().ContainSingle("nothing was written on the way to refusing");
        });
    }

    /// <summary>
    /// A list with entries is not offered a Delete button, and asking for one anyway lands back on
    /// the list with the reason rather than on a confirmation page for something that cannot happen.
    /// </summary>
    [Fact]
    public async Task APopulatedListIsNeitherOfferedNorGivenADeleteConfirmation()
    {
        string listId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<ILookupListService>().CreateAsync(
                "ui-undeletable",
                new BilingualText("Undeletable", "غير قابلة للحذف"),
                [new LookupListItem("X", new BilingualText("Something", "شيء"))]);

            created.Succeeded.Should().BeTrue(created.Describe());
            listId = created.Value!.LookupListId;
        });

        var index = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Forms/Lookups/Index");

        var row = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(index)
            .QuerySelector("tr.lookup-list-row[data-lookup-list-code='ui-undeletable']");

        row.Should().NotBeNull();
        row!.QuerySelector(".lookup-list-delete").Should().BeNull();

        var confirmation = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Forms/Lookups/Delete/{listId}");

        // Redirected back to the list with the reason, rather than shown a confirmation page for
        // something that cannot happen. Asserted on the row and on the warning rather than on the
        // absence of the string anywhere on the page: other tests in this collection share the
        // tenant and may legitimately leave an empty list that does offer Delete.
        var afterRedirect = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(confirmation);

        afterRedirect.QuerySelector("#lookup-list-delete")
            .Should().BeNull("there is no confirmation form to show");

        afterRedirect.QuerySelector("tr.lookup-list-row[data-lookup-list-code='ui-undeletable'] .lookup-list-delete")
            .Should().BeNull();

        confirmation.Should().Contain("cannot be deleted");
    }

    /// <summary>
    /// A signed-in reader without <c>ManageLookupLists</c> is refused in the UI, not only in the
    /// service — and sees no Forms entry at all.
    /// </summary>
    [Fact]
    public async Task AReaderWithoutThePermissionIsRefusedAndOfferedNothing()
    {
        var auditor = await _fixture.CreateSignedInClientAsync(
            "lookup-auditor",
            "Workmate!Lookup1",
            PlatformRoles.Auditor,
            allowAutoRedirect: false);

        var shell = await auditor.GetAsync("/Admin");
        var shellHtml = await shell.Content.ReadAsStringAsync();

        shellHtml.Should().NotContain(
            "/Admin/Forms/Index",
            "a menu entry to a screen somebody cannot open tells them only that the product is withholding a feature");

        foreach (var path in new[] { "/Admin/Forms/Index", "/Admin/Forms/Lookups/Index", "/Admin/Forms/Lookups/Create" })
        {
            var response = await auditor.GetAsync(path);

            response.StatusCode.Should().NotBe(HttpStatusCode.OK, "{0} needs ManageLookupLists", path);
        }
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup list admin test"))
            {
                await work(services);
            }
        });
}
