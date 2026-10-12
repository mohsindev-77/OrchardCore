using System.Text.RegularExpressions;
using FluentAssertions;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// Nothing in this module may save a concurrency-checked document except through
/// <see cref="RecordsDocuments.SaveCheckedAsync"/>.
/// </summary>
/// <remarks>
/// The same source-level guard <c>WorkMate.Dimensions.Tests.ConcurrencyCheckedSaveTests</c> keeps,
/// and it is unusual enough to justify again rather than by reference.
///
/// YesSql's concurrency check is opt-in per call: the store-level registration
/// <c>IConfiguration.CheckConcurrentUpdates</c> is not reachable from a module, because Orchard
/// builds the store configuration itself and <c>YesSqlOptions</c> exposes no hook for it. So one
/// <c>session.SaveAsync(document)</c> written anywhere, by anyone, at any point in the life of this
/// codebase, turns the guarantee off for that path. It compiles, it passes every behavioural test,
/// and the only symptom is that one day two administrators edit the same lookup list and one of
/// them loses their entries with no error and no trace.
///
/// There is no type-level way to prevent it: <c>ISession.SaveAsync</c> is Orchard's interface and
/// takes <c>object</c>. A test that reads the source is the honest remaining option, and it is
/// cheap. If YesSql or Orchard ever exposes the store-level registration, this test and the helper
/// both go away and the guarantee becomes structural instead.
/// </remarks>
public sealed class ConcurrencyCheckedSaveTests
{
    /// <summary>A <c>SaveAsync</c> call, with whatever is inside the brackets, across line breaks.</summary>
    private static readonly Regex SaveCall = new(
        @"\.SaveAsync\s*\((?<arguments>[^;]*?)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    /// <summary>The one file allowed to call <c>SaveAsync</c> directly: the helper that always passes the flag.</summary>
    private const string TheHelper = "RecordsDocuments.cs";

    [Fact]
    public void NoCodeInTheModuleSavesWithoutTheConcurrencyCheck()
    {
        var offences = new List<string>();

        foreach (var source in ModuleSources())
        {
            if (Path.GetFileName(source) == TheHelper)
            {
                continue;
            }

            foreach (Match call in SaveCall.Matches(File.ReadAllText(source)))
            {
                var arguments = call.Groups["arguments"].Value;

                if (arguments.Contains("checkConcurrency: true", StringComparison.Ordinal))
                {
                    continue;
                }

                offences.Add(
                    $"  {Path.GetRelativePath(RepositoryRoot, source)}: "
                    + $".SaveAsync({Collapse(arguments)})");
            }
        }

        offences.Should().BeEmpty(
            "every document write in this module must go through ISession.SaveCheckedAsync, which "
            + "always passes checkConcurrency: true. A direct SaveAsync silently drops the "
            + "guarantee, and the symptom is lost work with no error.{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void EveryConcurrencyCheckedDocumentTypeCarriesWhatTheMechanismNeeds()
    {
        foreach (var fullName in RecordsDocuments.ConcurrencyCheckedDocuments)
        {
            var type = typeof(RecordsDocuments).Assembly.GetType(fullName);

            type.Should().NotBeNull("'{0}' is listed but no such type exists: the entry is stale or misspelt", fullName);

            var version = type!.GetProperty("Version");

            version.Should().NotBeNull(
                "{0} is written with the concurrency check, which YesSql drives from a Version property",
                type.Name);

            version!.PropertyType.Should().Be<long>();
            version.CanWrite.Should().BeTrue("YesSql assigns the token after a successful write");
        }
    }

    /// <summary>
    /// Every YesSql document this module defines is on the list.
    /// </summary>
    /// <remarks>
    /// The guard above proves how documents are saved, not which documents are covered. A new
    /// document type added without being listed is covered by nothing, so this finds them by the
    /// shape a YesSql document has — a settable <c>long Id</c> and a settable <c>long Version</c> —
    /// rather than by a list somebody has to remember to extend.
    ///
    /// Employees are content items and are written through <c>IContentManager</c>, which owns its
    /// own write path; they have no <c>Version</c> property of this shape and so are not found here.
    /// </remarks>
    [Fact]
    public void EveryDocumentTypeInTheModuleIsOnTheList()
    {
        var documents = typeof(RecordsDocuments).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.GetProperty("Id")?.PropertyType == typeof(long) &&
                type.GetProperty("Id")?.CanWrite == true &&
                type.GetProperty("Version")?.PropertyType == typeof(long) &&
                type.GetProperty("Version")?.CanWrite == true)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        documents.Should().NotBeEmpty("this module defines at least LookupListDocument");

        RecordsDocuments.ConcurrencyCheckedDocuments.Should().BeEquivalentTo(documents);
    }

    private static string Collapse(string value) =>
        Regex.Replace(value, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(5)).Trim();

    private static IEnumerable<string> ModuleSources() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "WorkMate.Records"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
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
