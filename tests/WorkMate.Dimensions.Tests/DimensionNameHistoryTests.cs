using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// Corrective and substantive renames, which architecture section 5 requires to behave
/// differently.
/// </summary>
/// <remarks>
/// The distinction is a product requirement, not a nicety: "A typo fix should apply
/// retrospectively; a genuine renaming should leave last year's reports showing the old name."
/// Getting it wrong in either direction corrupts history quietly — a correction that forks the
/// timeline leaves last year's report showing a name that was never right, and a rename that
/// overwrites leaves it showing a name the unit did not have then.
/// </remarks>
public sealed class DimensionNameHistoryTests
{
    private static BilingualText Name(string en) => new(en, $"{en}-ar");

    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly Renamed = new(2025, 7, 1);

    /// <summary>
    /// A unit opened in 2024 as "Admin", renamed to "Corporate Services" on 1 July 2025.
    /// </summary>
    private static IReadOnlyList<DimensionNamePeriod> TwoPeriods()
    {
        var history = DimensionNameHistory.Start(Name("Admin"), Opened);

        return DimensionNameHistory.Rename(history, Name("Corporate Services"), Renamed, out _)!;
    }

    [Fact]
    public void ANewRecordStartsWithOneOpenEndedName()
    {
        var history = DimensionNameHistory.Start(Name("Admin"), Opened);

        history.Should().ContainSingle();
        history[0].Range.From.Should().Be(Opened);
        history[0].Range.To.Should().BeNull();
        DimensionNameHistory.IsWellFormed(history).Should().BeTrue();
    }

    // ---- substantive rename ------------------------------------------------------------

    [Fact]
    public void ASubstantiveRenameClosesTheOldNameTheDayBefore()
    {
        var history = TwoPeriods();

        history.Should().HaveCount(2);
        history[0].Name.En.Should().Be("Admin");
        history[0].Range.To.Should().Be(Renamed.AddDays(-1), "the end is inclusive");
        history[1].Name.En.Should().Be("Corporate Services");
        history[1].Range.From.Should().Be(Renamed);
        history[1].Range.To.Should().BeNull();

        DimensionNameHistory.IsWellFormed(history).Should().BeTrue();
    }

    [Fact]
    public void AfterASubstantiveRenameHistoryStillShowsTheOldName()
    {
        var document = new DimensionNameDocument { Periods = TwoPeriods() };

        document.NameOn(Renamed.AddDays(-1))!.En.Should().Be(
            "Admin",
            "last year's report must keep showing what the unit was called then");

        document.NameOn(Renamed)!.En.Should().Be("Corporate Services");
        document.CurrentName!.En.Should().Be("Corporate Services");
    }

    [Fact]
    public void ARenameOnTheDayAPeriodStartsReplacesItRatherThanLeavingAnEmptyOne()
    {
        // Renaming effective the very day the current name began leaves no day on which the old
        // name applied. A zero-length period would be a row that can never be resolved.
        var history = DimensionNameHistory.Start(Name("Admin"), Opened);

        var renamed = DimensionNameHistory.Rename(history, Name("Administration"), Opened, out var failure);

        failure.Should().Be(SubstantiveRenameFailure.None);
        renamed.Should().ContainSingle();
        renamed![0].Name.En.Should().Be("Administration");
        renamed[0].Range.From.Should().Be(Opened);
        DimensionNameHistory.IsWellFormed(renamed).Should().BeTrue();
    }

    [Fact]
    public void ARenameInsideAClosedPeriodDoesNotSwallowThePeriodsAfterIt()
    {
        // The awkward case. Backdating a rename into a period that has already been superseded
        // must split that period and leave everything after it untouched.
        var history = TwoPeriods();
        var midwayThroughTheFirst = new DateOnly(2024, 6, 1);

        var renamed = DimensionNameHistory.Rename(
            history,
            Name("Admin Services"),
            midwayThroughTheFirst,
            out var failure);

        failure.Should().Be(SubstantiveRenameFailure.None);
        renamed.Should().HaveCount(3);

        renamed![0].Name.En.Should().Be("Admin");
        renamed[0].Range.To.Should().Be(midwayThroughTheFirst.AddDays(-1));

        renamed[1].Name.En.Should().Be("Admin Services");
        renamed[1].Range.From.Should().Be(midwayThroughTheFirst);
        renamed[1].Range.To.Should().Be(Renamed.AddDays(-1), "it runs up to the rename that follows it");

        renamed[2].Name.En.Should().Be("Corporate Services");
        renamed[2].Range.From.Should().Be(Renamed);
        renamed[2].Range.To.Should().BeNull();

        DimensionNameHistory.IsWellFormed(renamed).Should().BeTrue();
    }

    [Fact]
    public void ARenameBeforeTheRecordHadAnyNameIsRefused()
    {
        var renamed = DimensionNameHistory.Rename(
            TwoPeriods(),
            Name("Too early"),
            Opened.AddDays(-1),
            out var failure);

        renamed.Should().BeNull();
        failure.Should().Be(
            SubstantiveRenameFailure.BeforeTheHistoryStarts,
            "renaming from a date the record did not exist on is a dating error, not a rename");
    }

    [Fact]
    public void ARenameWithNoHistoryToExtendIsRefused()
    {
        var renamed = DimensionNameHistory.Rename([], Name("Anything"), Renamed, out var failure);

        renamed.Should().BeNull();
        failure.Should().Be(SubstantiveRenameFailure.NoHistory);
    }

