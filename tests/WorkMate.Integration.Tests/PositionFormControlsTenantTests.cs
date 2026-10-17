using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Controllers;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The two controls manual testing sent back: the allowed-grades picker, and the unit picker's
/// <em>Within</em> filter contradicting the choice beside it.
/// </summary>
/// <remarks>
/// <b>Both failures were silent, which is what makes them worth a file.</b> The grades control was a
/// <c>&lt;select multiple&gt;</c>: clicking four grades one after another saved one, because a plain
/// click in a listbox drops every other selection, and nothing on the screen said so. The unit
/// picker's scope filter was the mirror image — narrowing <em>Within</em> left a chosen unit in the
/// form that the filter beside it said was out of bounds, and the form saved it.
///
/// <b>Every assertion here is about what the server renders.</b> Both controls work with no script
/// at all — that is the rule every picker in this module follows — so the markup is the contract and
/// the script only adds the search box and the live count. A browser test covers the enhanced
/// behaviour; this covers what arrives.
/// </remarks>
public sealed class PositionFormControlsTenantTests : IAsyncLifetime, IDisposable
{
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-zenith.recipe.json");
        await ApplyAsync("organisation-designer-zenith-positions.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    private static readonly DateOnly Today = new(2026, 10, 15);

    // ---- the allowed-grades checklist ---------------------------------------------------------

    /// <summary>
    /// The Add form offers grades as checkboxes with a search box, not as a multi-select listbox.
    /// </summary>
    /// <remarks>
    /// <b>The listbox was unusable in a way nobody could see.</b> Adding a second grade needed
    /// control-click and a run needed shift-click; a plain click silently dropped everything else.
    /// Checkboxes need nothing learnt, are keyboard-operable as they come, and post the same field
    /// in the same way — so the form binding is unchanged and only the control is different.
    /// </remarks>
    [Fact]
    public async Task TheAddFormOffersGradesAsCheckboxesWithASearchBox()
    {
        var page = await AddPositionFormAsync();

        page.Should().Contain("data-grade-checklist", "the list is a group of checkboxes");
        page.Should().Contain("data-grade-checklist-search", "with a box to filter it");
        page.Should().Contain("data-grade-checklist-count", "and a count of what is ticked");

        page.Should().NotContain("multiple size=\"6\"", "the listbox is gone");

        var list = Between(page, "data-grade-checklist>", "</div></div>");

        list.Should().Contain("type=\"checkbox\"");
        list.Should().Contain("name=\"AllowedGradeRecordIds\"", "posting the same field as before");

        // Every grade in the tenant is rendered, because a checkbox the page never drew cannot be
        // ticked — this control's page size is its whole answer, unlike the combobox pickers.
        Regex.Count(page, "data-grade-option").Should().Be(
            19, "all nineteen Zenith grades, so any of them can be chosen without a round trip");
    }

    /// <summary>Each checkbox is labelled, and the label points at its own box.</summary>
    /// <remarks>
    /// This is the whole of the keyboard and screen-reader story: a native checkbox with a
    /// <c>&lt;label for&gt;</c> is reachable by Tab, toggled by Space, announced with its name, and
    /// has a click target the size of its text. Nothing is added to achieve that, which is the
    /// argument for checkboxes over a listbox with ARIA bolted on.
    /// </remarks>
    [Fact]
    public async Task EveryGradeCheckboxIsLabelledForTheKeyboard()
    {
        var page = await AddPositionFormAsync();

        var boxes = Regex.Matches(page, "<input class=\"form-check-input\" type=\"checkbox\" id=\"(?<id>[^\"]+)\"");

        boxes.Count.Should().Be(19);

        foreach (Match box in boxes)
        {
            page.Should().Contain(
                $"for=\"{box.Groups["id"].Value}\"",
                "a checkbox without a label is a box a screen reader cannot name");
        }

        // The group is named by the field's own label rather than left anonymous.
        page.Should().Contain("role=\"group\"");
        page.Should().Contain("aria-labelledby=");
    }

    /// <summary>What is already chosen is rendered first, and ticked.</summary>
    /// <remarks>
    /// A position with two grades out of nineteen shows its two without scrolling. The order is
    /// settled when the page is drawn and the script does not re-sort on a tick — a row that jumped
    /// to the top under the reader's cursor would make their next click land on something else.
    /// </remarks>
    [Fact]
    public async Task TheGradesAPositionAlreadyHasAreTickedAndComeFirst()
    {
        var page = await EditPositionFormAsync("zenith-pos-site-supervisor");

        var list = Between(page, "data-grade-checklist>", "</div></div>");

        var first = Regex.Matches(list, "data-label=\"(?<label>[^\"]+)\"")
            .Take(2)
            .Select(match => match.Groups["label"].Value)
            .ToList();

        first.Should().AllSatisfy(label => label.Should().StartWith("SS-"),
            "Site Supervisor's own two grades lead the list");

        // And they are the ticked ones, which is the half that matters on a save.
        Regex.Count(list, "checked").Should().Be(2);
    }

    /// <summary>The search box is rendered hidden, for the no-script case.</summary>
    /// <remarks>
    /// The same rule the unit picker's <em>Within</em> filter follows. Filtering is over markup that
    /// is already in the page, so with no script the box would be a control that visibly does
    /// nothing — worse than one that is not there. The checkboxes themselves are untouched by this:
    /// without script every grade is visible and tickable, which is a longer form rather than a
    /// broken one.
    /// </remarks>
    [Fact]
    public async Task TheSearchBoxIsHiddenUntilTheScriptUnhidesIt()
    {
        var page = await AddPositionFormAsync();

        var search = Between(page, "data-grade-checklist-search", ">");

        search.Should().Contain("hidden");

        // And the script that unhides it is actually on the page, which is the other half.
        page.Should().Contain("grade-picker.js");
    }

    /// <summary>The control's words are localised, including the count template.</summary>
    /// <remarks>
    /// The count changes in the browser and a script has no localiser, so the server sends the
    /// <em>template</em> and the script puts one number in it. A phrase with a plural in it would
    /// have had to be chosen in the script, and Arabic has six forms — which is not a rule a script
    /// can carry. "Selected: 3" has no plural to get wrong in either language.
    /// </remarks>
    [Fact]
    public async Task TheChecklistSpeaksArabicUnderArabic()
    {
        var page = await AddPositionFormAsync(culture: "ar");

        page.Should().Contain("البحث في الدرجات", "the search box's placeholder");
        page.Should().Contain("المحدّد:", "and the count template");

        page.Should().NotContain("Search grades");
    }

    // ---- the unit picker's Within scope -------------------------------------------------------

    /// <summary>
    /// The picker endpoint says whether a chosen unit is still inside a narrowed scope.
    /// </summary>
    /// <remarks>
    /// <b>The control cannot answer this itself.</b> It holds a page of twenty units and the chosen
    /// one may be nowhere near it, so "is Civil inside Corporate" is a question only the server can
    /// settle — through the closure index, on the page's own date, which is the same resolution the
    /// search beside it uses so the two cannot disagree.
    /// </remarks>
    [Fact]
    public async Task TheUnitPickerSaysWhetherAChoiceSurvivesANarrowedScope()
    {
        var organisation = string.Empty;
        var civil = string.Empty;
        var corporate = string.Empty;
        var engineering = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();

            organisation = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;

            civil = (await records.GetByCodeAsync("zenith-dept-civil"))!.RecordId;
            corporate = (await records.GetByCodeAsync("zenith-div-corporate"))!.RecordId;
            engineering = (await records.GetByCodeAsync("zenith-div-engineering"))!.RecordId;
        });

