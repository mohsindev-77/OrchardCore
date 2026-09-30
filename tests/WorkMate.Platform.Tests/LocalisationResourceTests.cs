using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// The localisation gate. Specification section 9: "A pull request that adds a user-visible
/// string without a resource entry fails review." This makes it fail the build instead.
///
/// It scans every WorkMate module's C# and Razor sources for localiser calls and asserts that
/// each key has an Arabic entry in that module's PO file. English needs no file: it is the
/// msgid.
/// </summary>
public sealed class LocalisationResourceTests
{
    [Theory]
    [MemberData(nameof(Modules))]
    public void EveryLocalisedStringHasAnArabicResourceEntry(string moduleName)
    {
        var module = Path.Combine(RepositoryRoot, "src", moduleName);
        var keys = LocaliserKeysIn(module);

        if (keys.Count == 0)
        {
            return;
        }

        var poFile = Path.Combine(module, "Localization", "ar", $"{moduleName}.po");

        File.Exists(poFile).Should().BeTrue(
            "{0} raises {1} localised string(s), so it must ship {2}",
            moduleName,
            keys.Count,
            Path.GetRelativePath(RepositoryRoot, poFile));

        var translated = MessageIdsIn(poFile);
        var missing = keys.Where(key => !translated.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty(
            "every user-visible string needs a resource entry. Add these msgids to {0}:{1}{2}",
            Path.GetRelativePath(RepositoryRoot, poFile),
            Environment.NewLine,
            string.Join(Environment.NewLine, missing.Select(key => $"  msgid \"{key}\"")));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void NoResourceEntryIsLeftBehindByAStringThatHasGone(string moduleName)
    {
        var module = Path.Combine(RepositoryRoot, "src", moduleName);
        var poFile = Path.Combine(module, "Localization", "ar", $"{moduleName}.po");

        if (!File.Exists(poFile))
        {
            return;
        }

        var keys = LocaliserKeysIn(module);
        var orphaned = MessageIdsIn(poFile)
            .Where(msgid => !keys.Contains(msgid))
            .OrderBy(msgid => msgid, StringComparer.Ordinal)
            .ToList();

        orphaned.Should().BeEmpty(
            "a translation whose string no longer exists is dead weight and hides a rename. "
            + "Remove these msgids from {0}:{1}{2}",
            Path.GetRelativePath(RepositoryRoot, poFile),
            Environment.NewLine,
            string.Join(Environment.NewLine, orphaned.Select(msgid => $"  msgid \"{msgid}\"")));
    }

    public static TheoryData<string> Modules()
    {
        var data = new TheoryData<string>();

        foreach (var directory in Directory.GetDirectories(Path.Combine(RepositoryRoot, "src")))
        {
            var name = Path.GetFileName(directory);

            // The host carries no business code and no strings of its own, and Core has no
            // Orchard dependency and so no localiser.
            if (name is "WorkMate.Web" or "WorkMate.Core")
            {
                continue;
            }

            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// Matches a localiser indexer call: S["..."], H["..."], T["..."], and the same with a
    /// verbatim string. These are the four names Orchard uses for IStringLocalizer,
    /// IHtmlLocalizer and IViewLocalizer, and the convention this codebase follows.
    /// </summary>
    private static readonly Regex LocaliserCall = new(
        """"(?<![\w.])(?:S|H|T)\[\s*@?"(?<key>(?:[^"\\]|\\.)*)"""",
        RegexOptions.Compiled);

    private static readonly Regex MessageId = new(
        """^\s*msgid\s+"(?<key>(?:[^"\\]|\\.)*)"\s*$""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static HashSet<string> LocaliserKeysIn(string moduleDirectory)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        if (!Directory.Exists(moduleDirectory))
        {
            return keys;
        }

        var sources = Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(moduleDirectory, "*.cshtml", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path));

        foreach (var source in sources)
        {
            foreach (Match match in LocaliserCall.Matches(File.ReadAllText(source)))
            {
                keys.Add(Unescape(match.Groups["key"].Value));
            }
        }

        return keys;
    }

    private static HashSet<string> MessageIdsIn(string poFile)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in MessageId.Matches(File.ReadAllText(poFile)))
        {
            var key = Unescape(match.Groups["key"].Value);

            // The empty msgid carries the PO header, not a translation.
            if (key.Length > 0)
            {
                ids.Add(key);
            }
        }

        return ids;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Unescape(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal)
             .Replace("\\n", "\n", StringComparison.Ordinal)
             .Replace("\\\\", "\\", StringComparison.Ordinal);

    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

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
