using AngleSharp.Html.Parser;
using FluentAssertions;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// A list written by a recipe reaches the screens without the application being restarted.
/// </summary>
/// <remarks>
/// <b>The defect, exactly as it was found by hand.</b> On a tenant created before slice B2 the
/// standard lists do not exist, so every bound field renders as a free-text box — by design, see
/// <c>ILookupChoiceProvider</c>. An operator then applies <c>WorkMate standard lookup lists</c> from
/// Configuration → Recipes. The lists are written and the bindings are already there, and the
/// screens go on showing free-text boxes until the process is restarted.
///
/// The sequence is what makes it: a screen rendered <em>before</em> the lists exist, then the recipe,
/// then the screen again. A tenant set up with the lists in place can never reproduce it, which is
/// why this test builds a tenant without them.
/// </remarks>
public sealed class LookupListsRecipeVisibilityTenantTests : IAsyncLifetime, IDisposable
{
    private const string StandardLookupListsRecipe = "WorkMate.StandardLookupLists";

    private BaseTenantFixture _tenant = default!;

    public void Dispose() => _tenant?.Dispose();

    public async Task InitializeAsync()
    {
        _tenant = BaseTenantFixture.WithholdingRecipes(StandardLookupListsRecipe);

        await _tenant.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync() => await _tenant.DisposeAsync();

    [Fact]
    public async Task ALookupListAppliedByRecipeIsOfferedOnTheNextRenderWithoutARestart()
    {
        var before = await NationalityControlAsync();

        before.Should().Be("INPUT", "the tenant has no lists yet, so the bound field falls back to free text");

        await ApplyTheStandardListsRecipeAsync();

        var after = await NationalityControlAsync();

        after.Should().Be(
            "SELECT",
            "a list written by a recipe has to reach the screens without the application being restarted");
    }

    /// <summary>The tag name of the nationality control on the Add employee screen.</summary>
    private async Task<string> NationalityControlAsync()
    {
        var html = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin/Employees/Create");

        var control = new HtmlParser().ParseDocument(html).QuerySelector("#NationalityCode");

        control.Should().NotBeNull("the Add employee screen has a nationality control");

        return control!.TagName;
    }

    /// <summary>Runs the recipe the way an operator does: the Run button on Configuration → Recipes.</summary>
    private async Task ApplyTheStandardListsRecipeAsync()
    {
        var index = await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin/Recipes");

        var run = new HtmlParser().ParseDocument(index)
            .QuerySelectorAll("a[href]")
            .Select(link => link.GetAttribute("href") ?? string.Empty)
            .FirstOrDefault(href =>
                href.Contains("/Recipes/Execute", StringComparison.OrdinalIgnoreCase) &&
                href.Contains("standard-lookup-lists.recipe.json", StringComparison.OrdinalIgnoreCase));

        run.Should().NotBeNull("Configuration → Recipes offers the standard lookup lists recipe");

        // The token is per client rather than per page, so it is taken from a page that has a form
        // on it: the Recipes screen posts through a link that script turns into a form.
        var token = BaseTenantFixture.AntiforgeryTokenIn(
            await BaseTenantFixture.GetPageAsync(_tenant.Administrator, "/Admin/Employees/Create"));

        var response = await _tenant.Administrator.PostAsync(
            run,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
            }));

        response.IsSuccessStatusCode.Should().BeTrue(
            $"running the recipe returned {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(
            "failed to run",
            "the recipe has to apply cleanly to a tenant that does not have the lists");
    }
}
