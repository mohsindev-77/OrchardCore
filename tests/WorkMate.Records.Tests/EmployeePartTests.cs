using System.Reflection;
using FluentAssertions;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// What the employee record's fixed core holds, and — more importantly — what it must never hold.
/// </summary>
public sealed class EmployeePartTests
{
    private static IReadOnlyList<PropertyInfo> StoredProperties =>
        [.. typeof(EmployeePart)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.DeclaringType == typeof(EmployeePart))];

    /// <summary>
    /// The one rule that follows from the model: the employee record holds no placement.
    /// </summary>
    /// <remarks>
    /// Architecture section 2. Every placement is a dated <c>EmployeeAssignment</c> row, because an
    /// employee sits on several axes at once and a single department field would collapse them into
    /// one — and because a field has no dates, so a mid-month transfer would overwrite where
    /// somebody sat rather than record that they moved, and "where were they last March" would
    /// start quietly returning today's answer.
    ///
    /// Written as a reflection test over the whole type rather than as an assertion about three
    /// field names, because the way this decision gets undone is not somebody adding
    /// <c>Department</c> — nobody would — it is somebody adding <c>PrimaryUnitId</c> or
    /// <c>HomeCostCentre</c> in good faith to make one screen simpler.
    /// </remarks>
    [Fact]
    public void TheEmployeeRecordHoldsNoOrganisationalPlacementField()
    {
        var placementish = StoredProperties
            .Where(property => EmployeeFieldNames.IsForbiddenOnSection(property.Name)
                && !EmployeeFieldNames.IsReserved(property.Name))
            .Select(property => property.Name)
            .ToList();

        placementish.Should().BeEmpty(
            "placement is an assignment row in the dimension engine, not a field on the employee — "
            + "see IEmployeeAssignmentService and architecture section 2");
    }

    /// <summary>
    /// The reserved-name set and the part agree, exactly.
    /// </summary>
    /// <remarks>
    /// The set is declared rather than generated from the type on purpose: generating it would make
    /// any field somebody adds automatically "reserved and therefore fine", which is the opposite of
    /// a guard. Declaring it means adding a core field is a deliberate act in two places, and this
    /// test is what makes the second place compulsory.
    /// </remarks>
    [Fact]
    public void TheReservedNamesAreExactlyTheFieldsTheCoreHolds()
    {
        var stored = StoredProperties.Select(property => property.Name).ToList();

        stored.Should().BeEquivalentTo(
            EmployeeFieldNames.Reserved,
            "EmployeeFieldNames.Reserved is what every guard on the fixed core is written against");
    }

    [Fact]
    public void TheCoreHoldsEveryFieldTheSpecificationNames() =>
        StoredProperties.Select(property => property.Name).Should().Contain(
        [
            nameof(EmployeePart.EmployeeCode),
            nameof(EmployeePart.NameEn),
            nameof(EmployeePart.NameAr),
            nameof(EmployeePart.DateOfBirth),
            nameof(EmployeePart.NationalityCode),
            nameof(EmployeePart.Gender),
            nameof(EmployeePart.JoinDate),
            nameof(EmployeePart.Status),
            nameof(EmployeePart.LineManagerEmployeeId),
            nameof(EmployeePart.PhotoPath),
        ],
        "specification section 5 names each of these");

    /// <summary>
    /// A leaver is still employed on their last day, and not on the day after it.
    /// </summary>
    /// <remarks>
    /// The off-by-one this platform most has to get right. Placements and headships are closed on
    /// the last day <em>inclusive</em>, while "had they left by this date" is asked of the day
    /// after, so the part stores the first day of ex-employment and converts back. Both readings
    /// are asserted here because the whole point of keeping them apart is that they differ by a day
    /// on exactly the day somebody leaves.
    /// </remarks>
    [Fact]
    public void AnEmployeeIsStillEmployedOnTheirLastDay()
    {
        var part = new EmployeePart
        {
            Status = EmploymentStatus.Exited,
            StatusEffectiveFrom = new DateOnly(2026, 7, 1),
        };

        part.ExitedOn.Should().Be(new DateOnly(2026, 6, 30), "the last day of service is the day before");
        part.HasLeftBy(new DateOnly(2026, 6, 30)).Should().BeFalse("they worked that day");
        part.HasLeftBy(new DateOnly(2026, 7, 1)).Should().BeTrue();
    }

    /// <summary>
    /// A question about the past is not answered by what is true today.
    /// </summary>
    /// <remarks>
    /// An employee who left in June was not an ex-employee in March, and an approval or a payroll
    /// run re-resolving March must not be told otherwise.
    /// </remarks>
    [Fact]
    public void SomebodyWhoLeftInJuneHadNotLeftInMarch()
    {
        var part = new EmployeePart
        {
            Status = EmploymentStatus.Exited,
            StatusEffectiveFrom = new DateOnly(2026, 7, 1),
        };

        part.HasLeftBy(new DateOnly(2026, 3, 15)).Should().BeFalse();
    }

    [Fact]
    public void SomebodyWhoHasNotLeftHasNoExitDate() =>
        new EmployeePart { Status = EmploymentStatus.Active }.ExitedOn.Should().BeNull();

    /// <summary>
    /// A new part is prospective and unspecified, never a guess.
    /// </summary>
    /// <remarks>
    /// Gender in particular: an import that did not carry it must be able to say so rather than
    /// assert something nobody told it, because the entitlements that depend on it are ones a wrong
    /// answer silently grants or withholds.
    /// </remarks>
    [Fact]
    public void ANewRecordAssertsNothingItWasNotTold()
    {
        var part = new EmployeePart();

        part.Status.Should().Be(EmploymentStatus.Prospective);
        part.Gender.Should().Be(Gender.Unspecified);
        part.NationalityCode.Should().BeEmpty();
        part.DateOfBirth.Should().BeNull();
    }
}
