using FluentAssertions;
using WorkMate.Core;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// The range arithmetic the dated closure index is built on.
/// </summary>
/// <remarks>
/// ADR-0005 makes a closure row's range the intersection of the link ranges along the path, so
/// every ancestor relationship in the product is the output of <see cref="EffectiveRange.Intersect"/>.
/// It is four lines of code holding up the whole index, which is a good reason to test it
/// exhaustively here rather than only through the structures it produces.
///
/// The end is inclusive throughout — the assumption the whole platform is built on, stated once
/// on <see cref="EffectiveRange"/> and pending Umair's confirmation.
/// </remarks>
public sealed class EffectiveRangeTests
{
    private static EffectiveRange Range(int fromDay, int? toDay = null) => new(
        new DateOnly(2026, 1, fromDay),
        toDay is null ? null : new DateOnly(2026, 1, toDay.Value));

    [Fact]
    public void TwoOverlappingRangesIntersectOnTheOverlap()
    {
        var intersection = Range(1, 20).Intersect(Range(10, 31));

        intersection.Should().Be(Range(10, 20), "the later start and the earlier end");
    }

    [Fact]
    public void TwoOpenEndedRangesIntersectFromTheLaterStartAndStayOpen()
    {
        var intersection = Range(10).Intersect(Range(1));

        intersection.Should().Be(Range(10));
        intersection!.Value.To.Should().BeNull("both are still running, so the overlap is too");
    }

    [Fact]
    public void AnOpenRangeIntersectedWithAClosedOneTakesTheClosedEnd() =>
        Range(1).Intersect(Range(5, 20)).Should().Be(Range(5, 20));

    [Fact]
    public void RangesThatTouchOnOneDayIntersectOnThatDay() =>
        // The end is inclusive, so a range ending on the 10th and one starting on the 10th do
        // overlap — by exactly one day. Getting this wrong by one is how a transfer lands in
        // both departments or neither.
        Range(1, 10).Intersect(Range(10, 20)).Should().Be(Range(10, 10));

    [Fact]
    public void ConsecutiveRangesDoNotIntersect() =>
        // A link closed on the 10th and its successor opened on the 11th: adjacent, never
        // overlapping. This is the shape every dated write in the engine produces.
        Range(1, 10).Intersect(Range(11, 20)).Should().BeNull();

    [Fact]
    public void DisjointRangesDoNotIntersect()
    {
        Range(1, 5).Intersect(Range(20, 25)).Should().BeNull();
        Range(20, 25).Intersect(Range(1, 5)).Should().BeNull("intersection is symmetrical");
    }

    [Fact]
    public void IntersectionIsSymmetrical() =>
        Range(1, 20).Intersect(Range(10, 31)).Should().Be(Range(10, 31).Intersect(Range(1, 20)));

    [Fact]
    public void ARangeIntersectedWithItselfIsItself() =>
        Range(5, 15).Intersect(Range(5, 15)).Should().Be(Range(5, 15));

    [Fact]
    public void AContainedRangeIsTheIntersection() =>
        Range(1, 31).Intersect(Range(10, 20)).Should().Be(Range(10, 20));

    [Fact]
    public void OverlapsAgreesWithIntersect()
    {
        Range(1, 10).Overlaps(Range(10, 20)).Should().BeTrue();
        Range(1, 10).Overlaps(Range(11, 20)).Should().BeFalse();
    }

    [Fact]
    public void ARangeContainsItsFirstAndLastDay()
    {
        var range = Range(10, 20);

        range.Contains(new DateOnly(2026, 1, 9)).Should().BeFalse();
        range.Contains(new DateOnly(2026, 1, 10)).Should().BeTrue();
        range.Contains(new DateOnly(2026, 1, 20)).Should().BeTrue();
        range.Contains(new DateOnly(2026, 1, 21)).Should().BeFalse();
    }

    [Fact]
    public void AnOpenRangeContainsEveryLaterDay() =>
        Range(10).Contains(new DateOnly(2099, 1, 1)).Should().BeTrue();

    [Fact]
    public void ARangeWhoseEndPrecedesItsStartIsEmpty()
    {
        new EffectiveRange(new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 10)).IsEmpty.Should().BeTrue();
        Range(10, 20).IsEmpty.Should().BeFalse();
        Range(10).IsEmpty.Should().BeFalse("an open range is never empty");
        Range(10, 10).IsEmpty.Should().BeFalse("a one-day range covers a day");
    }

    [Fact]
    public void EndingOnClosesAnOpenRange()
    {
        var closed = Range(10).EndingOn(new DateOnly(2026, 1, 20));

        closed.Should().Be(Range(10, 20));
    }

    [Fact]
    public void EndingOnKeepsTheStart() =>
        Range(10, 31).EndingOn(new DateOnly(2026, 1, 15)).From.Should().Be(new DateOnly(2026, 1, 10));

    [Fact]
    public void IntersectingThreeRangesGivesTheNarrowestWindow()
    {
        // The three-level case the closure actually computes: a section under a department
        // under a division, each joining at a different time.
        var section = Range(1);
        var toDepartment = Range(5);
        var departmentToDivision = Range(12, 25);

        var path = section.Intersect(toDepartment)!.Value.Intersect(departmentToDivision);

        path.Should().Be(Range(12, 25), "the relationship holds only while every link does");
    }
}