    // ---- corrective rename -------------------------------------------------------------

    [Fact]
    public void ACorrectiveRenameChangesTheTextAndNoDates()
    {
        var history = DimensionNameHistory.Start(Name("Admn"), Opened);

        var corrected = DimensionNameHistory.Correct(history, Name("Admin"), new DateOnly(2024, 6, 1));

        corrected.Should().ContainSingle();
        corrected![0].Name.En.Should().Be("Admin");
        corrected[0].Range.Should().Be(history[0].Range, "a correction is not a change of fact");
    }

    [Fact]
    public void ACorrectiveRenameAppliesRetrospectively()
    {
        var history = DimensionNameHistory.Start(Name("Admn"), Opened);
        var corrected = DimensionNameHistory.Correct(history, Name("Admin"), Opened)!;

        new DimensionNameDocument { Periods = corrected }
            .NameOn(new DateOnly(2024, 3, 1))!.En.Should().Be(
                "Admin",
                "a typo was never the unit's name, so last year's report should show the fix");
    }

    [Fact]
    public void ACorrectiveRenameCanTargetAClosedPeriod()
    {
        // The case review called out: a typo can sit in a closed period, and it is usually found
        // precisely because someone ran last year's report and did not recognise the name. A
        // correction that could only reach the current name would leave the error where it was
        // found.
        var history = TwoPeriods();
        var withinTheClosedPeriod = new DateOnly(2024, 6, 1);

        var corrected = DimensionNameHistory.Correct(
            history,
            Name("Administration"),
            withinTheClosedPeriod);

        corrected.Should().HaveCount(2);
        corrected![0].Name.En.Should().Be("Administration", "the closed period was the one corrected");
        corrected[0].Range.Should().Be(history[0].Range, "its dates are untouched");

        corrected[1].Name.En.Should().Be("Corporate Services", "the current name is not affected");
        corrected[1].Range.Should().Be(history[1].Range);

        DimensionNameHistory.IsWellFormed(corrected).Should().BeTrue();
    }

    [Fact]
    public void CorrectingTheCurrentNameLeavesTheHistoryAlone()
    {
        var history = TwoPeriods();

        var corrected = DimensionNameHistory.Correct(history, Name("Corporate Service"), Renamed)!;

        corrected[0].Name.En.Should().Be("Admin", "the earlier period was not the one corrected");
        corrected[1].Name.En.Should().Be("Corporate Service");
    }

    [Fact]
    public void ACorrectionTargetingADateWithNoNameIsRefused()
    {
        var corrected = DimensionNameHistory.Correct(TwoPeriods(), Name("Nothing"), Opened.AddDays(-1));

        corrected.Should().BeNull("there is no name on that date to correct");
    }

    // ---- the shape of a history --------------------------------------------------------

    [Fact]
    public void AnEmptyHistoryIsNotWellFormed() =>
        DimensionNameHistory.IsWellFormed([]).Should().BeFalse();

    [Fact]
    public void AHistoryWithAGapIsNotWellFormed()
    {
        // A gap means a date on which a report can find no name for a unit that certainly had
        // one.
        IReadOnlyList<DimensionNamePeriod> gapped =
        [
            new(Name("Admin"), new EffectiveRange(Opened, new DateOnly(2025, 6, 1))),
            new(Name("Corporate Services"), new EffectiveRange(Renamed, null)),
        ];

        DimensionNameHistory.IsWellFormed(gapped).Should().BeFalse();
    }

    [Fact]
    public void AHistoryWithAnOverlapIsNotWellFormed()
    {
        IReadOnlyList<DimensionNamePeriod> overlapping =
        [
            new(Name("Admin"), new EffectiveRange(Opened, Renamed)),
            new(Name("Corporate Services"), new EffectiveRange(Renamed, null)),
        ];

        DimensionNameHistory.IsWellFormed(overlapping).Should().BeFalse();
    }

    [Fact]
    public void AHistoryWhoseLastPeriodIsClosedIsNotWellFormed()
    {
        IReadOnlyList<DimensionNamePeriod> closed =
        [
            new(Name("Admin"), new EffectiveRange(Opened, Renamed.AddDays(-1))),
        ];

        DimensionNameHistory.IsWellFormed(closed).Should().BeFalse(
            "a record that still exists is still called something");
    }

    [Fact]
    public void RepeatedRenamesStayWellFormed()
    {
        var history = DimensionNameHistory.Start(Name("One"), Opened);

        foreach (var year in new[] { 2025, 2026, 2027 })
        {
            history = DimensionNameHistory.Rename(history, Name($"Name {year}"), new DateOnly(year, 4, 1), out _)!;
        }

        history.Should().HaveCount(4);
        DimensionNameHistory.IsWellFormed(history).Should().BeTrue();

        var document = new DimensionNameDocument { Periods = history };

        document.NameOn(new DateOnly(2025, 3, 31))!.En.Should().Be("One");
        document.NameOn(new DateOnly(2025, 4, 1))!.En.Should().Be("Name 2025");
        document.NameOn(new DateOnly(2026, 3, 31))!.En.Should().Be("Name 2025");
        document.NameOn(new DateOnly(2027, 4, 1))!.En.Should().Be("Name 2027");
    }

    [Fact]
    public void ADateBeforeTheHistoryResolvesToNoName() =>
        new DimensionNameDocument { Periods = TwoPeriods() }
            .NameOn(Opened.AddDays(-1)).Should().BeNull();
}
