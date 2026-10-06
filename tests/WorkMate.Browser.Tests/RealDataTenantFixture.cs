using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Scope;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Browser.Tests;

/// <summary>
/// The host running against a <em>copy</em> of the developer's own <c>App_Data</c> — their real
/// tenant, their real organisation, their real settings — driven by a real browser.
/// </summary>
/// <remarks>
/// A fresh tenant built from <c>base.recipe.json</c> and the demo recipe is the right place to
/// assert what the designer does, and it is where <see cref="BrowserTenantFixture"/> runs. It is
/// the wrong place to reproduce a defect that only appears on a tenant with history: a tenant that
/// predates several schema changes, that has structures and dimension types created by hand
/// through the admin screens as well as by recipe, and whose site settings were written by an
/// older version of the base recipe. This fixture exists for exactly that difference.
///
/// It never touches the real App_Data. The copy is made before the host starts, the content root
/// points at the copy, and <see cref="AssertTheDeveloperDataWasNotTouched"/> compares the real
/// database's size and timestamp before and after. Everything this fixture writes — the test
/// administrator it creates to sign in with, and any migration the shell runs on start — lands in
/// the copy.
///
/// Skipped, rather than failed, when there is no developer App_Data to copy: this suite has to be
/// runnable on a build agent and on a clean clone, where the only honest answer is that there is
/// nothing of the developer's to reproduce against.
/// </remarks>
public sealed class RealDataTenantFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestAdminUserName = "workmate-browser-probe";
    public const string TestAdminPassword = "Workmate!Probe1";

    public const string ApplicationDataVariable = "WORKMATE_REAL_APPDATA";

    /// <summary>
    /// The App_Data to copy, named by <c>WORKMATE_REAL_APPDATA</c>: a snapshot taken before a
    /// tenant changed, or a copy of a customer's data sent in with a bug report.
    /// </summary>
    /// <remarks>
    /// Opt-in, with no default. Falling back to whatever is in <c>src/WorkMate.Web/App_Data</c>
    /// was worse than useless: on a build agent and on a clean clone there is nothing there, and
    /// on another developer's machine there is something there that these tests know nothing
    /// about. Either way the suite would be reporting on a tenant nobody chose. Unset means
    /// "nobody has offered me real data to reproduce against", and the honest response to that is
    /// to stand down and say so.
    /// </remarks>
    private static readonly string? RealApplicationData =
        Environment.GetEnvironmentVariable(ApplicationDataVariable) is { } configured
            && !string.IsNullOrWhiteSpace(configured)
                ? configured.Trim()
                : null;

    /// <summary>The structure the designer tests drive, seeded by the shipped demo recipe.</summary>
    public const string DemoStructureCode = "demo-org";

    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "workmate-realdata", Guid.NewGuid().ToString("n"));

    private IHost? _kestrel;
    private IPlaywright? _playwright;
    private string _signedInState = string.Empty;
    private (long Length, DateTime LastWriteUtc)? _realDatabaseBefore;

    public string ServerAddress { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = default!;

    /// <summary>Why this fixture could not run, or null when it did.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>
    /// The browser channel in use: a real installed Chrome or Edge when one is present, else
    /// Playwright's bundled Chromium. The developer's report is from a shipped browser, and a
    /// defect that only appears there would never show up against the bundled build.
    /// </summary>
    public string BrowserChannel { get; private set; } = "chromium (bundled)";

    /// <summary>
    /// Why this suite cannot run from the environment alone, or null when it can. Static and
    /// cheap, because <see cref="RealDataFactAttribute"/> asks it at discovery time, long before
    /// any host exists.
    /// </summary>
    public static string? EnvironmentSkipReason =>
        RealApplicationData is null
            ? $"{ApplicationDataVariable} is not set, so there is no real tenant to reproduce against. "
                + "Point it at a copy of an App_Data folder to run this suite."
            : !Directory.Exists(Path.Combine(RealApplicationData, "Sites"))
                ? $"{ApplicationDataVariable} is set to '{RealApplicationData}', which has no Sites folder, "
                    + "so it is not an App_Data directory."
                : null;

    public async Task InitializeAsync()
    {
        if (EnvironmentSkipReason is { } unavailable)
        {
            SkipReason = unavailable;
            return;
        }

        _realDatabaseBefore = RealDatabaseState();

        CopyDeveloperDataToTheContentRoot();

        _ = Services;

        // Two steps, and both are needed. InitializeAsync makes the host aware the developer's
        // tenant exists; it does not take that tenant through activation, and a shell that has
        // been loaded but never activated serves an admin screen with no data on it at all —
        // which looks exactly like a tenant whose records have vanished. A real request is what
        // activates it, and it is retried because the first one can arrive before the tenant's
        // routes are mapped and come back 404.
        await ActivateTheTenantAsync();

        _playwright = await Playwright.CreateAsync();
        Browser = await LaunchAsync(_playwright);

        await CreateTheProbeAdministratorAsync();

        if (await TheDemoStructureIsMissingAsync() is { } missing)
        {
            SkipReason = missing;
            return;
        }

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

        AssertTheDeveloperDataWasNotTouched();

        // The copy carries the developer's real organisation. It does not outlive the run.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_contentRoot))
                {
                    Directory.Delete(_contentRoot, recursive: true);
                }

                break;
            }
            catch (IOException)
            {
                // SQLite can still hold the file for a moment after the shell is released.
                await Task.Delay(500);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }
    }

    public Task<IBrowserContext> SignedInContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = ServerAddress,
            StorageState = _signedInState,
        });

    /// <summary>
    /// Prefers a browser the developer actually has installed. Playwright's bundled Chromium is a
    /// different build from shipped Chrome and Edge, and the whole point of this fixture is to stop
    /// assuming the difference does not matter.
    /// </summary>
    private async Task<IBrowser> LaunchAsync(IPlaywright playwright)
    {
        foreach (var channel in new[] { "msedge", "chrome" })
        {
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Channel = channel,
                    Headless = true,
                });

                BrowserChannel = channel;

                return browser;
            }
            catch (PlaywrightException)
            {
                // Not installed on this machine; try the next one.
            }
        }

        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>
    /// Why the designer tests cannot run against this tenant, or null when they can. A developer's
    /// tenant is theirs: they can re-run setup on it, or never have applied the demo recipe to it
    /// at all. Saying so is better than nine assertions failing on a tree that was never there.
    /// </summary>
    private async Task<string?> TheDemoStructureIsMissingAsync()
    {
        var shellHost = _kestrel!.Services.GetRequiredService<IShellHost>();
        shellHost.TryGetSettings("Default", out var settings);

        var scope = await shellHost.GetScopeAsync(settings!);
        string? reason = null;

        await scope.UsingAsync(async shellScope =>
        {
            var structures = await shellScope.ServiceProvider
                .GetRequiredService<IStructureService>()
                .ListAsync(CancellationToken.None);

            if (!structures.Any(structure => structure.Code == DemoStructureCode))
            {
                reason = $"The tenant copied from {RealApplicationData} has no '{DemoStructureCode}' structure "
                    + $"(it has: {(structures.Count == 0 ? "none" : string.Join(", ", structures.Select(s => s.Code)))}). "
                    + "Run the organisation designer demo recipe on it, or point WORKMATE_REAL_APPDATA "
                    + "at an App_Data that has it.";
            }
        });

        return reason;
    }

    private async Task ActivateTheTenantAsync()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(ServerAddress),
        };

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await client.GetAsync("/Login");

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Found)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException(
            "The copied tenant never answered /Login, so it never activated.");
    }

    /// <summary>
    /// Creates an administrator in the copied tenant, because the developer's own password is not
    /// ours to have and signing in is the only way to reach an admin screen.
    /// </summary>
    private async Task CreateTheProbeAdministratorAsync()
    {
        // From the Kestrel host, which is the one that has actually served a request and therefore
        // the one whose shell is running the developer's tenant.
        var shellHost = _kestrel!.Services.GetRequiredService<IShellHost>();

        if (!shellHost.TryGetSettings("Default", out var settings))
        {
            throw new InvalidOperationException(
                "The copied App_Data has no 'Default' tenant: " +
                string.Join(", ", shellHost.GetAllSettings().Select(s => $"{s.Name} ({s.State})")));
        }

        var scope = await shellHost.GetScopeAsync(settings);

        await scope.UsingAsync(async shellScope =>
        {
            var userService = shellScope.ServiceProvider.GetRequiredService<IUserService>();

            if (await userService.GetUserAsync(TestAdminUserName) is not null)
            {
                return;
            }

            var user = new User
            {
                UserName = TestAdminUserName,
                Email = $"{TestAdminUserName}@example.invalid",
                EmailConfirmed = true,
                IsEnabled = true,
                RoleNames = ["Administrator"],
            };

            await userService.CreateUserAsync(user, TestAdminPassword, (key, message) =>
                throw new InvalidOperationException($"Creating the probe administrator failed: {key}: {message}"));
        });
    }

    private async Task<string> SignInWithTheBrowserAsync()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = ServerAddress,
        });

        var page = await context.NewPageAsync();

        await page.GotoAsync("/Login?ReturnUrl=%2FAdmin");
        await page.FillAsync("input[name='LoginForm.UserName']", TestAdminUserName);
        await page.FillAsync("input[name='LoginForm.Password']", TestAdminPassword);
        await page.ClickAsync("form:has(input[name='LoginForm.Password']) button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Login", StringComparison.OrdinalIgnoreCase));

        return await context.StorageStateAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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

        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment(Environments.Production);
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    /// <summary>
    /// Lays out a content root that is the developer's, not a test's: their App_Data, their host
    /// configuration, and the PO files and recipes their build copies into place.
    /// </summary>
    private void CopyDeveloperDataToTheContentRoot()
    {
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));

        var host = Path.Combine(RepositoryRoot(), "src", "WorkMate.Web");

        // Everything except the logs, which are large, irrelevant and written to constantly.
        CopyDirectory(RealApplicationData!, Path.Combine(_contentRoot, "App_Data"), skip: "logs");

        foreach (var folder in new[] { "Localization", "Recipes" })
        {
            var source = Path.Combine(host, folder);

            if (Directory.Exists(source))
            {
                CopyDirectory(source, Path.Combine(_contentRoot, folder), skip: null);
            }
        }

        foreach (var file in new[] { "appsettings.json", "nlog.config" })
        {
            var source = Path.Combine(host, file);

            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(_contentRoot, file), overwrite: true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination, string? skip)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);

            if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name), skip);
        }
    }

    private static (long Length, DateTime LastWriteUtc)? RealDatabaseState()
    {
        if (RealApplicationData is null)
        {
            return null;
        }

        var database = new FileInfo(Path.Combine(RealApplicationData, "Sites", "Default", "OrchardCore.db"));

        return database.Exists ? (database.Length, database.LastWriteTimeUtc) : null;
    }

    /// <summary>
    /// The developer's own tenant must come out of this exactly as it went in. If the content root
    /// redirect ever stops working, this says so loudly instead of quietly editing someone's data.
    /// </summary>
    private void AssertTheDeveloperDataWasNotTouched()
    {
        if (_realDatabaseBefore is not { } before)
        {
            return;
        }

        if (RealDatabaseState() is not { } after || after != before)
        {
            throw new InvalidOperationException(
                "The developer's own App_Data changed while this fixture ran. The content root redirect is not working.");
        }
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
public sealed class UsesTheRealDataTenant : ICollectionFixture<RealDataTenantFixture>
{
    public const string Name = "real data tenant";
}
