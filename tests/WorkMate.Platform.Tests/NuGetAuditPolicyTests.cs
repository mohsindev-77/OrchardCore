using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// The NuGet audit is on, audits everything, and suppresses exactly the advisories ADR-0013 names.
/// </summary>
/// <remarks>
/// <b>What this replaces.</b> Session A2 proved by hand that a different, unsuppressed advisory
/// still fails the build: a vulnerable package was referenced temporarily, the restore failed, the
/// reference was removed. That proof was real and it evaporated the moment it was finished — it is
/// not in the repository, it is not in CI, and nothing would notice if somebody widened the
/// suppressions tomorrow.
///
/// <b>Why not keep doing it for real on every build.</b> The live version downloads a package with
/// a known vulnerability on every run, which is slow, needs the network, and puts a deliberately
/// vulnerable dependency into the restore graph of a product whose own build is the thing being
/// trusted. <c>tools/verify-nuget-audit.sh</c> keeps that version, for the day the ADR is reviewed
/// and somebody wants the end-to-end answer rather than the argument.
///
/// <b>What this asserts instead.</b> The two things that could actually make the audit stop
/// working, both of which are one-line edits:
/// <list type="bullet">
/// <item>the audit being turned off, turned down, or narrowed to direct dependencies — which would
/// hide the ImageSharp advisories entirely, since they are transitive;</item>
/// <item>a suppression being added that no ADR accounts for — which is how "suppressed by id" rots
/// into "suppressed by habit".</item>
/// </list>
///
/// A suppression is only legitimate if the advisory it names is written down somewhere a reviewer
/// will read, so each one must appear in an ADR. That is the rule this enforces, and it is why
/// adding a sixth line to <c>Directory.Build.props</c> fails until somebody has explained it.
/// </remarks>
public sealed partial class NuGetAuditPolicyTests
{
    [Theory]
    [InlineData("NuGetAudit", "true")]
    [InlineData("NuGetAuditMode", "all")]
    [InlineData("NuGetAuditLevel", "low")]
    public void TheAuditIsOnAndAtFullBreadth(string property, string expected)
    {
        var value = PropertyIn(BuildProps, property);

        value.Should().Be(
            expected,
            "<{0}> must be stated in Directory.Build.props. An SDK default is not a control: it "
            + "changes when the SDK changes, and the direction it changes in is 'audit less'. "
            + "NuGetAuditMode in particular decides whether transitive packages are audited at all, "
            + "and every advisory this product currently carries is against a transitive package.",
            property);
    }

    /// <summary>
    /// Every suppressed advisory is one an ADR names, and no advisory is suppressed twice.
    /// </summary>
    [Fact]
    public void EverySuppressedAdvisoryIsRecordedInAnAdr()
    {
        var suppressed = SuppressedAdvisories();

        suppressed.Should().NotBeEmpty(
            "this test is about the suppressions; if there are none left, delete it along with "
            + "the last one and say so in the ADR");

        suppressed.Should().OnlyHaveUniqueItems(
            "a duplicate suppression is a line nobody read before adding");

        var decisions = string.Concat(
            Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "docs", "adr"), "*.md")
                .Select(File.ReadAllText));

        foreach (var advisory in suppressed)
        {
            decisions.Should().Contain(
                advisory,
                "'{0}' is suppressed in Directory.Build.props, so some ADR must say which package "
                + "it is against, what this product's exposure to it is, why it cannot be fixed "
                + "now, and when the decision is revisited. A suppression nobody wrote down is a "
                + "vulnerability nobody is tracking.",
                advisory);
        }
    }

    /// <summary>
    /// The suppressions are by advisory id, never by package or by severity.
    /// </summary>
    /// <remarks>
    /// The distinction the whole arrangement rests on. Suppressing a package silences every
    /// advisory it will ever have, including the one published next week; suppressing an id
    /// silences exactly one known thing. <c>NoWarn</c> on the audit's warning codes would do the
    /// same damage from a different direction, so that is refused too.
    /// </remarks>
    [Fact]
    public void NothingIsSuppressedWholesale()
    {
        foreach (var advisory in SuppressedAdvisories())
        {
            advisory.Should().MatchRegex(
                @"^https://github\.com/advisories/GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}$",
                "a suppression must name one advisory. Anything else — a package, a pattern — "
                + "silences advisories that have not been published yet.");
        }

        // NU1901-NU1904 are the audit's own warnings. Silencing them is suppressing the audit
        // itself, which no amount of per-advisory care would make up for.
        var noWarn = PropertyIn(BuildProps, "NoWarn") ?? string.Empty;

        noWarn.Should().NotMatchRegex(
            "NU190[0-9]",
            "the audit's warnings must not be in NoWarn: that turns the whole audit off while "
            + "leaving every reassuring setting above it in place");
    }

    private static IReadOnlyList<string> SuppressedAdvisories() =>
        [.. SuppressElement().Matches(BuildProps).Select(match => match.Groups[1].Value)];

    private static string? PropertyIn(string xml, string property)
    {
        var match = Regex.Match(
            xml, $"<{Regex.Escape(property)}>([^<]*)</{Regex.Escape(property)}>", RegexOptions.None, TimeSpan.FromSeconds(2));

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static readonly string BuildProps =
        File.ReadAllText(Path.Combine(RepositoryRoot, "Directory.Build.props"));

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

    [GeneratedRegex("""<NuGetAuditSuppress\s+Include="([^"]+)"\s*/>""")]
    private static partial Regex SuppressElement();
}
