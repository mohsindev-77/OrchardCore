using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;
using static System.Net.WebUtility;

namespace WorkMate.Integration.Tests;

/// <summary>
/// D22, built in slice F6c: the free job title and grade code are retired — kept, readable, and no
/// longer typed — and the two lists behind them are marked superseded.
/// </summary>
/// <remarks>
/// <b>Retirement is only worth anything if the data survives it.</b> Every assertion here is
/// ultimately about that: the field keeps its type, the value stays on the row, saving the row
/// again does not erase it, and the list still resolves an old code to a name. A retirement that
/// quietly emptied the field would pass a test that only checked the box had gone.
/// </remarks>
public sealed class RetiredJobTitleAndGradeTenantTests : IAsyncLifetime, IDisposable
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

    /// <summary>
    /// Both fields still exist, with their types and their bindings — only the editor changed.
    /// </summary>
    /// <remarks>
    /// D22 step 1: retired from the editor, <em>not removed from the type</em>. A tenant's history
    /// is exactly the values it already typed, and dropping the field would destroy them.
    /// </remarks>
    [Fact]
    public async Task TheFieldsAreStillDefinedAndOnlyTheirEditorChanged()
    {
        await _tenant.InTenantAsync(async services =>
        {
            var part = await services.GetRequiredService<IContentDefinitionManager>()
                .GetPartDefinitionAsync(EmployeeSections.JobDetailType);

            part.Should().NotBeNull();

            foreach (var fieldName in EmployeeFieldNames.RetiredOnSections)
            {
                var field = part!.Fields.SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, fieldName, StringComparison.Ordinal));

                field.Should().NotBeNull("'{0}' is retired, not removed — D22 step 1", fieldName);

                field!.Editor().Should().Be(
                    RetiredFieldEditor.Name,
                    "what changes is which editor renders it, and nothing else");
            }

            // The binding survives on purpose: it is how a code typed last year still resolves to
            // a name rather than showing as a bare string.
            part!.Fields.Single(field => field.Name == "GradeCode")
                .GetSettings<LookupFieldSettings>().ListCode
                .Should().Be(StandardLookupLists.Grade, "a retired list is still read");
        });
    }

    /// <summary>
    /// A row that already holds a job title shows it as history, and saving the row keeps it.
    /// </summary>
    /// <remarks>
    /// <b>The hidden input is what this is really testing.</b> Orchard's field drivers bind their
    /// editor's model and write the result straight back, so an editor that rendered nothing at all
    /// would post nothing, the driver would store the empty string, and the second save of a Job
    /// details row would erase the history the first one displayed. That failure is silent and
    /// permanent, which is why it gets a test rather than a comment.
    /// </remarks>
    [Fact]
    public async Task TheRetiredEditorOffersNoBoxAndCarriesTheValueBackInAHiddenInput()
    {
        var employeeId = await GivenAnEmployeeAsync("d22-history", "Had A Job Title");

        await GivenAJobDetailsRowAsync(employeeId, "G3", "Senior Fitter");

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                _tenant.Administrator, $"/Admin/Employees/Sections/Edit/{employeeId}"));

        var posted = page.QuerySelectorAll("input[type='hidden']")
            .Select(input => input.GetAttribute("name") ?? string.Empty)
            .ToList();

        // THE HIDDEN INPUTS ARE THE SAFETY. Orchard's field drivers bind their editor's model and
        // write the result straight back, so an editor that rendered nothing would post nothing,
        // the driver would store the empty string, and the next save of this row would erase the
        // history D22 exists to keep. Silent and permanent, which is why it gets a test.
        posted.Should().Contain(
            name => name.EndsWith("GradeCode.Text", StringComparison.Ordinal),
            "the grade code is posted back untouched rather than dropped");

        posted.Should().Contain(
            name => name.EndsWith("JobTitle.En", StringComparison.Ordinal),
            "and both halves of the bilingual job title, or half a name would be lost");

        posted.Should().Contain(name => name.EndsWith("JobTitle.Ar", StringComparison.Ordinal));

        // And nothing to type into: the fields are not offered on this tenant any more.
        page.QuerySelectorAll("[data-retired-field] input[type='text']").Should().BeEmpty(
            "a tenant with positions does not type these any more — D22 step 1");

        // What IS shown is the value, labelled as history, with a line saying where the answer
        // comes from now. D22 step 3 — better than a blank field and a mystery.
        page.QuerySelectorAll("[data-retired-value]").Select(cell => cell.TextContent)
            .Should().Contain(text => text.Contains("G3", StringComparison.Ordinal));
    }

    /// <summary>
    /// The two lists are marked retired, still readable, and not deleted. D22 steps 5 and 6.
    /// </summary>
    [Fact]
    public async Task TheJobTitleAndGradeListsAreRetiredAndStillResolve()
    {
        await InTenantAsSystemAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();

            // Loaded first, the way an operator does from Forms → standard lists. A tenant that
            // never loaded them has nothing to retire, which is also correct.
            foreach (var code in StandardLookupLists.Superseded)
            {
                var list = await lists.GetByCodeAsync(code);

                if (list is null)
                {
                    continue;
                }

                list.IsRetired.Should().BeTrue(
                    "'{0}' is superseded by a dimension — D22 steps 5 and 6", code);
            }

            // And retiring is idempotent, which is what makes the migration safe to re-enter.
            var grade = await lists.GetByCodeAsync(StandardLookupLists.Grade);

            if (grade is not null)
            {
                (await lists.RetireAsync(grade.LookupListId)).Succeeded.Should().BeTrue(
                    "retiring a retired list changes nothing and is not an error");
            }
        });
    }

    /// <summary>
    /// A tenant with no positions keeps typing them, which is D22 step 3.
    /// </summary>
    /// <remarks>
    /// Where there are no positions the typed value is still the only answer there is, so taking
    /// the box away would leave the reader with a blank and a mystery. The retired editor asks the
    /// tenant's own axes, so this needs no second definition and no setting anybody can get wrong.
    /// </remarks>
    [Fact]
    public async Task ATenantWithNoPositionsStillTypesThem()
    {
        using var plain = new BaseTenantFixture();

        await plain.InitializeAsync();

        await ApplyToAsync(plain, "organisation-designer-demo.recipe.json");

        var employeeId = string.Empty;

        await plain.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("D22 test"))
            {
                var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                    "d22-plain",
                    new BilingualText("No Positions Here", "No Positions Here"),
                    new DateOnly(2024, 1, 1),
                    new EmployeeDetails());

                created.Succeeded.Should().BeTrue();

                employeeId = created.Value!.EmployeeId;

                await AddJobDetailsRowAsync(services, employeeId, "G7", "Still Typed");
            }
        });

        var page = new HtmlParser().ParseDocument(
            await BaseTenantFixture.GetPageAsync(
                plain.Administrator, $"/Admin/Employees/Sections/Edit/{employeeId}"));

        page.QuerySelectorAll("[data-retired-field]").Should().NotBeEmpty(
            "D22 step 3: with no positions the typed value is still the only answer there is");

        page.QuerySelectorAll("[data-retired-value]").Should().BeEmpty(
            "and it is offered rather than shown as history — nothing has superseded it here");

        // THE CONTROL IT ALREADY HAD, not merely a box. GradeCode is bound to the grade list, and
        // a tenant D30 does not even apply to must not lose a managed choice to this slice.
        HtmlDecode(await BaseTenantFixture.GetPageAsync(
                plain.Administrator, $"/Admin/Employees/Sections/Edit/{employeeId}"))
            .Should().Contain(
                "data-lookup-list=\"grade\"",
                "a bound field goes on offering its list on a tenant with no positions");

        await plain.DisposeAsync();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<string> GivenAnEmployeeAsync(string code, string name)
    {
        var employeeId = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                code, new BilingualText(name, name), new DateOnly(2024, 1, 1), new EmployeeDetails());

            created.Succeeded.Should().BeTrue(
                string.Join("; ", created.Errors.Select(error => error.Message.Value)));

            employeeId = created.Value!.EmployeeId;
        });

        return employeeId;
    }

    /// <summary>
    /// A Job details row holding the two values D22 retires, written the way a pre-F6 tenant has
    /// them.
    /// </summary>
    /// <remarks>
    /// Through the content item and <c>UpdateSectionsAsync</c>, which is what the sections screen
    /// itself saves through — so the row these tests read back is a row the product wrote.
    /// </remarks>
    private Task GivenAJobDetailsRowAsync(string employeeId, string gradeCode, string jobTitle) =>
        InTenantAsSystemAsync(services => AddJobDetailsRowAsync(services, employeeId, gradeCode, jobTitle));

    private static async Task AddJobDetailsRowAsync(
        IServiceProvider services,
        string employeeId,
        string gradeCode,
        string jobTitle)
    {
        var employees = services.GetRequiredService<IEmployeeService>();
        var item = (await employees.GetContentItemAsync(employeeId))!;

        var row = await services.GetRequiredService<OrchardCore.ContentManagement.IContentManager>()
            .NewAsync(EmployeeSections.JobDetailType);

        row.Alter<OrchardCore.ContentManagement.ContentPart>(
            EmployeeSections.JobDetailType,
            part =>
            {
                part.Alter<OrchardCore.ContentFields.Fields.TextField>(
                    "GradeCode", field => field.Text = gradeCode);

                part.Alter<WorkMate.Platform.Fields.BilingualTextField>(
                    "JobTitle",
                    field =>
                    {
                        field.En = jobTitle;
                        field.Ar = jobTitle;
                    });
            });

        item.Alter<OrchardCore.Flows.Models.BagPart>(
            EmployeeSections.JobDetails,
            bag => bag.ContentItems.Add(row));

        var saved = await employees.UpdateSectionsAsync(item);

        saved.Succeeded.Should().BeTrue(
            string.Join("; ", saved.Errors.Select(error => error.Message.Value)));
    }

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("D22 test"))
            {
                await work(services);
            }
        });

    private Task ApplyAsync(string fileName) => ApplyToAsync(_tenant, fileName);

    private static async Task ApplyToAsync(BaseTenantFixture tenant, string fileName)
    {
        var path = Path.Combine(RecipesFolder, fileName);

        File.Exists(path).Should().BeTrue("the recipe '{0}' ships with the module", fileName);

        await tenant.InTenantAsync(async services =>
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
