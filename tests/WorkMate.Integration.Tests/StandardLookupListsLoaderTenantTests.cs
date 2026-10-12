using System.Globalization;
using System.Net;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Platform;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The Load standard lists button on People → Forms: what it offers, what it does, and who may
/// press it.
/// </summary>
/// <remarks>
/// <b>On a tenant that has never had the lists</b>, which is the tenant the button exists for: one
/// created before slice B2, carrying the bindings and none of the entries. The base recipe applies
/// the same file at setup, so this fixture withholds it — see
/// <see cref="BaseTenantFixture.WithholdingRecipes"/>.
///
/// The end-to-end assertion is the one that matters, and it is deliberately made of clicks rather
/// than of services: render the Add employee screen and find a text box, press the button, render
/// the same screen and find a drop-down, all in one process with nothing restarted. That is the
/// defect this slice was reported for, stated in the terms it was reported in.
/// </remarks>
public sealed class StandardLookupListsLoaderTenantTests : IAsyncLifetime, IDisposable
{
    private BaseTenantFixture _tenant = default!;

    public void Dispose() => _tenant?.Dispose();

    public async Task InitializeAsync()
    {
        _tenant = BaseTenantFixture.WithholdingRecipes(StandardLookupListsLoader.RecipeName);

        await _tenant.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await _tenant.DisposeAsync();

    /// <summary>
    /// Press the button, and the bound field is a drop-down on the next render. No restart.
    /// </summary>
    [Fact]
    public async Task PressingTheButtonTurnsABoundFieldFromATextBoxIntoADropDown()
    {
        (await NationalityControlAsync()).Should().Be(
            "INPUT",
            "a tenant with no lists goes on offering free text, which is the fallback the binding "
            + "was shipped with");

        await PressLoadStandardListsAsync();

        (await NationalityControlAsync()).Should().Be(
            "SELECT",
            "the lists are loaded, so the field offers them — in this process, with nothing "
            + "restarted, which is the defect this was written for");
    }

    /// <summary>
    /// The card names what is missing while something is, and is gone once nothing is.
    /// </summary>
    /// <remarks>
    /// Both halves in one test because they are one rule: the button is a thing a tenant needs once.
    /// A button that stays on the screen after it has done its work invites a second press that
    /// does nothing, and teaches people that buttons on this screen do nothing.
    /// </remarks>
    [Fact]
    public async Task TheCardIsOfferedOnlyWhileAStandardListIsMissing()
    {
        var before = await FormsPageAsync();

        before.QuerySelector("#forms-standard-lists").Should().NotBeNull(
            "ten standard lists are missing on this tenant");

        before.QuerySelector("#forms-standard-lists-missing")!.TextContent
            .Should().Contain(StandardLookupLists.Grade).And.Contain(StandardLookupLists.Nationality);

        await PressLoadStandardListsAsync();

        var after = await FormsPageAsync();

        after.QuerySelector("#forms-standard-lists").Should().BeNull(
            "nothing is missing any more, so there is nothing to offer");

        after.QuerySelector("#forms-lookup-list-count")!.TextContent
            .Should().Contain(StandardLookupLists.All.Count.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Every list the module ships is loaded, as the recipe writes them, through the service.
    /// </summary>
    [Fact]
    public async Task EveryStandardListArrivesAndIsMarkedAsOneWorkMateShips()
    {
        await PressLoadStandardListsAsync();

        await _tenant.InTenantAsync(async services =>
        {
            var loader = services.GetRequiredService<IStandardLookupListsLoader>();

            (await loader.MissingAsync()).Should().BeEmpty();

            var lists = await services.GetRequiredService<ILookupListService>().ListAsync();

            foreach (var code in StandardLookupLists.All)
            {
                var list = lists.SingleOrDefault(candidate => candidate.Code == code);

                list.Should().NotBeNull("'{0}' is one of the lists this module ships", code);

                list!.IsSystemDefined.Should().BeTrue(
                    "a list that arrives in a recipe is WorkMate's, so a customer may extend it and "
                    + "may not delete it");
            }
        });
    }

    /// <summary>
    /// Pressing it twice changes nothing and says so.
    /// </summary>
    /// <remarks>
    /// Two people pressing it at once, or one person pressing it and then refreshing, is the
    /// ordinary case rather than the exotic one. The recipe step's ADR-0008 comparison is what makes
    /// it safe; this asserts that the screen in front of it does not undo that with a second write
    /// or a misleading message.
    /// </remarks>
    [Fact]
    public async Task PressingItTwiceIsSafeAndSaysNothingChanged()
    {
        await PressLoadStandardListsAsync();

        var second = await PressLoadStandardListsAsync();

        second.Should().Contain(
            "already loaded",
            "a second press has nothing to do, and the screen says so rather than claiming to have "
            + "loaded them again");

        await _tenant.InTenantAsync(async services =>
            (await services.GetRequiredService<ILookupListService>().ListAsync())
                .Count(list => list.Code == StandardLookupLists.Grade)
                .Should().Be(1, "re-running the recipe must not create a second copy of a list"));
    }

    /// <summary>
    /// Somebody without the permission is not offered the button, and the service refuses the work
    /// whoever asks for it.
    /// </summary>
    /// <remarks>
    /// Two assertions because they are two different guarantees, and CLAUDE.md rule 5 asks for the
    /// second one specifically: the screen not offering a button is the screen being honest, and a
    /// hidden button is not a permission check. The service is asked with no user and no system
    /// authority — the state every background job and every future API call starts in — and has to
    /// refuse on its own.
    ///
    /// The refusal is asserted at the service rather than by posting as the under-permissioned user,
    /// because an antiforgery token belongs to the session it was issued to: posting one client's
    /// token with another client's cookies is rejected as a forgery before any permission is
    /// consulted, which would make this test pass without ever reaching the thing it is about.
    /// </remarks>
    [Fact]
    public async Task WithoutThePermissionTheButtonIsNeitherOfferedNorHonoured()
    {
        var outsider = await _tenant.CreateSignedInClientAsync(
            "forms-outsider", "Workmate!Outsider1", PlatformRoles.Auditor, allowAutoRedirect: false);

        var forms = await outsider.GetAsync("/Admin/Forms/Index");

        forms.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "the Forms screen, and so the button on it, needs ManageLookupLists");

        await _tenant.InTenantAsync(async services =>
        {
            var loader = services.GetRequiredService<IStandardLookupListsLoader>();

            var result = await loader.LoadAsync();

            result.IsAuthorised.Should().BeFalse(
                "with no user and no system authority the service refuses, rather than treating the "
                + "absence of a caller as permission");

            (await loader.MissingAsync()).Should().NotBeEmpty("nothing was loaded");
        });
    }

    /// <summary>The tag name of the nationality control on the Add employee screen.</summary>
    private async Task<string> NationalityControlAsync()
    {
        var html = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin/Employees/Create");

        var control = new HtmlParser().ParseDocument(html).QuerySelector("#NationalityCode");

        control.Should().NotBeNull("the Add employee screen has a nationality control");

        return control!.TagName;
    }

    private async Task<AngleSharp.Dom.IDocument> FormsPageAsync() =>
        new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin/Forms/Index"));

    /// <summary>Presses the button the way a person does, and returns the page that comes back.</summary>
    private async Task<string> PressLoadStandardListsAsync()
    {
        var page = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin/Forms/Index");

        var response = await _tenant.Administrator.PostAsync(
            "/Admin/Forms/LoadStandardLists",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = BaseTenantFixture.AntiforgeryTokenIn(page),
            }));

        response.IsSuccessStatusCode.Should().BeTrue(
            $"pressing Load standard lists returned {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync();
    }
}
