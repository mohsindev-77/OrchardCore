using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Scope;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Starts the real host, creates one fresh tenant with the base recipe, and signs in — once for
/// the whole collection, because standing a tenant up is the expensive part and every assertion
/// is a read.
///
/// The tenant is real: the same `recipes/base.recipe.json` the pipeline applies and a customer
/// gets, through the same setup endpoint, on SQLite. That is the point. Structural checks on the
/// recipe live in the unit suite; this answers the question those cannot, which is whether
/// applying it produces the tenant the specification describes.
/// </summary>
public sealed class BaseTenantFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TenantName = "Default";
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Workmate!Integration1";

    /// <summary>
    /// The tenant's own content root, under the temp directory.
    /// </summary>
    /// <remarks>
    /// Orchard puts App_Data under the content root, so giving the test its own content root is
    /// what keeps it away from the App_Data a developer is running locally. Redirecting
    /// ShellOptions.ShellsApplicationDataPath through configuration was tried first and silently
    /// did nothing — the tenant was created in the host project instead — which is why
    /// <see cref="AssertTheDeveloperTenantWasNotTouched"/> exists.
    /// </remarks>
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "workmate-integration", Guid.NewGuid().ToString("n"));

    private string ApplicationData => Path.Combine(_contentRoot, "App_Data");

    /// <summary>A client signed in as the site administrator.</summary>
    public HttpClient Administrator { get; private set; } = default!;

    /// <summary>A client that has never signed in, for checking what an outsider can reach.</summary>
    public HttpClient Anonymous { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Anonymous = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Administrator = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

        await SetUpTenantAsync();
        await SignInAsync();

        AssertTheDeveloperTenantWasNotTouched();
    }

    public new async Task DisposeAsync()
    {
        Administrator?.Dispose();
        Anonymous?.Dispose();

        await base.DisposeAsync();

        // Best effort: a left-behind temp tenant costs disk, not correctness.
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
    /// Runs <paramref name="work"/> inside the tenant's shell scope, so a test can ask Orchard's
    /// own services what the tenant looks like instead of inferring it from HTML.
    /// </summary>
    public async Task InTenantAsync(Func<IServiceProvider, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var shellHost = Services.GetRequiredService<IShellHost>();

        if (!shellHost.TryGetSettings(TenantName, out var settings))
        {
            throw new InvalidOperationException($"The '{TenantName}' tenant does not exist. Setup did not complete.");
        }

        var scope = await shellHost.GetScopeAsync(settings);

        await scope.UsingAsync(async shellScope =>
        {
            await work(shellScope.ServiceProvider);
        });
    }

    /// <summary>
    /// Creates a user with exactly one role and signs in as them, so a test can assert what a
    /// screen or an API forbids to someone who is authenticated but lacks the permission it needs
    /// — a different question from what <see cref="Anonymous"/> answers.
    /// </summary>
    /// <remarks>
    /// The user is created directly through <see cref="IUserService"/> inside the tenant's shell
    /// scope rather than by scraping the admin "create user" form, because this fixture already
    /// has a reliable way into that scope (<see cref="InTenantAsync"/>) and the admin form's field
    /// names are not part of the contract this suite is pinning. Creation failures surface by
    /// throwing from the reportError callback: if that never fires but the subsequent sign-in
    /// still fails, the failure is in sign-in, not creation, and the exception says so.
    /// </remarks>
    public async Task<HttpClient> CreateSignedInClientAsync(
        string userName,
        string password,
        string roleName,
        bool allowAutoRedirect = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        await InTenantAsync(async services =>
        {
            var userService = services.GetRequiredService<IUserService>();

            var user = new User
            {
                UserName = userName,
                Email = $"{userName}@example.invalid",
                EmailConfirmed = true,
                IsEnabled = true,
                RoleNames = [roleName],
            };

            await userService.CreateUserAsync(user, password, (key, message) =>
                throw new InvalidOperationException($"Creating test user '{userName}' failed: {key}: {message}"));
        });

        // Built with whatever redirect behaviour the caller asked for, because the same client
        // signs in here and then makes the request the test actually wants to observe: a test
        // checking for a 403/redirect on a forbidden screen needs AllowAutoRedirect: false for
        // that request too, or it never sees the status code it is asserting on. A successful
        // sign-in POST still redirects (302) either way; this client just does not follow it.
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = allowAutoRedirect });
        var page = await GetPageAsync(client, "/Login");

        var response = await client.PostAsync(
            "/Login?ReturnUrl=%2FAdmin",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
                ["LoginForm.UserName"] = userName,
                ["LoginForm.Password"] = password,
            }));

        // Found when this client does not follow redirects, OK when it does and landed on /Admin.
        if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.OK))
        {
            throw new InvalidOperationException(
                $"Signing in test user '{userName}' returned {(int)response.StatusCode}, expected a redirect.");
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        PrepareContentRoot();

        builder.UseContentRoot(_contentRoot);

        // A test subscriber to the employee lifecycle events, on every tenant this host creates.
        // Specification section 5 requires those events to reach leave, attendance and payroll;
        // none of those modules exists yet, so the only way to prove they reach anybody is to be
        // somebody. See RecordingLifecycleHandler.
        builder.ConfigureServices(services => services.AddRecordingLifecycleHandler());

        // Production, not Development. In Development Orchard compiles Razor views at runtime,
        // and the runtime compiler in a test host has no language version to go on, so it falls
        // back to C# 8 and fails on Orchard's own views ("Feature 'not pattern' is not available
        // in C# 8.0"). Production uses the views compiled into the module assemblies, which is
        // also what the pipeline and a real deployment do.
        builder.UseEnvironment(Environments.Production);
    }

    /// <summary>
    /// Lays out what Orchard reads from the content root: the recipes, so the base recipe is
    /// offered at setup, and the PO files. Both are copied from where they are tracked rather
    /// than from the host's folders, so the test does not depend on a build having copied them
    /// there first. Modules themselves are found through the referenced assemblies, not here.
    /// </summary>
    private void PrepareContentRoot()
    {
        SweepAbandonedContentRoots();

        Directory.CreateDirectory(_contentRoot);

        // The media module's image cache refuses to start without a web root, and an empty one
        // is enough: these tests serve no static files.
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));

        var repositoryRoot = RepositoryRoot;

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

                foreach (var po in Directory.EnumerateFiles(culture, "*.po"))
                {
                    File.Copy(po, Path.Combine(destination, Path.GetFileName(po)), overwrite: true);
                }
            }
        }
    }

    /// <summary>
    /// Removes content roots left behind by earlier runs.
    /// </summary>
    /// <remarks>
    /// Cleanup at the end of a run is best effort and sometimes loses: SQLite can still hold the
    /// tenant's database file when the fixture is disposed. Sweeping at the start, once the files
    /// are long since released, keeps a build agent from accumulating a tenant per run forever.
    /// Only roots older than an hour are touched, so a run cannot delete another run's tenant.
    /// </remarks>
    private static void SweepAbandonedContentRoots()
    {
        var parent = Path.Combine(Path.GetTempPath(), "workmate-integration");

        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var candidate in Directory.EnumerateDirectories(parent))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(candidate) < DateTime.UtcNow.AddHours(-1))
                {
                    Directory.Delete(candidate, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Fetches a page and, when it fails, says what the server said. A fixture that cannot start
    /// should explain itself; "500 Internal Server Error" on nine tests at once explains nothing.
    /// </summary>
    /// <summary>Public so other tests in this project get the same explanatory failure on a non-2xx GET.</summary>
    public static async Task<string> GetPageAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var detail = body.Length > 2000 ? body[..2000] : body;

            throw new InvalidOperationException(
                $"GET {path} returned {(int)response.StatusCode}.{Environment.NewLine}{detail}");
        }

        return body;
    }

    private async Task SetUpTenantAsync()
    {
        var page = await GetPageAsync(Anonymous, "/");

        var response = await Anonymous.PostAsync("/", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
            ["SiteName"] = "WorkMate Integration",
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

    private async Task SignInAsync()
    {
        // Setup restarts the shell, which invalidates the antiforgery cookie issued with the
        // setup page, so the login form is fetched with a client that has not seen it.
        var page = await GetPageAsync(Administrator, "/Login");

        var response = await Administrator.PostAsync(
            "/Login?ReturnUrl=%2FAdmin",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AntiforgeryTokenIn(page),
                ["LoginForm.UserName"] = AdminUserName,
                ["LoginForm.Password"] = AdminPassword,
            }));

        response.EnsureSuccessStatusCode();

        var admin = await Administrator.GetAsync("/Admin");

        if (admin.RequestMessage?.RequestUri?.AbsolutePath.Contains("Login", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("Signing in as the site administrator did not work.");
        }
    }

    /// <summary>
    /// Running the suite must never create or disturb the App_Data a developer has locally. If
    /// the configuration key that redirects it ever changes, this fails loudly rather than
    /// quietly eating someone's tenant.
    /// </summary>
    private void AssertTheDeveloperTenantWasNotTouched() =>
        Directory.Exists(Path.Combine(ApplicationData, "Sites", TenantName)).Should().BeTrue(
            "the tenant Orchard created should be the one under the temp content root, "
            + "not the host project's App_Data");

    /// <summary>Public so other tests in this project can POST to a screen beyond setup and login.</summary>
    public static string AntiforgeryTokenIn(string html)
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

    private static string RepositoryRoot
    {
        get
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

            throw new InvalidOperationException("Could not find the repository root above the test output directory.");
        }
    }
}

/// <summary>
/// One tenant for every test in the collection. Creating it is the only slow part, so it is done
/// once and the assertions share it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UsesTheBaseTenant : ICollectionFixture<BaseTenantFixture>
{
    public const string Name = "base tenant";
}
