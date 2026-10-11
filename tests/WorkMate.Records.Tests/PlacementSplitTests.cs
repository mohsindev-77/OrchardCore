using FluentAssertions;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// Every rule the change-placement screen applies to a split before anything is written.
/// </summary>
/// <remarks>
/// Unit tests rather than tenant tests, which is the whole reason <see cref="PlacementSplit"/> is a
/// pure function with no session, no localiser and no clock in it. The rules are about the shape of
/// a set of rows, and a set of rows does not need a database to be wrong.
///
/// <b>What these do not test.</b> Whether a unit may hold employees at all is the engine's rule,
/// enforced by <c>IDimensionValidator.ValidateAssignmentAsync</c> and covered by
/// <c>EmployeeAttachmentRulesTenantTests</c>; the screen surfaces what that says rather than
/// restating it. These cover the two things a validator working on a resolved split cannot do —
/// say which row is wrong, and catch an empty row before it reaches a service that would report it
/// as an unknown record id — plus the one rule that is the screen's own, which is the refusal to
/// backdate behind a change already recorded.
/// </remarks>
public sealed class PlacementSplitTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private static PlacementSplitRow Row(string unit, decimal allocation, bool isMain) =>
        new(unit, allocation, isMain);

    private static IReadOnlyList<PlacementSplitRule> RulesFor(
        IReadOnlyList<PlacementSplitRow> rows,
        DateOnly? effectiveFrom = null,
        DateOnly? latestChange = null) =>
        [.. PlacementSplit.Validate(rows, effectiveFrom ?? March, latestChange).Select(violation => violation.Rule)];

    // ---- the shapes that are correct ---------------------------------------------------

    [Fact]
    public void OneUnitAtAHundredIsValid() =>
        RulesFor([Row("civil", 100m, isMain: true)]).Should().BeEmpty();

    /// <summary>The matrix: a home department and a site team, 60/40, one of them main.</summary>
    [Fact]
    public void ASixtyFortySplitWithOneMainIsValid() =>
        RulesFor([Row("civil", 60m, isMain: true), Row("site-a", 40m, isMain: false)])
            .Should().BeEmpty();

    /// <summary>
    /// Three ways is as valid as two. The rule is about the total, not about the number of rows.
    /// </summary>
    [Fact]
    public void AThreeWaySplitTotallingAHundredIsValid() =>
        RulesFor(
        [
            Row("civil", 50m, isMain: true),
            Row("site-a", 30m, isMain: false),
            Row("site-b", 20m, isMain: false),
        ]).Should().BeEmpty();

    /// <summary>Fractions are allowed; a third each is a real arrangement.</summary>
    [Fact]
    public void FractionalSharesThatTotalAHundredAreValid() =>
        RulesFor(
        [
            Row("civil", 33.34m, isMain: true),
            Row("site-a", 33.33m, isMain: false),
            Row("site-b", 33.33m, isMain: false),
        ]).Should().BeEmpty();

    // ---- the total ---------------------------------------------------------------------

    [Theory]
    [InlineData(60, 30)]
    [InlineData(60, 50)]
    [InlineData(0.5, 0.5)]
    public void ASplitThatDoesNotTotalAHundredIsRefused(double first, double second) =>
        RulesFor(
        [
            Row("civil", (decimal)first, isMain: true),
            Row("site-a", (decimal)second, isMain: false),
        ]).Should().Contain(PlacementSplitRule.AllocationTotal);

    /// <summary>
    /// The total is reported, not only that it is wrong.
    /// </summary>
    /// <remarks>
    /// "These total 90%" is a sentence somebody can act on; "the allocations are wrong" sends them
    /// to add the numbers up themselves, on a form where the numbers are the thing they were
    /// already looking at.
    /// </remarks>
    [Fact]
    public void TheTotalIsCarriedOnTheViolationSoTheMessageCanNameIt()
    {
        var violation = PlacementSplit
            .Validate([Row("civil", 60m, isMain: true), Row("site-a", 30m, isMain: false)], March, null)
            .Single(violation => violation.Rule == PlacementSplitRule.AllocationTotal);

        violation.Arguments.Should().Equal([90m]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ARowAllocatedNothingOrLessIsRefused(int allocation)
    {
        var rules = RulesFor(
        [
            Row("civil", 100m, isMain: true),
            Row("site-a", allocation, isMain: false),
        ]);

        rules.Should().Contain(PlacementSplitRule.AllocationNotPositive);
    }

    /// <summary>The offending row is named, so the message lands under its own control.</summary>
    [Fact]
    public void TheRowWithNoAllocationIsIdentifiedByItsIndex()
    {
        var violation = PlacementSplit
            .Validate([Row("civil", 100m, isMain: true), Row("site-a", 0m, isMain: false)], March, null)
            .Single(violation => violation.Rule == PlacementSplitRule.AllocationNotPositive);

        violation.RowIndex.Should().Be(1);
    }

    // ---- exactly one main --------------------------------------------------------------

    [Fact]
    public void ASplitWithNoMainRowIsRefused() =>
        RulesFor([Row("civil", 60m, isMain: false), Row("site-a", 40m, isMain: false)])
            .Should().Contain(PlacementSplitRule.MainCount);

    [Fact]
    public void ASplitWithTwoMainRowsIsRefused() =>
        RulesFor([Row("civil", 60m, isMain: true), Row("site-a", 40m, isMain: true)])
            .Should().Contain(PlacementSplitRule.MainCount);

    [Fact]
    public void TheNumberMarkedMainIsCarriedSoTheMessageCanNameIt()
    {
        var violation = PlacementSplit
            .Validate([Row("civil", 60m, isMain: true), Row("site-a", 40m, isMain: true)], March, null)
            .Single(violation => violation.Rule == PlacementSplitRule.MainCount);

        violation.Arguments.Should().Equal([2]);
    }

    // ---- one unit, one row -------------------------------------------------------------

    /// <summary>
    /// The same unit twice is refused, and the second occurrence is the one reported.
    /// </summary>
    /// <remarks>
    /// Two rows for one unit is two answers to one question — what share of this person does Civil
    /// account for — and the engine would store both. Reporting the second rather than the first is
    /// deliberate: the first is the one the reader meant to keep.
    /// </remarks>
    [Fact]
    public void TheSameUnitOnTwoRowsIsRefused()
    {
        var violations = PlacementSplit.Validate(
            [Row("civil", 60m, isMain: true), Row("civil", 40m, isMain: false)], March, null);

        var duplicate = violations.Single(violation => violation.Rule == PlacementSplitRule.DuplicateUnit);

        duplicate.RowIndex.Should().Be(1);
    }

    [Fact]
    public void ARowWithNoUnitChosenIsRefused()
    {
        var violations = PlacementSplit.Validate(
            [Row("civil", 60m, isMain: true), new PlacementSplitRow(null, 40m, false)], March, null);

        violations.Should().Contain(violation =>
            violation.Rule == PlacementSplitRule.UnitMissing && violation.RowIndex == 1);
    }

    /// <summary>An empty string is an empty row, not a unit called "".</summary>
    [Fact]
    public void ARowWhoseUnitIsAnEmptyStringIsTreatedAsEmpty() =>
        RulesFor([new PlacementSplitRow(string.Empty, 100m, true)])
            .Should().Contain(PlacementSplitRule.UnitMissing);

    /// <summary>
    /// Two empty rows are two empty rows, not a duplicate.
    /// </summary>
    /// <remarks>
    /// The obvious implementation adds every unit to a set and reports the second occurrence of
    /// anything already in it, which makes the second blank row read as "this unit is already on
    /// another row" — a message about a unit that was never chosen.
    /// </remarks>
    [Fact]
    public void TwoEmptyRowsAreNotADuplicate()
    {
        var violations = PlacementSplit.Validate(
            [new PlacementSplitRow(null, 50m, true), new PlacementSplitRow(null, 50m, false)], March, null);

        violations.Should().NotContain(violation => violation.Rule == PlacementSplitRule.DuplicateUnit);
        violations.Count(violation => violation.Rule == PlacementSplitRule.UnitMissing).Should().Be(2);
    }

    // ---- the set as a whole ------------------------------------------------------------

    /// <summary>
    /// Removing every row is refused, and pointed at the operation that does mean it.
    /// </summary>
    /// <remarks>
    /// An empty split is not "this person is placed nowhere": ending a placement needs a last day,
    /// which this form does not ask for. Writing one anyway would leave the old rows closed the day
    /// before the effective date with nothing opening after them — the same outcome as ending, by a
    /// route that never said so.
    /// </remarks>
    [Fact]
    public void RemovingEveryRowIsRefused() =>
        RulesFor([]).Should().Contain(PlacementSplitRule.NoRows);

    /// <summary>
    /// An empty split is reported once, not three times.
    /// </summary>
    /// <remarks>
    /// Zero rows total zero and have no main row, so a validator that kept going would stack a
    /// total and a main-count message on top of the only one that helps.
    /// </remarks>
    [Fact]
    public void AnEmptySplitReportsOnlyThat() =>
        RulesFor([]).Should().Equal([PlacementSplitRule.NoRows]);

    // ---- the effective date ------------------------------------------------------------

    [Fact]
    public void ASplitWithNoEffectiveDateIsRefused() =>
        PlacementSplit.Validate([Row("civil", 100m, true)], null, null)
            .Select(violation => violation.Rule)
            .Should().Contain(PlacementSplitRule.EffectiveDateMissing);

    /// <summary>
    /// A date earlier than a change already recorded is refused rather than quietly overwriting it.
    /// </summary>
    /// <remarks>
    /// <c>ReallocateAsync</c> states the whole picture from its effective date onwards: rows that
    /// start on or after it are replaced outright. So a split dated behind a later change would
    /// destroy that change without going through the one mechanism that is audited and shows a dry
    /// run — the "overwrite" behaviour ADR-0005's addendum examined and rejected for moves, reached
    /// through a different door.
    /// </remarks>
    [Fact]
    public void ADateBeforeTheLatestRecordedChangeIsRefused() =>
        RulesFor(
            [Row("civil", 100m, true)],
            effectiveFrom: new DateOnly(2026, 1, 1),
            latestChange: March)
            .Should().Contain(PlacementSplitRule.EffectiveDateBeforeLatestChange);

    /// <summary>
    /// The same date as the latest change is allowed: it restates that day, it does not slip behind
    /// it.
    /// </summary>
    /// <remarks>
    /// The boundary is the case this rule exists to get right. Refusing it would make correcting a
    /// split entered this morning impossible without first inventing a later date, which is the
    /// opposite of what an effective-dated engine is for.
    /// </remarks>
    [Fact]
    public void TheSameDateAsTheLatestRecordedChangeIsAllowed() =>
        RulesFor([Row("civil", 100m, true)], effectiveFrom: March, latestChange: March)
            .Should().BeEmpty();

    [Fact]
    public void ADateAfterTheLatestRecordedChangeIsAllowed() =>
        RulesFor(
            [Row("civil", 100m, true)],
            effectiveFrom: March.AddDays(1),
            latestChange: March)
            .Should().BeEmpty();

    /// <summary>A structure with no history at all accepts any date, including a past one.</summary>
    [Fact]
    public void WithNoChangeOnRecordAnyDateIsAllowed() =>
        RulesFor([Row("civil", 100m, true)], effectiveFrom: new DateOnly(2020, 1, 1), latestChange: null)
            .Should().BeEmpty();

    // ---- several things wrong at once --------------------------------------------------

    /// <summary>
    /// Every broken rule is reported, not the first one.
    /// </summary>
    /// <remarks>
    /// A form that reports one problem per submit makes a reader with three of them press Save
    /// three times and read three different refusals, which is how somebody concludes the screen is
    /// fighting them.
    /// </remarks>
    [Fact]
    public void EveryBrokenRuleIsReportedTogether() =>
        RulesFor(
        [
            Row("civil", 60m, isMain: true),
            Row("civil", 10m, isMain: true),
        ]).Should().Contain(
        [
            PlacementSplitRule.DuplicateUnit,
            PlacementSplitRule.AllocationTotal,
            PlacementSplitRule.MainCount,
        ]);
}
