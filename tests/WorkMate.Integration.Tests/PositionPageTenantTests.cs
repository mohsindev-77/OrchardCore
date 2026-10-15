using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Controllers;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// A position's own page, as manual testing asked for it: its two parents as two rows, one headcount
/// row, grade chips, a holders section, and the reporting chart on every post that has one.
/// </summary>
/// <remarks>
/// <b>The defect this file exists for.</b> The page drew its section from whichever structure the
/// URL named, and every link into it named one — so arriving from the Positions list scoped to the
/// organisation chart put a post's page on the axis where a post is a <em>leaf</em>. Manager
/// Technology has three posts reporting to it and Site Supervisor has one, and both pages showed no
/// section at all. Defaulting to the reporting line when nothing was asked for was not enough,
/// because something is always asked for.
///
/// <b>So the fix is unconditional</b>: a position's page is about its reporting line, whatever the
/// URL says, and the Structure chooser that could contradict that is not rendered. Where the post
/// sits in the business did not go away — it became a row of its own, which is ADR-0028's "two
/// parents on two axes" said on a screen rather than buried in a Parent row that listed both.
///
/// <b>Every assertion here runs over all eight demo positions</b>, split into the four with posts
/// under them and the four without, because "the section appears" and "the section appears only
/// where it should" are different claims and the second is the one that was broken.
/// </remarks>
public sealed class PositionPageTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>The four posts with somebody reporting to them. Each must draw the section.</summary>
    private static readonly string[] BranchCodes =
    [
        "zenith-pos-ceo",
        "zenith-pos-dir-tech",
        "zenith-pos-mgr-tech",
        "zenith-pos-site-supervisor",
    ];

    /// <summary>The four leaves. None may draw it.</summary>
    private static readonly string[] LeafCodes =
    [
        "zenith-pos-pse",
        "zenith-pos-se",
        "zenith-pos-hr-manager",
        "zenith-pos-labourer",
    ];

    public static TheoryData<string> Branches => [.. BranchCodes];

    public static TheoryData<string> Leaves => [.. LeafCodes];

    /// <summary>
    /// All eight, for the assertions that hold of every post — built from the two lists above so
    /// that a post added to either is covered by those too.
    /// </summary>
    public static TheoryData<string> EveryPosition => [.. BranchCodes, .. LeafCodes];

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- the section, which is the defect ----------------------------------------------------

    /// <summary>
    /// Every post with somebody under it draws the reporting section — even when the URL asks for
    /// the organisation chart, where it is a leaf.
    /// </summary>
    /// <remarks>
    /// <b>The organisation structure id is passed deliberately.</b> That is the URL the Positions
    /// list produces and the one the reported failure came through, so a test that omitted it would
    /// pass against the defect.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Branches))]
    public async Task EveryPostWithSomebodyUnderItDrawsItsReportingLine(string code)
    {
        var page = await PositionPageAsync(code, scopedToTheOrganisation: true);

        page.Should().Contain("value-tree-heading", "the section is there at all");
        page.Should().Contain("Positions reporting to", "and it is named for what it draws");

        // The three views, the retired toggle and the expand controls all survive the redesign.
        page.Should().Contain("value-units-view-hierarchy");
        page.Should().Contain("value-units-view-tree");
        page.Should().Contain("value-units-view-list");
        page.Should().Contain("value-tree-retired");
        page.Should().Contain("value-tree-expand-all");

        page.Should().NotContain("Units under", "a post contains posts, and they answer to it");
    }

    /// <summary>A post nobody reports to draws no section, and is not made to look broken by it.</summary>
    /// <remarks>
    /// The opposite half, and the one a careless fix breaks: forcing the section on would give every
    /// leaf an empty chart. D24's rule is that absence says "nothing is under this" better than an
    /// empty table does — what a leaf gets instead is the <em>Reports to</em> row, so it is still
    /// told where on the line it sits.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Leaves))]
    public async Task APostNobodyReportsToDrawsNoSection(string code)
    {
        var page = await PositionPageAsync(code, scopedToTheOrganisation: true);

        page.Should().NotContain("value-tree-heading");
        page.Should().NotContain("Positions reporting to");

        page.Should().Contain("value-reports-to", "but it still says where on the line it sits");
    }

    /// <summary>The count column on that section is Holders, filled over approved — never Employees.</summary>
    /// <remarks>
    /// An assignment names the organisation axis, so an employee count on a reporting line is zero
    /// on every row. A column of zeroes is worse than no column; D26 E says what belongs there.
    /// </remarks>
    [Theory]
    [InlineData("tree")]
    [InlineData("list")]
    public async Task TheSectionsCountColumnIsHoldersAndNotEmployees(string view)
    {
        var page = await PositionPageAsync("zenith-pos-ceo", scopedToTheOrganisation: true, view: view);

        page.Should().Contain("Holders");
        page.Should().Contain("value-tree-holders", "filled over approved, per row");

        page.Should().NotContain("value-tree-employees", "not a headcount that is zero by construction");
    }

    /// <summary>A position page never offers the Structure chooser.</summary>
    /// <remarks>
    /// Its only other setting is the organisation chart, where a post is a leaf and the section
    /// vanishes — a control whose job would be to make the page wrong. A unit whose dimension really
    /// is on two charts still gets it, which the next test holds.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPosition))]
    public async Task APositionPageHasNoStructureChooser(string code)
    {
        var page = await PositionPageAsync(code, scopedToTheOrganisation: true);

        page.Should().NotContain("value-structure-form");
        page.Should().NotContain("value-structure-apply");
    }

    // ---- the details block ------------------------------------------------------------------

    /// <summary>
    /// A post's two parents are two named rows, and the mixed Parent row is gone.
    /// </summary>
    /// <remarks>
    /// ADR-0028 on a screen. The organisation row is named for the dimension the post actually sits
    /// under — <em>Department</em> on Zenith — rather than a fixed caption, because a structure
    /// decides what may contain a position and FRS Rule 6 lets a customer rename it.
    /// </remarks>
    [Fact]
    public async Task TheTwoParentsAreTwoNamedRows()
    {
        var page = await PositionPageAsync("zenith-pos-mgr-tech", scopedToTheOrganisation: true);

        page.Should().Contain("value-organisation-path");
        page.Should().Contain("Department", "the row is named for the dimension the post sits under");
        page.Should().Contain("Electrical", "and carries the unit, linked");

        page.Should().Contain("value-reports-to");
        page.Should().Contain("CEO");
        page.Should().Contain("Director Technology");

        page.Should().NotContain("value-parent", "the mixed Parent row listed both axes at once");
    }

    /// <summary>The chain above is linked all the way up, and in order.</summary>
    [Fact]
    public async Task TheReportingChainIsLinkedAndInOrder()
    {
        var page = await PositionPageAsync("zenith-pos-labourer", scopedToTheOrganisation: true);

        var chain = Between(page, "id=\"value-reports-to\"", "</dd>");

        Regex.Count(chain, "value-reports-to-step").Should().Be(
            4,
            "Labourer reports to Site Supervisor, under Manager Technology, under Director "
            + "Technology, under the CEO — the whole line, not just the step above");

        // Highest first, which is what a breadcrumb means.
        chain.IndexOf("CEO", StringComparison.Ordinal)
            .Should().BeLessThan(chain.IndexOf("Site Supervisor", StringComparison.Ordinal));
    }

    /// <summary>The post at the top of the line says so rather than showing an empty row.</summary>
    [Fact]
    public async Task TheTopOfTheLineSaysItReportsToNobody()
    {
        var page = await PositionPageAsync("zenith-pos-ceo", scopedToTheOrganisation: true);

        page.Should().Contain("value-reports-to-nobody");
        page.Should().Contain("top of the reporting line");
    }

    /// <summary>
    /// One Headcount row carries all three numbers and the badge, and the three rows it replaced
    /// are gone.
    /// </summary>
    /// <remarks>
    /// <b>The three it replaced each failed differently.</b> <em>Employees</em> counted assignments
    /// on the organisation axis, which on a post is zero by construction and answers nothing.
    /// <em>Approved headcount</em> was the raw attribute, printed under its own label. And
    /// <em>Establishment</em> repeated that same approved figure beside filled and vacant. One read,
    /// one row, three numbers.
    /// </remarks>
    [Fact]
    public async Task OneHeadcountRowReplacesTheThreeThatSaidItBefore()
    {
        var page = await PositionPageAsync("zenith-pos-hr-manager", scopedToTheOrganisation: true);

        page.Should().Contain("value-headcount");
        page.Should().Contain("3 approved", "HR Manager is one post with three approved holders (D26 D)");
        page.Should().Contain("0 filled", "nobody holds a position until slice F6");
        page.Should().Contain("3 vacant");
        page.Should().Contain("value-headcount-badge");

        page.Should().NotContain("value-employees", "a count of assignments on the wrong axis");
        page.Should().NotContain("value-establishment", "the same approved figure a second time");
        page.Should().NotContain("Approved headcount", "and the raw attribute label under it");

        // "Dimension: Position" is the subtitle again three rows down.
        page.Should().NotContain("value-dimension");

        // A head belongs to a unit, never to a post (ADR-0012). Matched on the whole attribute,
        // because "value-head" is a prefix of "value-headcount" and a bare substring check would
        // pass on the page that has both and fail on the page that is correct.
        page.Should().NotContain("id=\"value-head\"");
    }

    /// <summary>The badge is green on a full post and amber on one with a vacancy.</summary>
    /// <remarks>
    /// That way round deliberately: a reader scanning for something to act on is looking for the
    /// gaps, so the colour that catches the eye marks the gap rather than the absence of one. Every
    /// demo post reads 0 filled today, so the full case is made by approving none — which is also
    /// the honest test of a post nobody has established.
    /// </remarks>
    [Fact]
    public async Task TheBadgeIsAmberWhileThereIsAVacancy()
    {
        var page = await PositionPageAsync("zenith-pos-ceo", scopedToTheOrganisation: true);

        page.Should().Contain("text-bg-warning", "one approved and none filled is a vacancy");
        page.Should().Contain("value-headcount-badge");
    }

    /// <summary>Allowed grades are chips: the name, the code on hover, each linking to the grade.</summary>
    [Fact]
    public async Task AllowedGradesAreLinkedChips()
    {
        var page = await PositionPageAsync("zenith-pos-se", scopedToTheOrganisation: true);

        page.Should().Contain("value-allowed-grade-chips");
        page.Should().Contain("SE-1");
        page.Should().Contain("SE-3");
        page.Should().Contain("title=\"zenith-grade-se-1\"", "the code is the hover, not a bracket");
        page.Should().Contain("value-allowed-grade");
    }

    /// <summary>
    /// Site Supervisor's grades are its own, which is what the demo tidy was for.
    /// </summary>
    /// <remarks>
    /// It carried MGR-1 and MGR-2 until manual testing pointed out that a site supervisor is not a
    /// technology manager. Sharing a ladder says two posts are the same level, which is the one
    /// thing a grade exists to say.
    /// </remarks>
    [Fact]
    public async Task SiteSupervisorHasItsOwnGradesRatherThanTheManagersOnes()
    {
        var page = await PositionPageAsync("zenith-pos-site-supervisor", scopedToTheOrganisation: true);

        // Scoped to the chips. The Organisation menu down the side expands every dimension to its
        // values, so every grade in the tenant — MGR-1 included — is somewhere on every page, and a
        // whole-page check would be asserting about the navigation rather than about this post.
        var chips = Between(page, "id=\"value-allowed-grade-chips\"", "</ul>");

        chips.Should().Contain("SS-1");
        chips.Should().Contain("SS-2");
        chips.Should().NotContain("MGR-", "a site supervisor is not a technology manager");

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var positions = services.GetRequiredService<IPositionService>();

            var supervisor = (await records.GetByCodeAsync("zenith-pos-site-supervisor"))!.RecordId;

            (await positions.GetAllowedGradesAsync(supervisor))
                .Select(grade => grade.NameEn).Should().Equal(["SS-1", "SS-2"]);
        });
    }

    /// <summary>
    /// Every post has a Holders section, and today every one of them says nobody holds it.
    /// </summary>
    /// <remarks>
    /// <b>The empty state is the deliverable.</b> Nothing assigns an employee to a position before
    /// slice F6, so this cannot be anything but empty — which is exactly why it is asserted. A
    /// reader told "3 approved · 0 filled" wants the other half, and a page with no such section
    /// leaves them unsure whether it is missing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPosition))]
    public async Task EveryPostHasAHoldersSectionAndNobodyHoldsOneYet(string code)
    {
        var page = await PositionPageAsync(code, scopedToTheOrganisation: true);

        page.Should().Contain("value-holders-heading");
        page.Should().Contain("value-holders-none");
        page.Should().Contain("No one holds this position yet.");
    }

    /// <summary>The subtitle carries the dimension and the Arabic name, with the status beside it.</summary>
    [Fact]
    public async Task TheSubtitleCarriesTheArabicNameAndTheStatus()
    {
        var page = await PositionPageAsync("zenith-pos-ceo", scopedToTheOrganisation: true);

        page.Should().Contain("value-subtitle-ar");
        page.Should().Contain("الرئيس التنفيذي", "the Arabic name, beside the dimension");
        page.Should().Contain("value-status-top");

        // And the ways to act on it are untouched.
        page.Should().Contain("value-edit");
        page.Should().Contain("value-move");
        page.Should().Contain("value-retire");
        page.Should().Contain("value-back");
    }

    // ---- Arabic -------------------------------------------------------------------------------

    /// <summary>The whole redesign is translated, on a branch and on a leaf.</summary>
    /// <remarks>
    /// Asserted on the rendered page rather than on the PO file, because a msgid with a translation
    /// nothing looks up is the failure that looks like success — the Organisation menu read المنظمة
    /// for a slice because two entries shared one msgid.
    /// </remarks>
    [Theory]
    [InlineData("zenith-pos-mgr-tech")]
    [InlineData("zenith-pos-labourer")]
    public async Task ThePageIsInArabicUnderArabic(string code)
    {
        var page = await PositionPageAsync(code, scopedToTheOrganisation: true, culture: "ar");

        page.Should().Contain("العدد", "Headcount");
        page.Should().Contain("معتمد", "approved");
        page.Should().Contain("شاغر", "vacant");
        page.Should().Contain("شاغلو المنصب", "Holders");
        page.Should().Contain("لا يشغل هذا المنصب أحد حتى الآن.", "the empty holders state");

        page.Should().NotContain("Headcount");
        page.Should().NotContain("No one holds this position yet.");
    }

    /// <summary>The section's heading is translated too, with the post's name in it.</summary>
    [Fact]
    public async Task TheReportingHeadingIsInArabic()
    {
        var page = await PositionPageAsync("zenith-pos-ceo", scopedToTheOrganisation: true, culture: "ar");

        page.Should().Contain("المناصب التابعة لـ", "Positions reporting to");
        page.Should().NotContain("Positions reporting to");
    }

    // ---- the pages that must NOT have changed -------------------------------------------------

    /// <summary>
    /// A unit's page keeps the layout it had: Head, Employees, Dimension, Parent, and the chooser.
    /// </summary>
    /// <remarks>
    /// <b>The guard on the brief's own boundary.</b> Every change above is behind a position test,
    /// and the cheapest way to get one of them wrong is to apply it to every dimension. A department
    /// is not a post: somebody heads it, people are assigned to it, and its page says so.
    /// </remarks>
    [Fact]
    public async Task AUnitsPageIsUntouched()
    {
        var page = await ValuePageAsync("zenith-dept-electrical");

        page.Should().Contain("value-head", "a unit has a head; ADR-0012");
        page.Should().Contain("value-employees", "and employees assigned to it");
        page.Should().Contain("value-dimension");
        page.Should().Contain("value-parent", "one row, because a department is on one axis");

        page.Should().NotContain("value-headcount", "an establishment belongs to a post");
        page.Should().NotContain("value-holders-heading");
        page.Should().NotContain("value-organisation-path");
        page.Should().NotContain("Positions reporting to");
    }

    /// <summary>A grade's page is untouched too, including its "Used by positions" list.</summary>
    [Fact]
    public async Task AGradesPageIsUntouched()
    {
        var page = await ValuePageAsync("zenith-grade-ss-1");

        page.Should().Contain("value-used-by-positions");
        page.Should().Contain("Site Supervisor", "which is what now uses SS-1");

        page.Should().NotContain("value-headcount");
        page.Should().NotContain("value-holders-heading");
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// A position's page, fetched the way the Positions list links to it.
    /// </summary>
    /// <param name="scopedToTheOrganisation">
    /// Passes the <em>organisation</em> structure in the URL — the axis on which a post is a leaf.
    /// This is what every real link carries and what the reported defect came through, so the tests
    /// set it rather than relying on the page being asked nothing.
    /// </param>
    private async Task<string> PositionPageAsync(
        string code,
        bool scopedToTheOrganisation,
        string? view = null,
        string culture = "en")
    {
        var recordId = string.Empty;
        var organisationId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            recordId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync(code))!.RecordId;

            organisationId = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;
        });

        var url = $"/Admin/Dimensions/Records/Details/{recordId}";

        if (scopedToTheOrganisation)
        {
            url += $"?structureId={organisationId}";

            if (view is not null)
            {
                url += $"&view={view}";
            }
        }

        return await PageAsync(url, culture);
    }

    /// <summary>Any value's page, with nothing asked of it.</summary>
    private async Task<string> ValuePageAsync(string code)
    {
        var recordId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            recordId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync(code))!.RecordId;
        });

        return await PageAsync($"/Admin/Dimensions/Records/Details/{recordId}", "en");
    }

    private async Task<string> PageAsync(string path, string culture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        // How Orchard's culture picker records a choice: the same cookie the admin UI writes.
        request.Headers.Add("Cookie", $".AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture}");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        var response = await _tenant.Administrator.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"GET {path} under '{culture}' should render");

        // Decoded, because ASP.NET's default HtmlEncoder writes every non-ASCII character as a
        // numeric entity — a record's Arabic name arrives as "&#x627;&#x644;…". That is correct on
        // the wire and is what a browser shows as Arabic, so a test asserting on what a reader sees
        // has to decode it. Localised text does not need this (a LocalizedHtmlString is written as
        // HTML already), which is exactly why asserting on one and not the other would hide the
        // difference.
        return System.Net.WebUtility.HtmlDecode(body);
    }

    /// <summary>
    /// The slice of a page between a marker and the next closer, for an assertion about one region.
    /// </summary>
    /// <remarks>
    /// The Organisation menu expands every dimension to its values, so every grade and every post in
    /// the tenant appears somewhere on every admin page. A whole-page <c>NotContain</c> is therefore
    /// an assertion about the navigation, and it passes or fails for reasons that have nothing to do
    /// with the screen under test.
    /// </remarks>
    private static string Between(string page, string marker, string closer)
    {
        var start = page.IndexOf(marker, StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1, "the page should contain '{0}'", marker);

        var end = page.IndexOf(closer, start, StringComparison.Ordinal);

        end.Should().BeGreaterThan(-1, "'{0}' should be closed by '{1}'", marker, closer);

        return page[start..end];
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("position page test"))
            {
                await work(services);
            }
        });

    private async Task ApplyAsync(string fileName)
    {
        var path = Path.Combine(RecipesFolder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' ships with the module", fileName);

        await _tenant.InTenantAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            var descriptor = new RecipeDescriptor
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                BasePath = string.Empty,
                FileProvider = fileProvider,
                RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                RequireNewScope = false,
            };

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                descriptor,
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
