using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Catches a document or part property whose default value is a shared static instance, rather
/// than a fresh one of its own.
/// </summary>
/// <remarks>
/// Found in WorkMate.Dimensions prompt 3, the hard way: <c>DimensionTypeDocument.Name</c> and
/// <c>StructureDocument.Name</c> both defaulted to <c>BilingualText.Empty</c> — one shared
/// object — rather than a fresh <c>BilingualText</c> each. Orchard's content serialiser
/// deserialises a property that already holds a non-null value by writing into the instance it
/// finds there instead of replacing it, for the same reason a saved Sunday-to-Thursday working
/// week once came back as Sunday-to-Friday: <c>WorkMateSettings.WorkingDays</c> defaulted to the
/// shared <c>DefaultWorkingDays</c> array's contents and a list-typed property would have been
/// populated rather than replaced. <c>WorkMateSettings</c> escapes this because an array is
/// always replaced, never populated — that fix is explained on <c>WorkMateSettings.WorkingDays</c>
/// itself. A bare static reference-type field used as a settable property's default has no such
/// protection: every document built from that default is the exact same object, so writing one
/// document's loaded value into it is visible from every other document that shares it, forever,
/// until the process restarts. Confirmed directly with a throwaway diagnostic before the fix:
/// <c>ReferenceEquals(docA.Name, docB.Name)</c> was true for two unrelated, freshly created
/// documents read back from a real tenant. <c>DocumentIdentityTenantTests</c> in the integration
/// suite is the permanent regression test for the fixed behaviour.
///
/// This is therefore a rule for every module, not just this one: a settable auto-property
/// (<c>{ get; set; }</c> — an immutable record's <c>{ get; init; }</c> is constructed fresh every
/// time and is not at risk the same way) must never default to a bare static member access whose
/// type is a reference type. <c>string.Empty</c> is the one standing exception: strings are
/// immutable and nothing can "populate into" one, which is also why an empty collection backed by
/// <c>Array.Empty&lt;T&gt;()</c> — what a collection-expression default like <c>= []</c> compiles
/// to for an <c>IReadOnlyList&lt;T&gt;</c>-typed property — is safe for the same reason the working
/// week's array fix is: both are types a populate-aware deserialiser cannot write into in place.
///
/// The check is a heuristic text scan, not a parser, matching the house style
/// <see cref="UserVisibleStringTests"/> already uses and for the same reason: it has to run
/// without a shell or a compiled reference to modules that do not exist yet when this file is
/// written. <see cref="SharedStaticDocumentDefaultDetectorTests"/> pins what it catches and what
/// it deliberately lets through.
/// </remarks>
public sealed class SharedStaticDocumentDefaultTests
{
    /// <summary>
    /// Every project under <c>src</c>, with no exclusion for <c>WorkMate.Core</c> or
    /// <c>WorkMate.Web</c> the way <see cref="LocalisationResourceTests.Modules"/> has — this
    /// hazard is not a localisation concern. <c>BilingualText.Empty</c>, the field that caused
    /// ADR-0007, lived in <c>WorkMate.Core</c>, which every module depends on and which
    /// <see cref="LocalisationResourceTests.Modules"/> skips because it has no localiser. A guard
    /// that could not have caught the actual defect is not a guard.
    /// </summary>
    public static TheoryData<string> AllModules()
    {
        var data = new TheoryData<string>();

        foreach (var directory in Directory.GetDirectories(Path.Combine(LocalisationResourceTests.RepositoryRoot, "src")))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void NoSettablePropertyDefaultsToASharedStaticInstance(string moduleName)
    {
        var module = Path.Combine(LocalisationResourceTests.RepositoryRoot, "src", moduleName);

        if (!Directory.Exists(module))
        {
            return;
        }

        var sources = EnumerateSources(module).ToList();
        var valueTypeNames = KnownValueTypes.Union(EnumNamesIn(sources), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        var offences = new List<string>();

        foreach (var source in sources)
        {
            foreach (var (type, property, expression) in SharedStaticDefaultsIn(File.ReadAllText(source), valueTypeNames))
            {
                offences.Add($"  {Path.GetRelativePath(LocalisationResourceTests.RepositoryRoot, source)}: " +
                    $"{type} {property} = {expression}");
            }
        }

        offences.Should().BeEmpty(
            "a settable property must default to a fresh instance, never a shared static one — " +
            "see the remarks on SharedStaticDocumentDefaultTests for why.{0}{1}",
            Environment.NewLine,
            string.Join(Environment.NewLine, offences));
    }

    private static readonly HashSet<string> KnownValueTypes = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "char", "string",
        "DateOnly", "DateTime", "DateTimeOffset", "TimeOnly", "TimeSpan", "Guid", "DayOfWeek",
    };

    private static readonly Regex EnumDeclaration = new(
        @"\benum\s+(?<name>\w+)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// A settable auto-property whose initialiser is a bare <c>Type.Member</c> access — not a
    /// call (<c>new(...)</c>, <c>Type.Method()</c>), not a collection expression, just a static
    /// field or property reference, which is exactly the shape that hands every instance the same
    /// object.
    /// </summary>
    private static readonly Regex PropertyWithDottedDefault = new(
        @"(?:public|internal)\s+(?<type>[A-Za-z_]\w*)\s+(?<prop>[A-Za-z_]\w*)\s*\{\s*get;\s*set;\s*\}\s*=\s*(?<expr>[A-Za-z_]\w*\.[A-Za-z_]\w*)\s*;",
        RegexOptions.Compiled);

    internal static IEnumerable<(string Type, string Property, string Expression)> SharedStaticDefaultsIn(
        string source, IReadOnlySet<string> valueTypeNames)
    {
        foreach (Match match in PropertyWithDottedDefault.Matches(source))
        {
            var type = match.Groups["type"].Value;

            if (valueTypeNames.Contains(type))
            {
                continue;
            }

            yield return (type, match.Groups["prop"].Value, match.Groups["expr"].Value);
        }
    }

    private static IEnumerable<string> EnumNamesIn(IEnumerable<string> sources)
    {
        foreach (var source in sources)
        {
            foreach (Match match in EnumDeclaration.Matches(File.ReadAllText(source)))
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    private static IEnumerable<string> EnumerateSources(string moduleDirectory) =>
        Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}

/// <summary>
/// Pins what <see cref="SharedStaticDocumentDefaultTests"/> catches and what it deliberately lets
/// through, the same way <c>UserVisibleStringDetectorTests</c> pins the localisation scanner.
/// </summary>
public sealed class SharedStaticDocumentDefaultDetectorTests
{
    [Fact]
    public void ABareStaticReferenceTypeDefaultIsCaught()
    {
        const string Source = "public BilingualText Name { get; set; } = BilingualText.Empty;";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>())
            .Should().ContainSingle(x => x.Type == "BilingualText" && x.Property == "Name");
    }

    [Fact]
    public void StringEmptyIsNotCaught()
    {
        const string Source = "public string Code { get; set; } = string.Empty;";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>(["string"]))
            .Should().BeEmpty();
    }

    [Fact]
    public void AnEnumConstantIsNotCaught()
    {
        const string Source = "public CalendarPreference Calendar { get; set; } = CalendarPreference.Gregorian;";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>(["CalendarPreference"]))
            .Should().BeEmpty();
    }

    [Fact]
    public void AFreshInstanceIsNotCaught()
    {
        const string Source = "public BilingualText Name { get; set; } = new(string.Empty, string.Empty);";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>())
            .Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyCollectionExpressionIsNotCaught()
    {
        const string Source = "public IReadOnlyList<StructureLevel> Levels { get; set; } = [];";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>())
            .Should().BeEmpty();
    }

    [Fact]
    public void AnInitOnlyPropertyIsNotCaught()
    {
        // Constructed fresh by the record's primary constructor every time; not at risk.
        const string Source = "public BilingualText Label { get; init; } = BilingualText.Empty;";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>())
            .Should().BeEmpty();
    }

    [Fact]
    public void ABareConstantWithNoDotIsNotCaught()
    {
        const string Source = "public DayOfWeek WeekStartsOn { get; set; } = DefaultWeekStartsOn;";

        SharedStaticDocumentDefaultTests.SharedStaticDefaultsIn(Source, new HashSet<string>())
            .Should().BeEmpty();
    }
}
