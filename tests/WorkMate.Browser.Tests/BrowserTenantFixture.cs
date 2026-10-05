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
public sealed class BrowserTenantFixture : WebApplicationFactory<Program>, IAsyncLifetime
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
        await ApplyDemoRecipeAsync(client);

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

    private async Task<string> SignInWithTheBrowserAsync()
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
        await page.FillAsync("input[name='LoginForm.UserName']", AdminUserName);
        await page.FillAsync("input[name='LoginForm.Password']", AdminPassword);
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
    /// Seeds the demo organisation the way an operator would: by running the shipped demo recipe
    /// from the admin screen, not by calling services. What the browser then sees is what someone
    /// following the module README would see.
    /// </summary>
    private static async Task ApplyDemoRecipeAsync(HttpClient client)
    {
        var page = await GetAsync(client, "/Admin/Recipes");

        await client.PostAsync(
            "/Admin/Recipes/Execute?basePath=Areas%2FWorkMate.Dimensions%2FRecipes&fileName=organisation-designer-demo.recipe.json",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
            }));

        // The recipe screen redirects whether or not a step failed, so the status code proves
        // nothing. The designer itself is the evidence: either the demo organisation is on it or
        // every test in this suite is about to fail for a reason that has nothing to do with the
        // browser.
        var designer = await GetAsync(client, "/Admin/Dimensions/Designer/Index");

        if (!designer.Contains("WorkMate Demo Organisation", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The demo recipe ran but the organisation designer does not show the demo organisation.");
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
