using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// The other half of the localisation gate.
///
/// <see cref="LocalisationResourceTests"/> checks that every string already routed through the
/// localiser has an Arabic entry. It cannot see a string that never reached the localiser at
/// all, which is the failure specification section 9 actually describes: "A pull request that
/// adds a user-visible string without a resource entry fails review."
///
/// These tests find those. Razor views are checked exhaustively, because that is where
/// user-visible text lives. C# is checked at a declared list of sinks whose string argument is
/// rendered to a user, because "is this C# literal user-visible?" is not decidable in general
/// and a guess would produce noise the team would learn to ignore.
/// </summary>
public sealed class UserVisibleStringTests
{
    [Theory]
    [MemberData(nameof(LocalisationResourceTests.Modules), MemberType = typeof(LocalisationResourceTests))]
    public void NoRazorViewRendersALiteralString(string moduleName)
    {
        var module = Path.Combine(RepositoryRoot, "src", moduleName);

        if (!Directory.Exists(module))
        {
            return;
        }

        var offences = new List<string>();

        foreach (var view in EnumerateSources(module, "*.cshtml"))
        {
            foreach (var literal in LiteralsRenderedBy(File.ReadAllText(view)))
            {
                offences.Add($"  {Path.GetRelativePath(RepositoryRoot, view)}: \"{literal}\"");
            }
        }

        offences.Should().BeEmpty(
            "text a user reads must go through the localiser, as @T[\"...\"] or @H[\"...\"].{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, offences));
    }

    [Theory]
    [MemberData(nameof(LocalisationResourceTests.Modules), MemberType = typeof(LocalisationResourceTests))]
    public void NoUserVisibleSinkIsGivenALiteralString(string moduleName)
    {
        var module = Path.Combine(RepositoryRoot, "src", moduleName);

        if (!Directory.Exists(module))
        {
            return;
        }

        var offences = new List<string>();

        foreach (var source in EnumerateSources(module, "*.cs"))
        {
            var text = File.ReadAllText(source);

            foreach (var sink in UserVisibleSinks.Where(sink => !ExcludedSinks.ContainsKey(sink.Name)))
            {
                foreach (var literal in LiteralsPassedTo(text, sink))
                {
                    offences.Add($"  {Path.GetRelativePath(RepositoryRoot, source)}: {sink.Name}(… \"{literal}\" …)");
                }
            }
        }

        offences.Should().BeEmpty(
            "a string rendered to a user must come from the localiser, not from a literal.{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void PermissionDescriptionsAreTheOnlyExcludedSinkAndTheReasonIsRecorded()
    {
        // This test exists so the exclusion list cannot grow quietly. Adding a sink here means
        // changing this test, which means a reviewer reads the justification.
        ExcludedSinks.Keys.Should().BeEquivalentTo(["Permission"]);
        ExcludedSinks["Permission"].Should().Contain("DataLocalization");
    }

    /// <summary>
    /// Calls whose string argument at <c>VisibleArgument</c> is rendered to a user. Add to this
    /// list whenever a new one appears; it is the whole of what the C# side checks.
    /// </summary>
    private static readonly (string Name, int VisibleArgument)[] UserVisibleSinks =
    [
        ("Permission", 1),      // Permission(name, description, …): the description shows in the roles editor.
        ("AddModelError", 1),   // ModelState.AddModelError(key, message): the message shows on the form.
        ("Information", 0),     // INotifier.Information(message) and its siblings show as admin notifications.
        ("Success", 0),
        ("Warning", 0),
        ("Error", 0),
    ];

    /// <summary>
    /// Permission descriptions are excluded, deliberately.
    ///
    /// In Orchard Core 3.0.1 a permission's description is not localised through the module's PO
    /// resources. It is surfaced as localisable data by OrchardCore.Roles'
    /// PermissionsLocalizationDataProvider and translated through the OrchardCore.DataLocalization
    /// feature, which the base recipe enables. Wrapping the description in IStringLocalizer would
    /// put the string in two places and get it translated in neither.
    ///
    /// This is the only exclusion, and
    /// <see cref="PermissionDescriptionsAreTheOnlyExcludedSinkAndTheReasonIsRecorded"/> keeps it
    /// that way. Anything added here needs justification of the same kind.
    /// </summary>
    private static readonly Dictionary<string, string> ExcludedSinks = new(StringComparer.Ordinal)
    {
        ["Permission"] =
            "Localised as data by OrchardCore.DataLocalization via PermissionsLocalizationDataProvider, "
            + "not by the module's PO resources.",
    };

    private static IEnumerable<string> EnumerateSources(string moduleDirectory, string pattern) =>
        Directory.EnumerateFiles(moduleDirectory, pattern, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // ---- Razor -------------------------------------------------------------------------


    private static readonly Regex RazorDirective = new(
        @"^\s*@(model|using|inherits|addTagHelper|removeTagHelper|inject|namespace|attribute|implements|typeparam|page|section|functions|code)\b.*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex UserVisibleAttribute = new(
        "\\b(?<name>placeholder|title|alt|aria-label)\\s*=\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Two or more letters in a row: enough to be a word rather than punctuation.</summary>
    private static readonly Regex LooksLikeProse = new(@"\p{L}{2,}", RegexOptions.Compiled);

    /// <summary>C# fragments that legitimately appear as bare text between Razor blocks.</summary>
    private static readonly string[] BareCodeLines = ["else", "do", "try", "catch", "finally"];

    internal static IEnumerable<string> LiteralsRenderedBy(string razor)
    {
        var text = razor;

        text = Replace(text, @"@\*.*?\*@");
        text = Replace(text, "<!--.*?-->");
        text = Replace(text, "<script[^>]*>.*?</script>");
        text = Replace(text, "<style[^>]*>.*?</style>");
        text = RazorDirective.Replace(text, string.Empty);
        text = BlankCodeBlocks(text);
        text = BlankRazorExpressions(text);
        text = BlankBareCSharpStatements(text);

        foreach (Match attribute in UserVisibleAttribute.Matches(text))
        {
            var value = attribute.Groups["value"].Value.Trim();

            if (LooksLikeProse.IsMatch(value))
            {
                yield return value;
            }
        }

        foreach (var node in TextNodes(text))
        {
            var candidate = node.Trim();

            if (candidate.Length == 0 || !LooksLikeProse.IsMatch(candidate))
            {
                continue;
            }

            if (BareCodeLines.Contains(candidate.Trim('{', '}', ' '), StringComparer.Ordinal))
            {
                continue;
            }

            yield return candidate;
        }

        static string Replace(string input, string pattern) =>
            Regex.Replace(input, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Blanks C# statements written bare inside the body of a Razor block, as in
    /// <c>@foreach (…) { var x = …; &lt;div&gt;…&lt;/div&gt; }</c>, where the statement carries no
    /// <c>@</c> of its own.
    ///
    /// The rule is: a line with no angle bracket on it that ends in a semicolon is a statement,
    /// not text. Razor requires markup to sit inside a tag, so a line with no tag cannot be
    /// rendered markup, and prose does not end in a semicolon. Narrow enough not to hide a real
    /// literal: <c>&lt;p&gt;Ready;&lt;/p&gt;</c> has angle brackets and is still caught.
    /// </summary>
    private static string BlankBareCSharpStatements(string text)
    {
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.EndsWith(';') && !trimmed.Contains('<') && !trimmed.Contains('>'))
            {
                lines[i] = string.Empty;
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>Blanks <c>@{ … }</c> blocks, which are C# rather than markup.</summary>
    private static string BlankCodeBlocks(string text)
    {
        var builder = new StringBuilder(text);

        for (var i = 0; i < builder.Length - 1; i++)
        {
            if (builder[i] != '@' || builder[i + 1] != '{')
            {
                continue;
            }

            var end = SkipBalanced(builder.ToString(), i + 1, '{', '}');
            Blank(builder, i, end);
            i = end;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Blanks every Razor transition — <c>@expression</c>, <c>@(…)</c> and the control-flow
    /// keywords with their condition — so that only literal markup text is left behind.
    /// </summary>
    private static string BlankRazorExpressions(string text)
    {
        var keywords = new[] { "if", "foreach", "for", "while", "switch", "do", "try", "catch", "finally", "lock", "else", "await" };
        var builder = new StringBuilder(text);
        var source = text;

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '@')
            {
                continue;
            }

            if (i + 1 < source.Length && source[i + 1] == '@')
            {
                Blank(builder, i, i + 1);
                i++;
                continue;
            }

            var j = i + 1;

            if (j < source.Length && source[j] == '(')
            {
                var end = SkipBalanced(source, j, '(', ')');
                Blank(builder, i, end);
                i = end;
                continue;
            }

            var word = ReadIdentifier(source, j);

            if (word.Length > 0 && keywords.Contains(word, StringComparer.Ordinal))
            {
                j += word.Length;

                while (j < source.Length && char.IsWhiteSpace(source[j]))
                {
                    j++;
                }

                if (j < source.Length && source[j] == '(')
                {
                    j = SkipBalanced(source, j, '(', ')');
                }
                else
                {
                    j--;
                }

                Blank(builder, i, j);
                i = j;
                continue;
            }

            // A member chain: identifier, then any run of calls, indexers and further members.
            var scanned = i;

            while (j < source.Length)
            {
                var identifier = ReadIdentifier(source, j);

                if (identifier.Length == 0)
                {
                    break;
                }

                j += identifier.Length;
                scanned = j - 1;

                while (j < source.Length && source[j] is '(' or '[')
                {
                    j = SkipBalanced(source, j, source[j], source[j] == '(' ? ')' : ']') + 1;
                    scanned = j - 1;
                }

                if (j < source.Length && source[j] == '.' && j + 1 < source.Length && IsIdentifierStart(source[j + 1]))
                {
                    j++;
                    continue;
                }

                break;
            }

            if (scanned > i)
            {
                Blank(builder, i, scanned);
                i = scanned;
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> TextNodes(string markup)
    {
        var depth = 0;
        var current = new StringBuilder();

        foreach (var character in markup)
        {
            if (character == '<')
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                depth++;
                continue;
            }

            if (character == '>')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0)
            {
                current.Append(character == '\n' ? '\n' : character);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    // ---- C# ----------------------------------------------------------------------------

    internal static IEnumerable<string> LiteralsPassedTo(string csharp, (string Name, int VisibleArgument) sink)
    {
        foreach (Match call in Regex.Matches(csharp, $@"\b{Regex.Escape(sink.Name)}\s*\("))
        {
            var open = call.Index + call.Length - 1;
            var close = SkipBalanced(csharp, open, '(', ')');

            if (close <= open)
            {
                continue;
            }

            var arguments = SplitArguments(csharp[(open + 1)..close]);

            if (arguments.Count <= sink.VisibleArgument)
            {
                continue;
            }

            var argument = arguments[sink.VisibleArgument].Trim();

            // Strip a named-argument prefix such as description: "…".
            var colon = argument.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0 && argument[..colon].Trim().All(IsIdentifierPart))
            {
                argument = argument[(colon + 1)..].Trim();
            }

            if (IsStringLiteral(argument))
            {
                yield return argument.Trim('@', '$', '"');
            }
        }
    }

    private static bool IsStringLiteral(string argument) =>
        argument.StartsWith('"') ||
        argument.StartsWith("@\"", StringComparison.Ordinal) ||
        argument.StartsWith("$\"", StringComparison.Ordinal) ||
        argument.StartsWith("\"\"\"", StringComparison.Ordinal);

    private static List<string> SplitArguments(string arguments)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        var inChar = false;

        for (var i = 0; i < arguments.Length; i++)
        {
            var character = arguments[i];

            if (inString)
            {
                current.Append(character);

                if (character == '\\')
                {
                    if (i + 1 < arguments.Length)
                    {
                        current.Append(arguments[++i]);
                    }
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                current.Append(character);

                if (character == '\\' && i + 1 < arguments.Length)
                {
                    current.Append(arguments[++i]);
                }
                else if (character == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '\'':
                    inChar = true;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                default:
                    break;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    // ---- shared ------------------------------------------------------------------------

    /// <summary>
    /// Returns the index of the closer matching the opener at <paramref name="start"/>, ignoring
    /// anything inside a string or character literal.
    /// </summary>
    private static int SkipBalanced(string text, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        var inChar = false;

        for (var i = start; i < text.Length; i++)
        {
            var character = text[i];

            if (inString)
            {
                if (character == '\\')
                {
                    i++;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (character == '\\')
                {
                    i++;
                }
                else if (character == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character == '\'')
            {
                inChar = true;
                continue;
            }

            if (character == open)
            {
                depth++;
            }
            else if (character == close)
            {
                depth--;

                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return text.Length - 1;
    }

    private static void Blank(StringBuilder builder, int from, int to)
    {
        for (var i = from; i <= to && i < builder.Length; i++)
        {
            if (builder[i] != '\n')
            {
                builder[i] = ' ';
            }
        }
    }

    private static string ReadIdentifier(string text, int start)
    {
        if (start >= text.Length || !IsIdentifierStart(text[start]))
        {
            return string.Empty;
        }

        var end = start;

        while (end < text.Length && IsIdentifierPart(text[end]))
        {
            end++;
        }

        return text[start..end];
    }

    private static bool IsIdentifierStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static string RepositoryRoot { get; } = LocalisationResourceTests.RepositoryRoot;
}
