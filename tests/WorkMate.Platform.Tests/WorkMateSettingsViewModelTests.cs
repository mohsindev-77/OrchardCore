using FluentAssertions;
using WorkMate.Platform.Models;
using WorkMate.Platform.ViewModels;
using Xunit;

namespace WorkMate.Platform.Tests;

public sealed class WorkMateSettingsViewModelTests
{
    [Fact]
    public void FillingFromSettingsAndConvertingBackPreservesEveryValue()
    {
        var settings = new WorkMateSettings
        {
            DefaultCulture = "ar",
            Calendar = CalendarPreference.GregorianWithHijri,
            TextDirection = TextDirectionPreference.RightToLeft,
            FiscalYearStartMonth = 7,
            FiscalYearStartDay = 15,
            CurrencyCode = "SAR",
            WeekStartsOn = DayOfWeek.Sunday,
            CustomerCode = "gulf-trading",
            WorkingDays = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
        };

        var model = new WorkMateSettingsViewModel();
        WorkMateSettingsViewModel.Fill(model, settings, ["en", "ar"]);

        model.ToSettings().Should().BeEquivalentTo(settings);
    }

    [Fact]
    public void ACheckedDayBecomesAWorkingDayAndAnUncheckedOneDoesNot()
    {
        var model = new WorkMateSettingsViewModel
        {
            WorksSaturday = true,
            WorksSunday = true,
            WorksMonday = false,
        };

        var settings = model.ToSettings();

        settings.WorkingDays.Should().BeEquivalentTo([DayOfWeek.Sunday, DayOfWeek.Saturday]);
    }

    [Fact]
    public void WorkingDaysComeBackInWeekOrderWhicheverOrderTheyWereTicked()
    {
        var model = new WorkMateSettingsViewModel
        {
            WorksThursday = true,
            WorksMonday = true,
            WorksSunday = true,
        };

        model.ToSettings().WorkingDays.Should()
            .ContainInOrder(DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Thursday);
    }

    [Theory]
    [InlineData("  aed  ", "AED")]
    [InlineData("sar", "SAR")]
    public void ACurrencyTypedInAnyCaseOrWithSpacesIsNormalised(string typed, string expected) =>
        new WorkMateSettingsViewModel { CurrencyCode = typed }.ToSettings().CurrencyCode.Should().Be(expected);

    [Theory]
    [InlineData("  Gulf-Trading ", "gulf-trading")]
    [InlineData("ACME", "acme")]
    public void ACustomerCodeTypedInAnyCaseOrWithSpacesIsNormalised(string typed, string expected) =>
        new WorkMateSettingsViewModel { CustomerCode = typed }.ToSettings().CustomerCode.Should().Be(expected);
}
