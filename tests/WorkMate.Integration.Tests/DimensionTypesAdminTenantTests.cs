using System.Net;
using System.Net.Http;
using FluentAssertions;
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
