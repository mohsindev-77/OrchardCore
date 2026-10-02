using System.Text.RegularExpressions;
using FluentAssertions;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// Nothing in this module may save a concurrency-checked document except through
/// <see cref="DimensionDocuments.SaveCheckedAsync"/>.
/// </summary>
/// <remarks>
/// This is a source-level guard, which is unusual enough to justify.
///
/// YesSql's concurrency check is opt-in per call: the store-level registration
/// <c>IConfiguration.CheckConcurrentUpdates</c> is not reachable from a module, because Orchard
/// builds the store configuration itself and <c>YesSqlOptions</c> exposes no hook for it. The
/// consequence is that one <c>session.SaveAsync(document)</c> written anywhere, by anyone, at
/// any point in the life of this codebase, turns the guarantee off for that path. It compiles,
/// it passes every behavioural test, and the only symptom is that one day two administrators
/// edit the same subtree and one of them loses work with no error and no trace.
///
/// There is no type-level way to prevent that: <c>ISession.SaveAsync</c> is Orchard's interface
/// and takes <c>object</c>. A test that reads the source is the honest remaining option, and it
/// is cheap. If YesSql or Orchard ever exposes the store-level registration, this test and the
/// helper both go away and the guarantee becomes structural instead.
/// </remarks>
public sealed class ConcurrencyCheckedSaveTests
{
    /// <summary>
    /// A <c>SaveAsync</c> call, with whatever is inside the brackets, across line breaks.
    /// </summary>
    private static readonly Regex SaveCall = new(
        @"\.SaveAsync\s*\((?<arguments>[^;]*?)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The one file allowed to call <c>SaveAsync</c> directly: the helper that always passes the
    /// flag.
    /// </summary>
    private const string TheHelper = "DimensionDocuments.cs";

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
            "every write in this module must go through ISession.SaveCheckedAsync, which always "
            + "passes checkConcurrency: true. A direct SaveAsync silently drops the guarantee "
            + "ADR-0005 depends on, and the symptom is lost work with no error.{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void EveryConcurrencyCheckedDocumentTypeActuallyExistsOrIsPlanned()
    {
        // The list names types by string so it can include the graph documents before they are
        // written and without making the internal ones public. The cost of that is that a typo
        // or a rename would leave an entry pointing at nothing. This finds the ones that have
        // arrived and checks they carry what the mechanism needs; the rest are accounted for by
        // name against the source tree.
        foreach (var fullName in DimensionDocuments.ConcurrencyCheckedDocuments)
        {
            var type = typeof(DimensionDocuments).Assembly.GetType(fullName);

            if (type is null)
            {
                var fileName = fullName.Split('.').Last() + ".cs";

                ModuleSources().Any(source => Path.GetFileName(source) == fileName)
                    .Should().BeFalse(
                        "'{0}' has a source file but no such type: the entry is stale or misspelt",
                        fullName);

                continue;
            }

            var version = type.GetProperty("Version");

            version.Should().NotBeNull(
                "{0} is written with the concurrency check, which YesSql drives from a Version property",
                type.Name);

            version!.PropertyType.Should().Be<long>();
            version.CanWrite.Should().BeTrue("YesSql assigns the token after a successful write");
        }
    }

    [Fact]
    public void TheDocumentsWrittenTodayAreOnTheList()
    {
        // The guard above proves how documents are saved, not which documents are covered. This
        // is the other half: a new document type added to the module without being listed here
        // is covered by nothing.
        DimensionDocuments.ConcurrencyCheckedDocuments.Should().Contain(
        [
            "WorkMate.Dimensions.Models.DimensionTypeDocument",
            "WorkMate.Dimensions.Models.StructureDocument",
        ]);
    }

    private static string Collapse(string value) =>
        Regex.Replace(value, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(5)).Trim();

    private static IEnumerable<string> ModuleSources() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "WorkMate.Dimensions"),
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