        // Civil is an Engineering department, so Corporate is a scope it is not in.
        (await InScopeAsync(organisation, check: civil, within: corporate))
            .Should().BeFalse("the form would otherwise hold a unit the filter beside it disowns");

        (await InScopeAsync(organisation, check: civil, within: engineering))
            .Should().BeTrue("and a scope it is in leaves the choice alone");

        // At or below, not below. Narrowing Within to the very unit that is chosen is how somebody
        // confirms a choice, and treating it as out of scope would clear what they just agreed with.
        (await InScopeAsync(organisation, check: civil, within: civil)).Should().BeTrue();

        // Nothing to contradict is not a contradiction. Either of these answering false would have
        // the control clear a choice nobody had narrowed away from.
        (await InScopeAsync(organisation, check: civil, within: null)).Should().BeTrue();
        (await InScopeAsync(organisation, check: null, within: corporate)).Should().BeTrue();
    }

    /// <summary>The picker renders the place its note goes, and the words for it.</summary>
    /// <remarks>
    /// Said out loud rather than silently emptied. A control that cleared itself without a word
    /// would send a reader to Save believing a unit was still named — which is the same class of
    /// failure as the listbox dropping selections, and the reason both are in this file.
    /// </remarks>
    [Fact]
    public async Task TheUnitPickerCarriesSomewhereToSayItClearedAChoice()
    {
        var page = await AddPositionFormAsync();

        page.Should().Contain("data-unit-picker-cleared", "the note's own element, empty until needed");
        page.Should().Contain("data-unit-picker-within", "and the control that can cause it");

        var strings = await PickerStringsAsync();

        strings.Should().ContainKey("cleared", "the words travel with the control, not in the script");
        strings["cleared"].Should().Contain("not inside that scope");
        strings["cleared"].Should().Contain("has been cleared", "and it says what was done, not just what is wrong");
    }

    /// <summary>And those words are Arabic under Arabic.</summary>
    /// <remarks>
    /// Read out of the control's JSON rather than off the page. The strings ride in an attribute as
    /// JSON, which escapes non-ASCII to <c>ا</c>-style sequences, so Arabic is really there and
    /// a substring search for it on the rendered HTML finds nothing. Parsing is what a browser does
    /// with it, so parsing is what the assertion should do.
    /// </remarks>
    [Fact]
    public async Task TheClearedNoteIsInArabicUnderArabic()
    {
        var strings = await PickerStringsAsync(culture: "ar");

        strings["cleared"].Should().Contain(
            "ليست داخل ذلك النطاق", "the unit you chose is not inside that scope");

        strings["cleared"].Should().NotContain("not inside that scope");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private async Task<bool> InScopeAsync(string structureId, string? check, string? within)
    {
        var query = $"?structureId={structureId}&q=&take=1";

        if (check is not null)
        {
            query += $"&check={check}";
        }

        if (within is not null)
        {
            query += $"&within={within}";
        }

        // childType, so the endpoint answers the "which units may contain one of these" question
        // the Add form's picker asks, and checks the record-management permission rather than the
        // placement one.
        var positionTypeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            positionTypeId = (await services.GetRequiredService<IDimensionTypeService>()
                .GetByCodeAsync("zenith-position"))!.DimensionTypeId;
        });

        var response = await _tenant.Administrator.GetAsync(
            UnitPickerAdminController.SearchUrl + query + $"&childType={positionTypeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("checkInScope", "the endpoint answers the question the control asks");

        return body.Contains("\"checkInScope\":true", StringComparison.Ordinal);
    }

    /// <summary>
    /// The unit picker's localised strings, as the script receives them.
    /// </summary>
    /// <remarks>
    /// Taken from the <em>raw</em> response rather than the decoded one. The attribute holds JSON,
    /// whose quotes are HTML-encoded on the way out; decoding the whole page first turns those back
    /// into real quotes and there is no longer an attribute boundary to find. So: find it, decode
    /// just it, parse it — which is the order a browser does the same three things in.
    /// </remarks>
    private async Task<Dictionary<string, string>> PickerStringsAsync(string culture = "en")
    {
        var raw = await AddPositionFormAsync(culture, decoded: false);

        var attribute = Regex.Match(raw, "data-unit-picker-strings=\"(?<json>[^\"]*)\"");

        attribute.Success.Should().BeTrue("the unit picker sends its words with it");

        var json = WebUtility.HtmlDecode(attribute.Groups["json"].Value);

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }

    private async Task<string> AddPositionFormAsync(string culture = "en", bool decoded = true)
    {
        var typeId = string.Empty;
        var structureId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            typeId = (await services.GetRequiredService<IDimensionTypeService>()
                .GetByCodeAsync("zenith-position"))!.DimensionTypeId;

            structureId = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-org"))!.StructureId;
        });

        return await PageAsync(
            $"/Admin/Dimensions/Records/Create?dimensionTypeId={typeId}&structureId={structureId}",
            culture,
            decoded);
    }

    // ---- polish B: Reports to is an answer, not a silence (D31) -------------------------------

    /// <summary>
    /// The control starts unanswered and offers "nobody" as a choice of its own. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>The defect.</b> The empty option read "— reports to nobody —", so a form nobody had
    /// touched looked like a deliberate claim that the post was the top of the reporting line. It
    /// was not even that: the save wrote no reporting-line row at all, leaving the post off that
    /// axis entirely while the screen said otherwise.
    /// </remarks>
    [Fact]
    public async Task TheReportsToControlStartsUnansweredAndOffersNobodyAsAChoice()
    {
        var page = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await AddPositionFormAsync(decoded: false));

        var options = page.QuerySelectorAll("select[data-position-picker] option").ToList();

        options.Should().NotBeEmpty("the position form offers a Reports to control");

        options[0].GetAttribute("value").Should().BeEmpty("the first option is the unanswered one");
        options[0].TextContent.Trim().Should().Be("Choose…");

        options[1].GetAttribute("value").Should().Be(
            ReportsToChoice.Nobody,
            "'nobody' is an answer and therefore has a value of its own");

        options[1].TextContent.Should().Contain("top of the reporting line");
    }

    /// <summary>
    /// Saving a position with Reports to unanswered is refused, in the validator. Decision D31.
    /// </summary>
    /// <remarks>
    /// Asked of the validator directly as well as through the screen, because the rule is the
    /// product's and not the form's: a recipe and any later import go through the same method and
    /// must refuse in the same words (CLAUDE.md rule 5).
    /// </remarks>
    [Fact]
    public async Task SavingAPositionWithReportsToUnansweredIsRefused() =>
        await InTenantAsSystemAsync(async services =>
        {
            var reporting = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-reporting"))!.StructureId;

            var validator = services.GetRequiredService<IDimensionValidator>();

            var refused = await validator.ValidateReportsToAsync(
                reporting, ReportsToIntent.Unstated, null, Today);

            refused.Should().ContainSingle()
                .Which.Rule.Should().Be(DimensionRule.ReportsToRequired);

            refused[0].IsAdvisory.Should().BeFalse("silence blocks the save");

            refused[0].Message.Value.Should().Contain(
                "Reports to nobody", "the message says what the other answer is");

            // And the two real answers are accepted.
            (await validator.ValidateReportsToAsync(
                reporting, ReportsToIntent.UnderPosition, "whatever", Today))
                .Should().BeEmpty("naming a post is the ordinary answer");
        });

    /// <summary>
    /// Choosing "nobody" saves, puts the post ON the reporting line, and warns when somebody is
    /// already at the top. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>The warning never blocks.</b> An organisation mid-handover, or one with joint chief
    /// executives, is entitled to record what it actually has — the same judgement D26 F makes
    /// about a full position.
    /// </remarks>
    [Fact]
    public async Task ChoosingNobodySavesAndWarnsWhenSomebodyIsAlreadyAtTheTop() =>
        await InTenantAsSystemAsync(async services =>
        {
            var reporting = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("zenith-reporting"))!.StructureId;

            // Zenith's CEO is already the top of this line.
            var warned = await services.GetRequiredService<IDimensionValidator>()
                .ValidateReportsToAsync(reporting, ReportsToIntent.Top, null, Today);

            warned.Should().ContainSingle()
                .Which.Rule.Should().Be(DimensionRule.SecondTopOfReportingLine);

            warned[0].IsAdvisory.Should().BeTrue(
                "a second post at the top is worth saying and not worth refusing");

            warned[0].Message.Value.Should().Contain("zenith-pos-ceo", "the message names the one already there");
        });

    /// <summary>
    /// The Edit form shows where a position sits on the reporting line, and does not offer to
    /// change it there. Decision D31.
    /// </summary>
    /// <remarks>
    /// <b>Reports to is a parent, on a second axis.</b> Changing a parent is a dated move with its
    /// own permission and its own dry run — which is exactly why the organisation parent is not on
    /// this form either, as the view model's own remarks say. So this screen states the fact and
    /// links to the designer, where that move already lives.
    /// </remarks>
    [Fact]
    public async Task TheEditFormShowsTheReportingLineAndDoesNotOfferToChangeItThere()
    {
        // A post under another: the Director reports to the CEO.
        var directorHtml = await EditPositionFormAsync("zenith-pos-dir-tech");
        var director = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(directorHtml);

        director.QuerySelector("#record-edit-reports-to-name")!.TextContent
            .Should().Contain("CEO");

        director.QuerySelector("#record-edit-reporting-line-link").Should().NotBeNull(
            "changing it is a dated move, and the link goes where that move lives");

        director.QuerySelectorAll("select[data-position-picker]").Should().BeEmpty(
            "this form does not reparent anything — on either axis");

        // And the top of the line reads as the top rather than as a blank.
        new AngleSharp.Html.Parser.HtmlParser().ParseDocument(
            await EditPositionFormAsync("zenith-pos-ceo"))
            .QuerySelector("#record-edit-reports-to-top")
            .Should().NotBeNull("the CEO is the top of the line and the form says so");
    }

    private async Task<string> EditPositionFormAsync(string code, string culture = "en")
    {
        var recordId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            recordId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync(code))!.RecordId;
        });

        return await PageAsync($"/Admin/Dimensions/Records/Edit/{recordId}", culture);
    }

    private async Task<string> PageAsync(string path, string culture, bool decoded = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Add("Cookie", $".AspNetCore.Culture=c%3D{culture}%7Cuic%3D{culture}");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        var response = await _tenant.Administrator.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"GET {path} under '{culture}' should render");

        // See PositionPageTenantTests.PageAsync: non-ASCII arrives as numeric entities. Not decoded
        // when a caller is about to pull an attribute out by its quotes — decoding first would turn
        // the JSON's own encoded quotes into real ones and dissolve the boundary.
        return decoded ? WebUtility.HtmlDecode(body) : body;
    }

    private static string Between(string page, string marker, string closer)
    {
        var start = page.IndexOf(marker, StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1, "the page should contain '{0}'", marker);

        start += marker.Length;

        var end = page.IndexOf(closer, start, StringComparison.Ordinal);

        end.Should().BeGreaterThan(-1, "'{0}' should be closed by '{1}'", marker, closer);

        return page[start..end];
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("position form test"))
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
