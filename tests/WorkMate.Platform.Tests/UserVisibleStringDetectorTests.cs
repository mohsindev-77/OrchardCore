using FluentAssertions;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Tests for the detector behind <see cref="UserVisibleStringTests"/>.
///
/// A gate nobody has seen fail is a gate nobody should trust, and this one reads Razor with
/// rules rather than a parser. These cases pin down what it catches and what it deliberately
/// lets through, so the next person to change it can tell which is which.
/// </summary>
public sealed class UserVisibleStringDetectorTests
{
    [Theory]
    [InlineData("<h2>Platform settings</h2>", "Platform settings")]
    [InlineData("<p class=\"lead\">These settings apply to every module.</p>", "These settings apply to every module.")]
    [InlineData("<label>Currency</label>", "Currency")]
    [InlineData("<input placeholder=\"Enter a code\" />", "Enter a code")]
    [InlineData("<img alt=\"Company logo\" src=\"/logo.png\" />", "Company logo")]
    [InlineData("<span title=\"Not yet approved\"></span>", "Not yet approved")]
    public void ALiteralAUserWouldReadIsCaught(string razor, string expected) =>
        UserVisibleStringTests.LiteralsRenderedBy(razor).Should().Contain(expected);

    [Theory]
    [InlineData("<h2>@T[\"Platform settings\"]</h2>")]
    [InlineData("<label>@T[\"Currency\"]</label> <span>@Model.CurrencyCode</span>")]
    [InlineData("<input placeholder=\"@T[\"Enter a code\"]\" />")]
    [InlineData("<p>@culture.DateTimeFormat.GetMonthName(month)</p>")]
    [InlineData("<span>@(Model.Count > 0 ? Model.First : Model.Last)</span>")]
    [InlineData("<td>@Html.ValidationMessage(nameof(Thing.Name), new { @class = \"text-danger\" })</td>")]
    public void TextThatCameFromTheLocaliserOrFromDataIsNotCaught(string razor) =>
        UserVisibleStringTests.LiteralsRenderedBy(razor).Should().BeEmpty();

    [Theory]
    [InlineData("@model WorkMate.Platform.ViewModels.WorkMateSettingsViewModel")]
    [InlineData("@using System.Globalization")]
    [InlineData("@* A comment explaining why this view exists. *@")]
    [InlineData("<!-- an ordinary html comment -->")]
    [InlineData("<script>var message = 'Saved';</script>")]
    [InlineData("<style>.thing { content: 'x'; }</style>")]
    public void DirectivesCommentsAndScriptAreNotMarkupAUserReads(string razor) =>
        UserVisibleStringTests.LiteralsRenderedBy(razor).Should().BeEmpty();

    [Fact]
    public void CSharpInsideACodeBlockIsNotText()
    {
        const string Razor = """
            @{
                var culture = CultureInfo.CurrentUICulture;
                var greeting = "this is a code string, not markup";
            }
            <p>@greeting</p>
            """;

        UserVisibleStringTests.LiteralsRenderedBy(Razor).Should().BeEmpty();
    }

    [Fact]
    public void AStatementWrittenBareInsideALoopBodyIsNotText()
    {
        const string Razor = """
            @foreach (var day in daysOfWeek)
            {
                var fieldName = fieldPrefix + day.For;
                <label for="@fieldName">@culture.DateTimeFormat.GetDayName(day.Day)</label>
            }
            """;

        UserVisibleStringTests.LiteralsRenderedBy(Razor).Should().BeEmpty();
    }

    [Fact]
    public void ALiteralInsideALoopBodyIsStillCaught()
    {
        const string Razor = """
            @foreach (var day in daysOfWeek)
            {
                var fieldName = fieldPrefix + day.For;
                <label for="@fieldName">Working day</label>
            }
            """;

        UserVisibleStringTests.LiteralsRenderedBy(Razor).Should().Contain("Working day");
    }

    [Fact]
    public void ProseEndingInASemicolonInsideATagIsStillCaught() =>
        UserVisibleStringTests.LiteralsRenderedBy("<p>Approved; awaiting payment</p>")
            .Should().Contain("Approved; awaiting payment");

    [Fact]
    public void PunctuationBetweenExpressionsIsNotProse() =>
        UserVisibleStringTests.LiteralsRenderedBy("<option>@name (@code) — @count</option>")
            .Should().BeEmpty();

    [Fact]
    public void AnIfElseChainWithLocalisedMarkupInEveryBranchIsNotCaught()
    {
        const string Razor = """
            @if (type.RetiredOn is not null)
            {
                <span>@T["Retired"]</span>
            }
            else if (type.IsSystemDefined)
            {
                <span>@T["System-defined"]</span>
            }
            else
            {
                <span>@T["Active"]</span>
            }
            """;

        UserVisibleStringTests.LiteralsRenderedBy(Razor).Should().BeEmpty();
    }

    [Fact]
    public void ALiteralLeftInAnElseBranchIsStillCaught()
    {
        const string Razor = """
            @if (type.RetiredOn is not null)
            {
                <span>@T["Retired"]</span>
            }
            else
            {
                <span>Active</span>
            }
            """;

        UserVisibleStringTests.LiteralsRenderedBy(Razor).Should().Contain("Active");
    }

    [Fact]
    public void TheWordElseOrCatchInOrdinaryProseIsStillCaught()
    {
        UserVisibleStringTests.LiteralsRenderedBy("<p>Approve, or catch the error and try again</p>")
            .Should().Contain("Approve, or catch the error and try again");
    }

    [Fact]
    public void ALiteralGivenToAUserVisibleSinkIsCaught()
    {
        const string CSharp = """
            context.Updater.ModelState.AddModelError("Currency", "The currency is not valid.");
            """;

        UserVisibleStringTests.LiteralsPassedTo(CSharp, ("AddModelError", 1))
            .Should().Contain("The currency is not valid.");
    }

    [Fact]
    public void ALocalisedValueGivenToAUserVisibleSinkIsNotCaught()
    {
        const string CSharp = """
            context.Updater.ModelState.AddModelError($"{Prefix}.{error.PropertyName}", error.Message.Value);
            """;

        UserVisibleStringTests.LiteralsPassedTo(CSharp, ("AddModelError", 1)).Should().BeEmpty();
    }

    [Fact]
    public void TheKeyArgumentOfASinkIsNotTreatedAsUserVisible()
    {
        // AddModelError's first argument is a field name, which is code, not prose.
        const string CSharp = """
            ModelState.AddModelError("CurrencyCode", S["The currency is not valid."]);
            """;

        UserVisibleStringTests.LiteralsPassedTo(CSharp, ("AddModelError", 1)).Should().BeEmpty();
    }

    [Fact]
    public void ANamedArgumentIsUnwrappedBeforeBeingJudged()
    {
        const string CSharp = """
            new Permission(nameof(Thing), description: "A literal description");
            """;

        UserVisibleStringTests.LiteralsPassedTo(CSharp, ("Permission", 1))
            .Should().Contain("A literal description");
    }
}
