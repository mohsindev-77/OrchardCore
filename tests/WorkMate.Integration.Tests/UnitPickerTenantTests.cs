using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using WorkMate.Dimensions.Controllers;
using WorkMate.Dimensions.Services;
using WorkMate.Dimensions.Shell;
using WorkMate.Platform;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The unit picker: what it offers, how it is narrowed, and the paths that make it usable.
/// </summary>
/// <remarks>
/// <b>Run on Crescent, which is the tenant this control exists for.</b> Crescent has two departments
/// called "Credit" and two called "Operations", on one structure, under different branches. A
/// picker that showed bare names would offer a reader two identical rows and no way to choose
/// correctly — so the assertions here are about the path, not about the search matching.
///
/// Crescent is also the structure that declares no employee-attachable types at all, which makes it
/// the unconstrained case: everything on the axis may hold employees, and the "Within" filter falls
/// back to the root types. Both are deliberate — a tenant that has not expressed the constraint is
/// the normal state of a tenant being set up.
/// </remarks>
public sealed class UnitPickerTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();

        await ApplyAsync("organisation-designer-crescent.recipe.json");
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    // ---- the endpoint ------------------------------------------------------------------

    /// <summary>
    /// The endpoint is where the control hard-codes it, answers in the shape the script reads, and
    /// is not open to the world.
    /// </summary>
    /// <remarks>
    /// The URL is a constant because the tag helper has no URL helper to ask. Resolving it for
    /// real here is what stops a route rename from turning the control back into a flat list
    /// silently.
    /// </remarks>
    [Fact]
    public async Task TheEndpointIsWhereTheControlLooksForItAndAnswersInTheShapeItReads()
    {
        var structureId = await StructureIdAsync("crescent-org");

        var anonymous = await _tenant.Anonymous.GetAsync(
            $"{UnitPickerAdminController.SearchUrl}?structureId={structureId}");

        anonymous.StatusCode.Should().NotBe(
            HttpStatusCode.OK, "the picker is an admin control, not a public directory of the company");

        var body = await _tenant.Administrator.GetStringAsync(
            $"{UnitPickerAdminController.SearchUrl}?structureId={structureId}&take=5");

        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        document.RootElement.TryGetProperty("total", out _).Should().BeTrue();

        items.GetArrayLength().Should().BeGreaterThan(0, "Crescent has units on it");

        foreach (var item in items.EnumerateArray())
        {
            // Every name the script builds an option from. A renamed property is a control that
            // silently shows "undefined (undefined)".
            foreach (var property in new[] { "id", "code", "name", "path" })
            {
                item.TryGetProperty(property, out _).Should().BeTrue(
                    "unit-picker.js reads '{0}' off every result", property);
            }
        }
    }

    /// <summary>
    /// Without <c>AssignEmployees</c> the endpoint is refused.
    /// </summary>
    /// <remarks>
    /// Placement is what the control is for, so that is the permission that opens it. A reader who
    /// cannot place anybody has no use for a list of the units they could not place them at — and
    /// an endpoint that answered anyway would be a way to enumerate the organisation around the
    /// screens that guard it.
    /// </remarks>
    [Fact]
    public async Task WithoutAssignEmployeesTheEndpointIsRefused()
    {
        var structureId = await StructureIdAsync("crescent-org");

        using var reader = await _tenant.CreateSignedInClientAsync(
            "unit-picker-reader", "Workmate!Integration1", PlatformRoles.Auditor, allowAutoRedirect: false);

        var refused = await reader.GetAsync(
            $"{UnitPickerAdminController.SearchUrl}?structureId={structureId}");

        refused.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    // ---- the duplicate-name case the control exists for --------------------------------

    /// <summary>
    /// Searching "Credit" finds both of Crescent's, and the paths tell them apart.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the picker shows a path. Two rows reading "Credit" are two rows a
    /// reader has to guess between, and guessing wrong puts somebody in the wrong branch of the
    /// bank — a mistake nothing downstream would flag, because both answers are valid units.
    /// </remarks>
    [Fact]
    public async Task TwoUnitsCalledCreditAreToldApartByTheirPaths()
    {
        var paths = await PathsAsync("crescent-org", "Credit");

        paths.Should().HaveCountGreaterThanOrEqualTo(2, "Crescent has a Credit under two branches");

        paths.Should().Contain(path => path.Contains("Lahore Main Branch", StringComparison.Ordinal));
        paths.Should().Contain(path => path.Contains("Karachi Main Branch", StringComparison.Ordinal));

        paths.Should().OnlyHaveUniqueItems(
            "if two options read identically the control cannot be used correctly");

        foreach (var path in paths.Where(path => path.EndsWith("Credit", StringComparison.Ordinal)))
        {
            path.Should().Contain(
                "›", "a department's path names what it sits under, root first");
        }
    }

    /// <summary>The same for "Operations", which Crescent also has twice.</summary>
    [Fact]
    public async Task TwoUnitsCalledOperationsAreAlsoToldApartByTheirPaths()
    {
        var paths = await PathsAsync("crescent-org", "Operations");

        paths.Should().HaveCountGreaterThanOrEqualTo(2);
        paths.Should().OnlyHaveUniqueItems();
    }

    /// <summary>A code is a search term too, because that is what some readers have memorised.</summary>
    [Fact]
    public async Task SearchingByCodeFindsTheUnit()
    {
        var found = await SearchAsync("crescent-org", q: "crescent-dept-khi-credit");

        found.Should().ContainSingle();
        found[0].Code.Should().Be("crescent-dept-khi-credit");
    }

    // ---- the within filter -------------------------------------------------------------

    /// <summary>
    /// "Within" narrows the results to the chosen unit's subtree, through the closure index.
    /// </summary>
    /// <remarks>
    /// Asserted from both sides. Narrowing to the Branch Network division must keep the Karachi
    /// Credit and drop the Lahore one is <em>not</em> what this says — both branches are under that
    /// division — so the assertion that matters is the opposite one: a division that contains
    /// neither must return neither, and the whole-structure search must still return both. A filter
    /// that quietly did nothing would pass a one-sided test.
    /// </remarks>
    [Fact]
    public async Task TheWithinFilterNarrowsResultsToTheChosenUnitsSubtree()
    {
        var everywhere = await SearchAsync("crescent-org", q: "Credit");
        var branchNetwork = await RecordIdAsync("crescent-div-branch-network");
        var headOffice = await RecordIdAsync("crescent-div-head-office");

        var underBranches = await SearchAsync("crescent-org", q: "Credit", within: branchNetwork);
        var underHeadOffice = await SearchAsync("crescent-org", q: "Credit", within: headOffice);

        everywhere.Should().HaveCountGreaterThan(
            underHeadOffice.Count, "the whole structure has more Credits on it than Head Office does");

        underBranches.Select(unit => unit.Code).Should().Contain(
            ["crescent-dept-lhr-credit", "crescent-dept-khi-credit"],
            "both branch departments sit under the Branch Network division");

        underHeadOffice.Select(unit => unit.Code).Should().NotContain(
            "crescent-dept-lhr-credit",
            "a branch department is not inside Head Office, and the filter must say so");

        // Credit Risk is Head Office's own department, so the filter is narrowing rather than
        // emptying: a filter that returned nothing for everything would also pass the line above.
        underHeadOffice.Select(unit => unit.Code).Should().Contain("crescent-dept-credit-risk");
    }

    /// <summary>A unit is within itself, so narrowing to a branch still offers that branch.</summary>
    /// <remarks>
    /// The boundary. Excluding the node itself would mean a reader who chose "within Lahore Main
    /// Branch" was then refused Lahore Main Branch, which is the opposite of what they asked.
    /// </remarks>
    [Fact]
    public async Task AUnitIsWithinItself()
    {
        var lahore = await RecordIdAsync("crescent-branch-lahore-main");
        var found = await SearchAsync("crescent-org", q: null, within: lahore);

        found.Select(unit => unit.Code).Should().Contain("crescent-branch-lahore-main");
    }

    /// <summary>
    /// The containers are what the "Within" control offers: divisions and regions, not departments.
    /// </summary>
    [Fact]
    public async Task TheWithinControlOffersContainersRatherThanEveryUnit()
    {
        await InTenantAsync(async services =>
        {
            var structureId = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync("crescent-org"))!.StructureId;

            var scopes = await services.GetRequiredService<IDimensionGraphService>()
                .GetEmployeeScopeUnitsAsync(structureId);

            // Crescent declares no employee-attachable types, so every type is attachable and the
            // fallback applies: the structure's root types, which are its divisions.
            scopes.Select(scope => scope.Code).Should().Contain(
                ["crescent-div-head-office", "crescent-div-branch-network"]);

            scopes.Select(scope => scope.Code).Should().NotContain(
                "crescent-dept-lhr-credit", "a department is not a scope somebody narrows to");
        });
    }

    // ---- the effective date ------------------------------------------------------------

    /// <summary>
    /// A unit that does not exist yet on the effective date is not offered on that date.
    /// </summary>
    /// <remarks>
    /// Sialkot Branch opens on 1 July 2024. Offering it for a placement effective in January would
    /// be offering a placement at a unit that did not exist — which the engine would then have to
    /// refuse, after the reader had chosen it.
    /// </remarks>
    [Fact]
    public async Task AUnitIsOnlyOfferedOnDatesItIsActive()
    {
        var before = await SearchAsync("crescent-org", q: "Sialkot", asAt: "2024-01-15");
        var after = await SearchAsync("crescent-org", q: "Sialkot", asAt: "2024-08-01");

        before.Should().BeEmpty("Sialkot Branch opens in July");
        after.Select(unit => unit.Code).Should().Contain("crescent-branch-sialkot");
    }

    // ---- the control itself ------------------------------------------------------------

    /// <summary>
    /// The script the control depends on is registered under the name the views ask for.
    /// </summary>
    /// <remarks>
    /// A view asking for a resource that does not exist renders nothing and says nothing, so the
    /// picker would quietly go back to being a plain list of the first page — which is the defect
    /// it exists to fix, returning by a different door. Its dependency on the shared combobox is
    /// asserted too: without it the script loads, throws, and the control is dead on the page.
    /// </remarks>
    [Fact]
    public void ThePickerScriptIsRegisteredAndDependsOnTheSharedCombobox()
    {
        var options = new OrchardCore.ResourceManagement.ResourceManagementOptions();

        new WorkMateDimensionsResourceManifest().Configure(options);
        new WorkMate.Platform.Shell.WorkMateResourceManifest().Configure(options);

        var scripts = options.ResourceManifests
            .SelectMany(manifest => manifest.GetResources("script"))
            .ToList();

        scripts.Should().Contain(resource => resource.Key == "workmate-unit-picker");
        scripts.Should().Contain(resource => resource.Key == "workmate-picker");

        scripts.Single(resource => resource.Key == "workmate-unit-picker")
            .Value[0].Dependencies.Should().Contain("workmate-picker");
    }

    /// <summary>
    /// The Place screen renders the picker, with the paths on its options and the "Within" filter.
    /// </summary>
    [Fact]
    public async Task ThePlaceScreenRendersThePickerWithPathsAndTheWithinFilter()
    {
        var structureId = await StructureIdAsync("crescent-org");

        var page = await _tenant.Administrator.GetStringAsync(
            $"/Admin/Employees/Placement/Place?structureId={structureId}");

        page.Should().Contain("data-unit-picker-url", "the control is on the form");

        // The Within filter is OFF on every employee screen since slice F6b. It narrows a list of
        // units, which earns its place on an admin screen about units and is a control nobody uses
        // on a form about one person — and on a position-based axis there is no unit list left for
        // it to narrow, so leaving it on this shape would mean the two placement screens disagreed
        // about what controls an employee form has. It is still on the Positions and Units screens,
        // which ThePositionsScreenKeepsTheWithinFilter holds.
        page.Should().NotContain("data-unit-picker-within");

        // Read as a browser would, so the assertion is about what the option says rather than about
        // how the encoder spelled a chevron.
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        var options = document
            .QuerySelectorAll("select[data-unit-picker] option")
            .Select(option => option.TextContent)
            .ToList();

        options.Should().Contain(
            text => text.Contains("Lahore Main Branch", StringComparison.Ordinal)
                && text.Contains("Credit", StringComparison.Ordinal),
            "an option names the whole path, which is what tells Crescent's two Credits apart");

        options.Should().Contain(
            text => text.Contains(WorkMate.Dimensions.ViewModels.BilingualDisplay.PathSeparator, StringComparison.Ordinal),
            "the path is drawn with the separator the control and the search agree on");
    }

    /// <summary>
    /// The Within filter survives where it earns its place: an admin screen about units.
    /// </summary>
    /// <remarks>
    /// The other half of slice F6b's item 8. Removing a control everywhere is easy and usually
    /// wrong; what was decided is that it belongs on a screen whose job is to narrow a list of
    /// units and not on a form about one person. Without this test, "removed from employee screens"
    /// and "removed" are indistinguishable the next time somebody tidies up.
    ///
    /// <b>Two different controls share the word "Within", and only one of them is this one.</b> The
    /// records list's filter <em>is</em> a unit picker bound to <c>Within</c> — the whole control,
    /// which is why it carries <c>allow-within="false"</c>: you do not narrow the narrower. The
    /// marker <c>data-unit-picker-within</c> is the <em>nested</em> scope select a picker renders
    /// inside itself, which is what the employee screens switched off and what
    /// <see cref="ThePlaceScreenRendersThePickerWithPathsAndTheWithinFilter"/> holds absent.
    /// Asserting the nested marker here would pass only if this screen grew a control it has never
    /// had.
    /// </remarks>
    [Fact]
    public async Task ThePositionsScreenKeepsTheWithinFilter()
    {
        var typeId = string.Empty;

        await InTenantAsync(async services =>
        {
            typeId = (await services.GetRequiredService<IDimensionTypeService>()
                .GetByCodeAsync("crescent-department"))!.DimensionTypeId;
        });

        var page = await _tenant.Administrator.GetStringAsync(
            $"/Admin/Dimensions/Records/Index?dimensionTypeId={typeId}");

        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(page);

        document.QuerySelector("select[data-unit-picker]#Within")
            .Should().NotBeNull("narrowing a list of units is exactly what this screen's filter is for");
    }

    // ---- harness ---------------------------------------------------------------------------

    private sealed record Offered(string Id, string Code, string Name, string Path);

    private async Task<IReadOnlyList<Offered>> SearchAsync(
        string structureCode, string? q = null, string? within = null, string? asAt = null)
    {
        var structureId = await StructureIdAsync(structureCode);
        var query = $"{UnitPickerAdminController.SearchUrl}?structureId={structureId}&take=20";

        if (!string.IsNullOrEmpty(q))
        {
            query += $"&q={Uri.EscapeDataString(q)}";
        }

        if (!string.IsNullOrEmpty(within))
        {
            query += $"&within={within}";
        }

        if (!string.IsNullOrEmpty(asAt))
        {
            query += $"&asAt={asAt}";
        }

        var body = await _tenant.Administrator.GetStringAsync(query);

        using var document = JsonDocument.Parse(body);

        return
        [
            .. document.RootElement.GetProperty("items").EnumerateArray().Select(item => new Offered(
                item.GetProperty("id").GetString() ?? string.Empty,
                item.GetProperty("code").GetString() ?? string.Empty,
                item.GetProperty("name").GetString() ?? string.Empty,
                item.GetProperty("path").GetString() ?? string.Empty)),
        ];
    }

    private async Task<IReadOnlyList<string>> PathsAsync(string structureCode, string q) =>
        [.. (await SearchAsync(structureCode, q)).Select(unit => unit.Path)];

    private async Task<string> StructureIdAsync(string code)
    {
        string structureId = null!;

        await InTenantAsync(async services =>
        {
            structureId = (await services.GetRequiredService<IStructureService>()
                .GetByCodeAsync(code))!.StructureId;
        });

        return structureId;
    }

    private async Task<string> RecordIdAsync(string code)
    {
        string recordId = null!;

        await InTenantAsync(async services =>
        {
            recordId = (await services.GetRequiredService<IDimensionService>()
                .GetByCodeAsync(code))!.RecordId;
        });

        return recordId;
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("unit picker test"))
            {
                await work(services);
            }
        });

    private async Task ApplyAsync(string fileName)
    {
        await InTenantAsync(async services =>
        {
            var fileProvider = new PhysicalFileProvider(RecipesFolder);

            await services.GetRequiredService<IRecipeExecutor>().ExecuteAsync(
                Guid.NewGuid().ToString("n"),
                new RecipeDescriptor
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo(fileName),
                    RequireNewScope = false,
                },
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
