using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// A tenant on a real Kestrel server, seeded with the organisation designer demo data, and a real
/// Chromium pointed at it.
/// </summary>
/// <remarks>
/// Kestrel rather than the in-memory test server every other suite uses, because a browser needs a
/// socket to talk to. The in-memory host that <see cref="WebApplicationFactory{TEntryPoint}"/>
/// builds is started but never asked for anything; every request in this suite — the setup form,
/// the recipe, and everything the browser does — goes to the Kestrel address.
///
/// This suite exists because every other test here asserts on server-rendered HTML, and so cannot
/// see anything that only happens once the browser runs the page's script: expanding a branch,
/// switching view without a reload, zooming. A designer whose expand control did nothing at all
/// passed the entire suite.
/// </remarks>
public class BrowserTenantFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Workmate!Browser1";

    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "workmate-browser", Guid.NewGuid().ToString("n"));

    private IHost? _kestrel;
    private IPlaywright? _playwright;
    private string _signedInState = string.Empty;

    /// <summary>The address the browser and the seeding client both talk to.</summary>
    public string ServerAddress { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = default!;

    /// <summary>The demo recipe's structure code, which every test in this suite opens.</summary>
    public const string DemoStructureCode = "demo-org";

    public async Task InitializeAsync()
    {
        // Touching Services is what builds the host, and therefore what starts Kestrel.
        _ = Services;

        // Redirects are not followed, the way the integration fixture does it: every POST here
        // answers with one, and the base recipe leaves no home page behind for a redirect to "/"
        // to land on — following it turns a successful setup into a 404.
        using var client = new HttpClient(new HttpClientHandler { UseCookies = true, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(ServerAddress),
        };

        await SetUpTenantAsync(client);
        await SignInAsync(client);
        await ApplyRecipesAsync(client);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _signedInState = await SignInWithTheBrowserAsync();
    }

    public new async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_kestrel is not null)
        {
            await _kestrel.StopAsync();
            _kestrel.Dispose();
        }

        await base.DisposeAsync();

        try
        {
            if (Directory.Exists(_contentRoot))
            {
                Directory.Delete(_contentRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A fresh browser context already signed in as the administrator, so each test starts from a
    /// clean page with no cookie — view preference included — carried over from another test.
    /// </summary>
    /// <remarks>
    /// Signing in happens once, in <see cref="InitializeAsync"/>, and every context after that is
    /// built from the saved cookies. Driving the login form once per test cost more than all the
    /// assertions put together.
    /// </remarks>
    public Task<IBrowserContext> SignedInContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = ServerAddress,
            StorageState = _signedInState,
        });

    /// <summary>
    /// A browser context signed in as a user holding exactly one role.
    /// </summary>
    /// <remarks>
    /// What a permission test needs and the administrator cannot give: the administrator holds
    /// everything, so a screen that forgot to check a permission looks identical to one that
    /// checks it correctly. A reader with one role is the only way to see the difference, and a
    /// <em>browser</em> one is the only way to see what a menu actually offers them.
    ///
    /// The user is created once per role and the signed-in state cached, because driving the login
    /// form costs more than the assertions it enables. Created through <c>IUserService</c> inside
    /// the tenant's shell scope rather than by scraping the admin form, for the reason the
    /// integration fixture gives: this fixture has a reliable way into that scope, and the admin
    /// form's field names are not part of the contract this suite pins.
    /// </remarks>
    public async Task<IBrowserContext> SignedInContextAsync(string roleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        if (!_roleStates.TryGetValue(roleName, out var state))
        {
            var userName = $"browser-{roleName.Replace(" ", string.Empty, StringComparison.Ordinal)}".ToLowerInvariant();

            await InTenantAsync(async services =>
            {
                var users = services.GetRequiredService<OrchardCore.Users.Services.IUserService>();

                if (await users.GetUserAsync(userName) is not null)
                {
                    return;
                }

                await users.CreateUserAsync(
                    new OrchardCore.Users.Models.User
                    {
                        UserName = userName,
                        Email = $"{userName}@example.invalid",
                        EmailConfirmed = true,
                        IsEnabled = true,
                        RoleNames = [roleName],
                    },
                    RolePassword,
                    (key, message) => throw new InvalidOperationException(
                        $"Creating browser test user '{userName}' failed: {key}: {message}"));
            });

            state = await SignInWithTheBrowserAsync(userName, RolePassword);
            _roleStates[roleName] = state;
        }

        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = ServerAddress,
            StorageState = state,
        });
    }

    /// <summary>Runs work inside the tenant's shell scope, the way the integration fixture does.</summary>
    /// <remarks>
    /// <b>Against the Kestrel host, not <see cref="WebApplicationFactory{TEntryPoint}.Services"/>.</b>
    /// This fixture builds two hosts: the in-memory one the base class insists on, which is started
    /// and never asked for anything, and the Kestrel one the browser actually talks to. Setup, the
    /// recipes and every tenant this suite has ever created belong to the second. Asking the first
    /// for the tenant finds nothing — and says "setup did not complete", which is true of that host
    /// and extremely misleading about this one.
    /// </remarks>
    public async Task InTenantAsync(Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var host = _kestrel
            ?? throw new InvalidOperationException("The Kestrel host has not been built yet.");

        var shellHost = host.Services.GetRequiredService<OrchardCore.Environment.Shell.IShellHost>();

        if (!shellHost.TryGetSettings(TenantName, out var settings))
        {
            throw new InvalidOperationException($"The '{TenantName}' tenant does not exist. Setup did not complete.");
        }

        var scope = await shellHost.GetScopeAsync(settings);

        await scope.UsingAsync(async shellScope => await work(shellScope.ServiceProvider));
    }

    /// <summary>
    /// Creates a throwaway position for one test to fill, under an existing department, with its
    /// own approved headcount and its own grades.
    /// </summary>
    /// <remarks>
    /// <b>THE RULE FOR THIS SUITE: a test that places people never places them into a position the
    /// recipe created.</b> Every browser test in a collection shares one tenant, and the recipe's
    /// positions are what the <em>reading</em> tests document — <c>PositionsClickBrowserTests</c>
    /// asserts HR Manager is "approved 3, nobody in it yet, 3 vacant", which stops being true the
    /// moment anybody is hired into it. The two tests then pass or fail depending on which ran
    /// first, which is the worst kind of red: it moves.
    ///
    /// So a placing test makes its own post. It is unique per call, so two tests cannot collide
    /// and a re-run cannot trip over the last one; it sits under a department that already exists,
    /// so the derived organisation chain is a real chain and the test can assert on it; and it
    /// carries its own grades, so the grade picker has something of its own to offer.
    ///
    /// <b>No separate tenant.</b> The recipe positions stay pristine because nothing writes to
    /// them, not because they are somewhere else.
    /// </remarks>
    /// <param name="structureCode">The axis to put it on — the organisation chart.</param>
    /// <param name="departmentCode">An existing department it sits under, which becomes its chain.</param>
    /// <param name="approvedHeadcount">Its establishment. Generous by default: a full post warns.</param>
    /// <param name="gradeCodes">Existing grades to allow on it, in the order the picker offers them.</param>
    /// <param name="reportingStructureCode">
    /// The reporting-line axis to put it on as well, or null to leave it off that axis. ADR-0028:
    /// the reporting line is a second axis and nothing is ever assigned on it — a position's place
    /// on it is what answers <em>Reports to</em>.
    /// </param>
    /// <param name="reportsToCode">
    /// The post it answers to on that axis, or null to make it the top of the line. Pass another
    /// throwaway position's code to build a two-post line without touching a recipe post.
    /// </param>
    /// <returns>What the test needs to drive the screens: the position's name and its grades' names.</returns>
    public async Task<ThrowawayPosition> CreateThrowawayPositionAsync(
        string structureCode,
        string departmentCode,
        IReadOnlyList<string> gradeCodes,
        int approvedHeadcount = 9,
        string? reportingStructureCode = null,
        string? reportsToCode = null)
    {
        ArgumentNullException.ThrowIfNull(gradeCodes);

        var token = Guid.NewGuid().ToString("n")[..8];
        var code = $"test-pos-{token}";

        // Named with the token too. The position picker searches by name, so two throwaway posts
        // called the same thing would make ChooseAsync ambiguous — which is the defect that made
        // "Software Engineer" unusable against "Principal Software Engineer".
        var name = $"Test Post {token}";
        var created = new ThrowawayPosition(code, name, []);

        await InTenantAsync(async services =>
        {
            var records = services.GetRequiredService<WorkMate.Dimensions.Services.IDimensionService>();
            var structures = services.GetRequiredService<WorkMate.Dimensions.Services.IStructureService>();
            var types = services.GetRequiredService<WorkMate.Dimensions.Services.IDimensionTypeService>();

            using (services.GetRequiredService<WorkMate.Platform.Services.ISystemOperation>()
                .Begin("browser test position"))
            {
                var structure = await structures.GetByCodeAsync(structureCode)
                    ?? throw new InvalidOperationException($"No structure '{structureCode}'.");

                // The axis's own Position type, found by its standard role rather than by a code,
                // for the reason every other read of it gives: a tenant may rename anything.
                var positionTypeId = await PositionTypeIdAsync(types, structure);

                var department = await records.GetByCodeAsync(departmentCode)
                    ?? throw new InvalidOperationException($"No record '{departmentCode}'.");

                var added = await records.AddUnitAsync(
                    structure.StructureId,
                    department.RecordId,
                    positionTypeId,
                    code,
                    new WorkMate.Core.BilingualText(name, name),
                    new DateOnly(2024, 1, 1),
                    [
                        new WorkMate.Dimensions.Models.DimensionAttributeValue(
                            WorkMate.Dimensions.Services.PositionEstablishment.ApprovedHeadcountAttribute,
                            approvedHeadcount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ]);

                if (!added.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Could not create the throwaway position: "
                        + string.Join("; ", added.Errors.Select(error => error.Message.Value)));
                }

                var grades = new List<string>();
                var gradeIds = new List<string>();

                foreach (var gradeCode in gradeCodes)
                {
                    var grade = await records.GetByCodeAsync(gradeCode)
                        ?? throw new InvalidOperationException($"No grade '{gradeCode}'.");

                    gradeIds.Add(grade.RecordId);
                    grades.Add(WorkMate.Dimensions.ViewModels.BilingualDisplay.Name(grade.NameEn, grade.NameAr));
                }

                var referenced = await records.SetReferencesAsync(
                    added.Value!.RecordId,
                    WorkMate.Dimensions.Models.DimensionReferenceKinds.AllowedGrade,
                    gradeIds);

                if (!referenced.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Could not set the throwaway position's grades: "
                        + string.Join("; ", referenced.Errors.Select(error => error.Message.Value)));
                }

                // The reporting line, when the test is about Reports to. A second axis, placed the
                // same way a recipe's second "placements" row does — and nothing is ever assigned
                // on it, which is ADR-0028's whole point.
                if (!string.IsNullOrEmpty(reportingStructureCode))
                {
                    var reporting = await structures.GetByCodeAsync(reportingStructureCode)
                        ?? throw new InvalidOperationException($"No structure '{reportingStructureCode}'.");

                    string? parentId = null;

                    if (!string.IsNullOrEmpty(reportsToCode))
                    {
                        parentId = (await records.GetByCodeAsync(reportsToCode)
                            ?? throw new InvalidOperationException($"No position '{reportsToCode}'."))
                            .RecordId;
                    }

                    var onTheLine = await services
                        .GetRequiredService<WorkMate.Dimensions.Services.IDimensionGraphService>()
                        .PlaceAsync(
                            reporting.StructureId,
                            added.Value!.RecordId,
                            parentId,
                            new DateOnly(2024, 1, 1));

                    if (!onTheLine.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Could not put the throwaway position on the reporting line: "
                            + string.Join("; ", onTheLine.Errors.Select(error => error.Message.Value)));
                    }
                }

                created = created with { GradeNames = grades };
            }
        });

        return created;
    }

    private static async Task<string> PositionTypeIdAsync(
        WorkMate.Dimensions.Services.IDimensionTypeService types,
        WorkMate.Dimensions.Models.StructureDocument structure)
    {
        foreach (var typeId in structure.DimensionTypeIds)
        {
            var type = await types.GetAsync(typeId);

            if (type?.StandardRole == WorkMate.Dimensions.Models.DimensionTypeRole.Position)
            {
                return typeId;
            }
        }

        throw new InvalidOperationException(
            $"The structure '{structure.Code}' has no Position level, so it places nobody at a post.");
    }

    private const string TenantName = "Default";
    private const string RolePassword = "Workmate!BrowserRole1";

    private readonly Dictionary<string, string> _roleStates = new(StringComparer.Ordinal);

    private Task<string> SignInWithTheBrowserAsync() =>
        SignInWithTheBrowserAsync(AdminUserName, AdminPassword);

    private async Task<string> SignInWithTheBrowserAsync(string userName, string password)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = ServerAddress,
        });

        var page = await context.NewPageAsync();

        await page.GotoAsync("/Login?ReturnUrl=%2FAdmin");

        // By form field name rather than by id: the fields bind to a nested LoginForm model, so
        // the ids Razor generates are a detail of that nesting while the names are what the server
        // actually reads.
        await page.FillAsync("input[name='LoginForm.UserName']", userName);
        await page.FillAsync("input[name='LoginForm.Password']", password);
        await page.ClickAsync("form:has(input[name='LoginForm.Password']) button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Login", StringComparison.OrdinalIgnoreCase));

        return await context.StorageStateAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The host the base class expects. It is started because the base class requires it to be,
        // and then left alone: nothing in this suite sends it a request.
        var testHost = builder.Build();

        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));

        _kestrel = builder.Build();
        _kestrel.Start();

        var addresses = _kestrel.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel started without reporting an address.");

        ServerAddress = addresses.Addresses.First();

        testHost.Start();

        return testHost;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        PrepareContentRoot();

        builder.UseContentRoot(_contentRoot);

        // Production for the same reason the integration suite uses it: in Development Orchard
        // compiles Razor at runtime and the test host's compiler has no language version to go on.
        builder.UseEnvironment(Environments.Production);

        // A browser fetches every stylesheet, script and font on every page, and at information
        // level the host narrates all of it. A failing assertion is unreadable underneath that.
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    private void PrepareContentRoot()
    {
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));

        var repositoryRoot = RepositoryRoot();

        // The host's own configuration, for the reason the integration fixture copies it: ASP.NET
        // reads appsettings.json from the content root, and this is a temporary one.
        File.Copy(
            Path.Combine(repositoryRoot, "src", "WorkMate.Web", "appsettings.json"),
            Path.Combine(_contentRoot, "appsettings.json"),
            overwrite: true);

        var recipes = Path.Combine(_contentRoot, "Recipes");
        Directory.CreateDirectory(recipes);

        foreach (var recipe in Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "recipes"), "*.recipe.json", SearchOption.AllDirectories))
        {
            File.Copy(recipe, Path.Combine(recipes, Path.GetFileName(recipe)), overwrite: true);
        }

        foreach (var module in Directory.EnumerateDirectories(Path.Combine(repositoryRoot, "src")))
        {
            var localization = Path.Combine(module, "Localization");

            if (!Directory.Exists(localization))
            {
                continue;
            }

            foreach (var culture in Directory.EnumerateDirectories(localization))
            {
                var destination = Path.Combine(_contentRoot, "Localization", Path.GetFileName(culture));
                Directory.CreateDirectory(destination);

                foreach (var file in Directory.EnumerateFiles(culture, "*.po"))
                {
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
                }
            }
        }
    }

    private static async Task SetUpTenantAsync(HttpClient client)
    {
        var page = await GetAsync(client, "/");

        var response = await client.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
            ["SiteName"] = "WorkMate Browser",
            ["UserName"] = AdminUserName,
            ["Email"] = "admin@example.invalid",
            ["Password"] = AdminPassword,
            ["PasswordConfirmation"] = AdminPassword,
            ["DatabaseProvider"] = "Sqlite",
            ["RecipeName"] = "WorkMate.Base",
            ["SiteTimeZone"] = "Asia/Bahrain",
        }));

        if (response.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException(
                $"Setting up the tenant with the base recipe returned {(int)response.StatusCode}, expected a redirect. "
                + "The recipe failed to apply.");
        }
    }

    private static async Task SignInAsync(HttpClient client)
    {
        // Setup restarts the shell, which invalidates the antiforgery cookie issued with the setup
        // page, so the login form is fetched fresh.
        var page = await GetAsync(client, "/Login");

        var response = await client.PostAsync("/Login?ReturnUrl=%2FAdmin", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
                ["LoginForm.UserName"] = AdminUserName,
                ["LoginForm.Password"] = AdminPassword,
            }));

        if (response.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException(
                $"Signing in as the site administrator returned {(int)response.StatusCode}, expected a redirect.");
        }
    }

    /// <summary>
    /// The shipped recipes this tenant is seeded with, each paired with a phrase that must appear
    /// on the designer once it has run.
    /// </summary>
    /// <remarks>
    /// Overridable so a suite can be given a different organisation to look at, and deliberately
    /// not additive: a tenant carrying two companies' structures opens the designer on whichever
    /// sorts first, which is a different screen from the one the demo suite asserts against. Each
    /// company therefore gets its own tenant rather than sharing one.
    /// </remarks>
    protected virtual IReadOnlyList<(string FileName, string Evidence)> Recipes =>
        [("organisation-designer-demo.recipe.json", "WorkMate Demo Organisation")];

    /// <summary>
    /// The screen that would show what a recipe added, for the check that it actually ran.
    /// </summary>
    /// <remarks>
    /// By the recipe's own name rather than by a third tuple element, because there are two kinds
    /// and they are told apart by what they are called. A recipe that seeds people is proved by the
    /// employee list; everything else by the designer.
    /// </remarks>
    private static string EvidencePathFor(string fileName) =>
        fileName.Contains("-employees", StringComparison.Ordinal)
            ? "/Admin/Employees/Index?search="
            : "/Admin/Dimensions/Designer/Index";

    /// <summary>
    /// Seeds the organisation the way an operator would: by running the shipped recipes from the
    /// admin screen, not by calling services. What the browser then sees is what someone following
    /// the module README would see.
    /// </summary>
    private async Task ApplyRecipesAsync(HttpClient client)
    {
        foreach (var (fileName, evidence) in Recipes)
        {
            var page = await GetAsync(client, "/Admin/Recipes");

            await client.PostAsync(
                "/Admin/Recipes/Execute?basePath=Areas%2FWorkMate.Dimensions%2FRecipes&fileName=" + fileName,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
                }));

            // The recipe screen redirects whether or not a step failed, so the status code proves
            // nothing. A screen that would show what the recipe added is the evidence: either it is
            // there or every test in this suite is about to fail for a reason that has nothing to
            // do with the browser.
            //
            // Which screen depends on what the recipe carries. An organisation shows up on the
            // designer; people do not, and a recipe of 158 employees that silently did nothing
            // would otherwise be found by a browser test timing out on an empty picker.
            var path = EvidencePathFor(fileName);
            var screen = await GetAsync(client, path);

            if (!screen.Contains(evidence, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{fileName}' ran but {path} does not mention '{evidence}'.");
            }
        }
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {path} returned {(int)response.StatusCode}.{Environment.NewLine}{body[..Math.Min(1000, body.Length)]}");
        }

        return body;
    }

    private static string AntiforgeryTokenIn(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return match.Success
            ? match.Groups["token"].Value
            : throw new InvalidOperationException("No antiforgery token on the page; it is not the form expected.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
    }

    public static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

[CollectionDefinition(Name)]
public sealed class UsesTheBrowserTenant : ICollectionFixture<BrowserTenantFixture>
{
    public const string Name = "browser tenant";
}

/// <summary>
/// A position created by one test for that test to fill, so the recipe's posts stay pristine.
/// </summary>
/// <param name="GradeNames">
/// The grades it allows, as the picker labels them — which is what a test selects by.
/// </param>
public sealed record ThrowawayPosition(string Code, string Name, IReadOnlyList<string> GradeNames);
