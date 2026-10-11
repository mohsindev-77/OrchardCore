using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Every Orchard feature a module needs is declared in that module's manifest.
/// </summary>
/// <remarks>
/// <b>The defect this exists for.</b> The employee record's sections are <c>BagPart</c>s, which the
/// <c>OrchardCore.Flows</c> feature registers. Session A1 added Flows to <c>recipes/base.recipe.json</c>
/// and nowhere else, and on a tenant created before that edit the sections did not work — silently,
/// because a part nothing has registered is not a broken part, it is a part Orchard has never heard
/// of. Nothing failed; the editor simply had no sections in it.
///
/// <b>Why the base recipe is not where this belongs.</b> A recipe is applied once, at setup. Editing
/// it changes what *new* tenants get and reaches no existing one, ever — there is no mechanism by
/// which it could. A manifest dependency is resolved every time a shell is built, so it reaches a
/// tenant set up last year as surely as one set up this morning. The recipe line is still worth
/// having, because it keeps the feature list of a fresh tenant readable in one place; it is just not
/// a substitute.
///
/// <b>How this test decides what a module needs.</b> From its package references, through
/// <see cref="FeatureRequiredBy"/> — a package that carries a feature's types implies that feature.
/// A package the table does not classify <em>fails the test</em>, which is the point: adding a
/// reference to a WorkMate module forces somebody to say whether it brings a feature with it. That
/// is the question nobody asked about <c>OrchardCore.Flows.Core</c>.
///
/// Satisfied through the dependency closure, not only directly: <c>WorkMate.Records</c> uses content
/// items and does not declare <c>OrchardCore.Contents</c>, because <c>WorkMate.Dimensions</c> does
/// and Records depends on it. Orchard resolves transitively, so demanding a direct declaration would
/// be demanding noise.
/// </remarks>
public sealed partial class ModuleFeatureDependencyTests
{
    [Theory]
    [MemberData(nameof(Modules))]
    public void EveryFeatureAModuleNeedsIsAManifestDependency(string moduleName)
    {
        var declared = ClosureOf(moduleName);
        var missing = new List<string>();

        foreach (var package in PackagesOf(moduleName))
        {
            if (!FeatureRequiredBy.TryGetValue(package, out var feature))
            {
                throw new InvalidOperationException(
                    $"{moduleName} references '{package}', which {nameof(ModuleFeatureDependencyTests)} does not "
                    + $"classify. Add it to {nameof(FeatureRequiredBy)}: either the Orchard feature it needs at "
                    + "runtime, or null with a comment saying why it needs none. A package nobody classified is "
                    + "how OrchardCore.Flows came to be missing from WorkMate.Records' manifest.");
            }

            if (feature is not null && !declared.Contains(feature))
            {
                missing.Add($"'{feature}' (needed by the reference to {package})");
            }
        }

        missing.Should().BeEmpty(
            "{0}'s manifest must declare every Orchard feature it needs, so that enabling it enables them — on "
            + "existing tenants as well as new ones. Add to src/{0}/Manifest.cs: {1}",
            moduleName,
            string.Join(", ", missing.Distinct().Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// What each Orchard package a WorkMate module may reference implies, as a feature id.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means the package needs no feature enabled, and each one says why. The
    /// distinction is "would this stop working if the feature were off": an abstraction whose
    /// implementation is registered by the host works regardless; one whose implementation lives in
    /// a module does not.
    /// </remarks>
    private static readonly Dictionary<string, string?> FeatureRequiredBy = new(StringComparer.Ordinal)
    {
        // Build-time only: an MSBuild targets package that makes the project a module. No runtime
        // surface at all.
        ["OrchardCore.Module.Targets"] = null,

        // YesSql's session and index registration, added by the host for every tenant before any
        // feature is enabled. Index providers are registered by the module's own Startup.
        ["OrchardCore.Data.YesSql"] = null,

        // IRecipeStepHandler and IRecipeExecutor are both interfaces in the abstractions package,
        // and the executor's implementation comes with setup itself — a tenant is created by
        // running a recipe, so recipes work before anything has been enabled. The base recipe still
        // enables OrchardCore.Recipes, which is the admin screen for running one by hand.
        ["OrchardCore.Recipes.Abstractions"] = null,

        // Shapes, display drivers and the shape table: host-level infrastructure every tenant has,
        // independent of which content features are on.
        ["OrchardCore.DisplayManagement"] = null,
        ["OrchardCore.Infrastructure.Abstractions"] = null,

        // The content item, IContentManager and the part/field base types. Registered by the
        // Contents feature, which is also what gives a content type its admin screens.
        ["OrchardCore.ContentManagement"] = "OrchardCore.Contents",
        ["OrchardCore.ContentManagement.Display"] = "OrchardCore.Contents",

        // The content-definition editor a field's settings driver is rendered into.
        ["OrchardCore.ContentTypes.Abstractions"] = "OrchardCore.ContentTypes",

        // Permissions. IPermissionProvider is collected by the Roles feature, which is what turns a
        // declared permission into something grantable.
        ["OrchardCore.Security"] = "OrchardCore.Roles",

        ["OrchardCore.Localization.Abstractions"] = "OrchardCore.Localization",
        ["OrchardCore.Localization"] = "OrchardCore.Localization",
        ["OrchardCore.Navigation.Core"] = "OrchardCore.Navigation",
        ["OrchardCore.Admin.Abstractions"] = "OrchardCore.Admin",
        ["OrchardCore.ResourceManagement"] = "OrchardCore.Resources",
        ["OrchardCore.Title"] = "OrchardCore.Title",
        ["OrchardCore.ContentFields"] = "OrchardCore.ContentFields",
        ["OrchardCore.AuditTrail.Abstractions"] = "OrchardCore.AuditTrail",
        ["OrchardCore.Deployment.Abstractions"] = "OrchardCore.Deployment",
        ["OrchardCore.Media"] = "OrchardCore.Media",

        // BagPart and its settings. The one that started this.
        ["OrchardCore.Flows.Core"] = "OrchardCore.Flows",
    };

    /// <summary>
    /// A module's Orchard feature dependencies, plus everything its WorkMate dependencies declare.
    /// </summary>
    private static HashSet<string> ClosureOf(string moduleName)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([moduleName]);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var dependency in DependenciesOf(current))
            {
                closure.Add(dependency);

                if (dependency.StartsWith("WorkMate.", StringComparison.Ordinal))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return closure;
    }

    private static IReadOnlyList<string> DependenciesOf(string moduleName)
    {
        var manifest = Path.Combine(RepositoryRoot, "src", moduleName, "Manifest.cs");

        if (!File.Exists(manifest))
        {
            return [];
        }

        var text = File.ReadAllText(manifest);
        var block = DependenciesBlock().Match(text);

        return block.Success
            ? [.. QuotedString().Matches(block.Groups[1].Value).Select(match => match.Groups[1].Value)]
            : [];
    }

    private static IReadOnlyList<string> PackagesOf(string moduleName)
    {
        var project = Path.Combine(RepositoryRoot, "src", moduleName, $"{moduleName}.csproj");

        return File.Exists(project)
            ?
            [
                .. OrchardPackageReference().Matches(File.ReadAllText(project))
                    .Select(match => match.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal),
            ]
            : [];
    }

    public static TheoryData<string> Modules()
    {
        var data = new TheoryData<string>();

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .Where(directory => File.Exists(Path.Combine(directory, "Manifest.cs")))
            .OrderBy(directory => directory, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkMate.sln")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull("the test runs inside the repository");

            return directory!.FullName;
        }
    }

    [GeneratedRegex(@"Dependencies\s*=\s*(?:new\[\]\s*)?[\{\[](.*?)[\}\]]", RegexOptions.Singleline)]
    private static partial Regex DependenciesBlock();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedString();

    [GeneratedRegex(@"PackageReference\s+Include=""(OrchardCore\.[^""]+)""")]
    private static partial Regex OrchardPackageReference();
}
