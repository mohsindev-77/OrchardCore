using System.Globalization;
using FluentAssertions;
using WorkMate.Dimensions.Controllers;
using WorkMate.Dimensions.ViewModels;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// How the value page's <b>list</b> view orders its rows — decision D25's "sortable by column".
/// </summary>
/// <remarks>
/// <b>Sorted on the server, from the query string</b>, so a sorted list is a URL somebody can send
/// and the headings work with no JavaScript. The rules are here rather than in a tenant test
/// because they are rules about a list, and each one is a sentence a reviewer can check.
/// </remarks>
public sealed class UnitsListSortingTests
{
    /// <summary>
    /// No column named means tree order — the order the other two views show.
    /// </summary>
    /// <remarks>
    /// The alternative, alphabetising by default, would mean a reader who switched from Tree to
    /// List saw the same units in a different order for no reason they asked for. The list is the
    /// same answer rendered flat until somebody says otherwise.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void WithoutAColumnTheRowsStayInTreeOrder(string? sort)
    {
        var rows = Rows();

        UnitsListSorting(rows, sort, descending: false)
            .Select(row => row.Code)
            .Should().Equal(rows.Select(row => row.Code));
    }

    [Fact]
    public void SortingByCodeIsOrdinalAndReversible()
    {
        UnitsListSorting(Rows(), "code", descending: false)
            .Select(row => row.Code)
            .Should().Equal(["a-ops", "m-fleet", "z-credit"]);

        UnitsListSorting(Rows(), "code", descending: true)
            .Select(row => row.Code)
            .Should().Equal(["z-credit", "m-fleet", "a-ops"]);
    }

    /// <summary>
    /// Names sort as words a person reads: case-insensitively, in the reader's culture.
    /// </summary>
    /// <remarks>
    /// Ordinal would put every capital before every lower-case letter, so <em>fleet</em> would
    /// follow <em>Zulu</em> — which is right for a code and wrong for a name.
    /// </remarks>
    [Fact]
    public void SortingByNameIgnoresCase()
    {
        UnitsListSorting(Rows(), "name", descending: false)
            .Select(row => row.NameEn)
            .Should().Equal(["credit", "Fleet", "Operations"]);
    }

    [Fact]
    public void SortingByParentUsesTheColumnTheListShows() =>
        UnitsListSorting(Rows(), "parent", descending: false)
            .Select(row => row.ParentName)
            .Should().Equal(["Corporate", "Head Office", "Lahore Main Branch"]);

    [Fact]
    public void SortingByDimensionOrdersTheDimensionNames() =>
        UnitsListSorting(Rows(), "dimension", descending: false)
            .Select(row => row.TypeName)
            .Should().Equal(["Department", "Section", "Team"]);

    [Fact]
    public void SortingByEmployeesIsNumericRatherThanAlphabetical() =>
        UnitsListSorting(Rows(), "employees", descending: true)
            .Select(row => row.EmployeeCountIncludingSubUnits)
            .Should().Equal([12, 9, 2]);

    /// <summary>Inactive units sort together, and the direction decides which end.</summary>
    [Fact]
    public void SortingByStatusGroupsTheInactiveOnes()
    {
        UnitsListSorting(Rows(), "status", descending: false)
            .Select(row => row.IsEffective)
            .Should().Equal([false, true, true]);

        UnitsListSorting(Rows(), "status", descending: true)
            .Select(row => row.IsEffective)
            .Should().Equal([true, true, false]);
    }

    /// <summary>
    /// Ties break on the code, so the order is total.
    /// </summary>
    /// <remarks>
    /// Without it, two units of the same dimension would come back in whatever order the subtree
    /// read happened to produce, and a reader pressing a heading twice could see them swap for no
    /// reason. A code is unique within a tenant, so it always settles a tie.
    /// </remarks>
    [Fact]
    public void TiesBreakOnTheCode()
    {
        var rows = new List<DimensionRecordTreeRowViewModel>
        {
            Row("z-one", "Alpha", "Department", "Head Office", 1, true),
            Row("a-two", "Beta", "Department", "Head Office", 1, true),
        };

        UnitsListSorting(rows, "dimension", descending: false)
            .Select(row => row.Code)
            .Should().Equal(["a-two", "z-one"]);
    }

    // ---- harness ---------------------------------------------------------------------------

    private static IReadOnlyList<DimensionRecordTreeRowViewModel> UnitsListSorting(
        IReadOnlyList<DimensionRecordTreeRowViewModel> rows, string? sort, bool descending)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        try
        {
            return DimensionRecordsAdminController.SortedForList(rows, sort, descending);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>Three rows in tree order, deliberately not sorted by anything.</summary>
    private static List<DimensionRecordTreeRowViewModel> Rows() =>
    [
        Row("z-credit", "credit", "Section", "Lahore Main Branch", 2, true),
        Row("a-ops", "Operations", "Department", "Head Office", 12, false),
        Row("m-fleet", "Fleet", "Team", "Corporate", 9, true),
    ];

    private static DimensionRecordTreeRowViewModel Row(
        string code, string name, string dimension, string parent, int employees, bool effective) =>
        new()
        {
            RecordId = code,
            Code = code,
            NameEn = name,
            TypeName = dimension,
            ParentName = parent,
            EmployeeCountIncludingSubUnits = employees,
            IsEffective = effective,
        };
}
