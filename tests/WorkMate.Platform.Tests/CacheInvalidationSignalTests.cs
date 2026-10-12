using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Nothing in this module may invalidate a cache except through
/// <c>CacheSignals.SignalNowAndAfterCommitAsync</c>. ADR-0019.
/// </summary>
/// <remarks>
/// The same shape as <c>WorkMate.Records.Tests.ConcurrencyCheckedSaveTests</c>, kept for the same
/// reason and against a defect of the same character: a call that compiles, passes every
/// behavioural test, and is wrong only when two things overlap.
///
/// <b>This module owns the helper, so this module is the one with an exemption</b> — and the
/// exemption is the point of having the guard here at all. <c>CacheSignals</c> is allowed to call
/// <c>SignalTokenAsync</c>, because calling it at the right three moments is what it is for.
/// Nothing else in Platform may, including whatever Platform caches next.
///
/// <c>ISignal.SignalTokenAsync</c> sends the signal while the write is still staged on the session.
/// A read arriving between that and the commit refills the cache from the state the write is about
/// to replace, under a token that has already been spent — and the entry is then wrong until the
/// process restarts. That is the defect ADR-0019 was written from, and it reached manual testing as
/// "the lookup lists are loaded and every screen still shows a free-text box".
///
/// There is no type-level way to prevent the direct call: <c>ISignal</c> is Orchard's interface and
/// <c>SignalTokenAsync</c> is the only method on it that sends anything. A test that reads the
/// source is the honest remaining option, and it is cheap.
/// </remarks>
public sealed class CacheInvalidationSignalTests
{
    private static readonly Regex DirectSignal = new(
        @"\.SignalTokenAsync\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>The one file allowed to signal directly: the helper that signals three times.</summary>
    private const string TheHelper = "CacheSignals.cs";

    [Fact]
    public void NoCodeInTheModuleSignalsACacheTokenDirectly()
    {
        var offences = ModuleSources()
            .Where(source => Path.GetFileName(source) != TheHelper)
            .Where(source => DirectSignal.IsMatch(ReadCode(source)))
            .Select(source => "  " + Path.GetRelativePath(RepositoryRoot, source))
            .ToList();

        offences.Should().BeEmpty(
            "every cache invalidation in this module must go through "
            + "CacheSignals.SignalNowAndAfterCommitAsync, which also signals after the write has "
            + "committed. A direct SignalTokenAsync signals only while the write is still staged, "
            + "and a read that overlaps it then holds the pre-write state until the process "
            + "restarts.{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, offences));
    }

    /// <summary>
    /// The file with the code stripped of comments.
    /// </summary>
    /// <remarks>
    /// Several files name <c>SignalTokenAsync</c> in a <c>&lt;see cref&gt;</c> or in prose, saying
    /// what is not being called and why. Reading those as offences would make the guard punish the
    /// documentation that explains it.
    /// </remarks>
    private static string ReadCode(string path) =>
        Regex.Replace(
            Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(5)),
            @"//.*?$",
            string.Empty,
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));

    private static IEnumerable<string> ModuleSources() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "WorkMate.Platform"),
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
