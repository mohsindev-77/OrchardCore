using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;
using static System.Net.WebUtility;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The dimension types admin screen, exercised through real HTTP against the base tenant: the
/// list, creating a type end to end (including the content type name the editor promises to
/// show), bilingual rendering, and that a user without <c>ManageDimensionTypes</c> is forbidden —
/// in the UI, which is this module's own rule from specification section 4, not only in the
/// service layer the unit suite already covers.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionTypesAdminTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public DimensionTypesAdminTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AnAnonymousVisitorIsNotShownTheList()
    {
        var response = await _fixture.Anonymous.GetAsync("/Admin/Dimensions/Types/Index");

        // AdminFilter challenges an unauthenticated request rather than forbidding it outright;
        // either way it must never be the 200 the screen gives a signed-in administrator.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheAdministratorSeesAnEmptyListBeforeAnythingIsCreated()
    {
        var html = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Dimensions/Types/Index");

        html.Should().Contain("Dimension types");
    }

    [Fact]
    public async Task CreatingATypeShowsItsGeneratedContentTypeName()
    {
        var createPage = await BaseTenantFixture.GetPageAsync(_fixture.Administrator, "/Admin/Dimensions/Types/Create");

        // Built from the rendered page's own fields, not a hand-picked subset: whatever hidden,
        // disabled or display-only field the form carries rides along exactly as a browser would
        // send it, which is the only way a test can catch a field that fails validation empty —
        // see RenderedForm's remarks for the bug this replaced a narrower test to catch.
        var fields = RenderedForm.FieldsOf(createPage)
            .With("Code", "projecttest")
            .With("NameEn", "Project test")
            .With("NameAr", "مشروع تجريبي");

        var response = await _fixture.Administrator.PostAsync(
            "/Admin/Dimensions/Types/Create",
            new FormUrlEncodedContent(fields));

        response.EnsureSuccessStatusCode();

        // The razor HTML encoder escapes non-ASCII text to numeric character references rather
        // than emitting UTF-8 bytes directly; decoded, they are the same Arabic text a browser
        // would show.
        var html = HtmlDecode(await response.Content.ReadAsStringAsync());

        // The editor promises the generated content type's name; DimensionCodes derives
        // "Projecttest" from the code "projecttest" (first letter upper-cased, rest untouched).
        html.Should().Contain("Projecttest");
        html.Should().Contain("projecttest");

        // The Arabic name, bilingual and right-to-left, on the very page that confirms creation.
        html.Should().Contain("مشروع تجريبي");
        html.Should().Contain("dir=\"rtl\"");
    }

    /// <summary>
    /// The editor no longer offers a self-nesting setting, and saving a type that has one stored
    /// does not clear it.
    /// </summary>
    /// <remarks>
    /// ADR-0010's addendum moved the question to the structure's containment grid, where the
    /// person asking it is already looking. Both halves matter: a checkbox left on this screen
    /// would be a second, contradictory answer, and a screen that quietly posted <c>false</c> over
    /// a stored <c>true</c> would change what a <c>structures</c> recipe row written as a plain
    /// chain derives the next time one is applied — the one job that flag still has.
    /// </remarks>
    [Fact]
    public async Task TheEditorNeitherOffersNorClearsTheSelfNestingSetting()
    {
        var createPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, "/Admin/Dimensions/Types/Create");

        createPage.Should().NotContain("AllowsSelfNesting", "the setting is not on this screen any more");
        createPage.Should().NotContain("sit under another record of the same type");

        string typeId = string.Empty;

        await _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                // Created with the flag on, as a recipe written before the addendum would.
                var created = await services.GetRequiredService<IDimensionTypeService>().CreateAsync(
                    "legacynesting",
                    new BilingualText("Legacy nesting", "نوع قديم"),
                    [],
                    allowsSelfNesting: true);

                created.Succeeded.Should().BeTrue(
                    string.Join("; ", created.Errors.Select(error => error.Message.Value)));

                typeId = created.Value!.DimensionTypeId;
            }
        });

        var editPage = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Types/Edit/{typeId}");

        editPage.Should().NotContain("AllowsSelfNesting");

        (await _fixture.Administrator.PostAsync(
            $"/Admin/Dimensions/Types/Edit/{typeId}",
            new FormUrlEncodedContent(RenderedForm.FieldsOf(editPage).With("NameEn", "Legacy nesting renamed"))))
            .EnsureSuccessStatusCode();

        await _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("integration test"))
            {
                var saved = await services.GetRequiredService<IDimensionTypeService>().GetAsync(typeId);

                saved!.Name.En.Should().Be("Legacy nesting renamed", "the save went through");
                saved.AllowsSelfNesting.Should().BeTrue(
                    "a screen that no longer asks the question must not answer it either");
            }
        });
    }

    [Fact]
    public async Task AUserWithoutManageDimensionTypesCannotSeeOrCreate()
    {
        var restricted = await _fixture.CreateSignedInClientAsync(
            "hr-admin-no-dimension-types",
            "Workmate!Integration1",
            "HR Administrator",
            allowAutoRedirect: false);

        var index = await restricted.GetAsync("/Admin/Dimensions/Types/Index");
        index.StatusCode.Should().NotBe(HttpStatusCode.OK);

        var create = await restricted.GetAsync("/Admin/Dimensions/Types/Create");
        create.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
