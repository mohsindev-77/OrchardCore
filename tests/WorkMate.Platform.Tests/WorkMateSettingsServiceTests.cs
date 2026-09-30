using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrchardCore.Entities;
using OrchardCore.Environment.Shell;
using WorkMate.Platform.Models;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Platform.Tests;

public sealed class WorkMateSettingsServiceTests
{
    [Fact]
    public async Task ATenantThatHasNeverSavedSettingsStillGetsEveryDefault()
    {
        var context = new TestContext();

        var settings = await context.Service.GetAsync();

        settings.DefaultCulture.Should().Be("en");
        settings.Calendar.Should().Be(CalendarPreference.Gregorian);
        settings.TextDirection.Should().Be(TextDirectionPreference.FollowCulture);
        settings.FiscalYearStartMonth.Should().Be(1);
        settings.FiscalYearStartDay.Should().Be(1);
        settings.CurrencyCode.Should().Be("BHD");
        settings.WeekStartsOn.Should().Be(DayOfWeek.Sunday);
        settings.WorkingDays.Should().BeEquivalentTo(WorkMateSettings.DefaultWorkingDays);
    }

    [Fact]
    public void TheDefaultsAreBahrainiBecauseTheFirstTenantsAreInBahrain()
    {
        var settings = new WorkMateSettings();

        settings.CurrencyCode.Should().Be("BHD");
        settings.WeekStartsOn.Should().Be(DayOfWeek.Sunday);
        settings.WorkingDays.Should().BeEquivalentTo(
            [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday]);
    }

    [Fact]
    public async Task AnUnsetCustomerCodeIsTakenFromTheTenantName()
    {
        var context = new TestContext(tenantName: "gulf-trading-config");

        var settings = await context.Service.GetAsync();

        settings.CustomerCode.Should().Be("gulf-trading");
    }

    [Fact]
    public async Task AStoredCustomerCodeIsNotOverwrittenByTheTenantName()
    {
        var context = new TestContext(tenantName: "gulf-trading-config");
        context.Store(new WorkMateSettings { CustomerCode = "gt" });

        var settings = await context.Service.GetAsync();

        settings.CustomerCode.Should().Be("gt");
    }

    [Theory]
    [InlineData("gulf-trading-prod", "gulf-trading")]
    [InlineData("acme-config", "acme")]
    [InlineData("Default", "default")]
    [InlineData("", "")]
    public void TheCustomerCodeIsEverythingBeforeTheLastHyphenOfTheTenantName(string tenantName, string expected) =>
        WorkMateSettingsService.DeriveCustomerCode(tenantName).Should().Be(expected);

    [Fact]
    public async Task ATenantWhoseCulturesDoNotIncludeTheDefaultLocaleFallsBackToItsOwn()
    {
        // A tenant set up outside the base recipe has only the culture its setup chose.
        var context = new TestContext();
        context.Localization.SupportedCultures = ["en-US"];
        context.Localization.DefaultCulture = "en-US";

        var settings = await context.Service.GetAsync();

        settings.DefaultCulture.Should().Be("en-US",
            "a default locale the tenant would refuse to save is not a sensible default");
    }

    [Fact]
    public async Task FallingBackOnTheDefaultLocaleIsLoggedAsAWarning()
    {
        // The fallback keeps the tenant working, but it means something is wrong with the
        // tenant's setup, so it must not be silent.
        var context = new TestContext(tenantName: "gulf-trading-config");
        context.Localization.SupportedCultures = ["en-US"];
        context.Localization.DefaultCulture = "en-US";

        await context.Service.GetAsync();

        var warning = context.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;

        warning.Message.Should().Contain("gulf-trading-config").And.Contain("en").And.Contain("en-US");
    }

    [Fact]
    public async Task ATenantWhoseCulturesAreFineLogsNothing()
    {
        var context = new TestContext();

        await context.Service.GetAsync();

        context.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AUserWithoutThePermissionCannotChangeTheSettings()
    {
        var context = new TestContext();
        context.Authorization.Allow = false;

        var result = await context.Service.UpdateAsync(Valid());

        result.Status.Should().Be(SettingsUpdateStatus.NotAuthorised);
        context.SiteService.UpdateCount.Should().Be(0);
    }

    [Fact]
    public async Task AnAnonymousCallerCannotChangeTheSettings()
    {
        var context = new TestContext();
        context.HttpContextAccessor.HttpContext = null;

        var result = await context.Service.UpdateAsync(Valid());

        result.Status.Should().Be(SettingsUpdateStatus.NotAuthorised);
        context.SiteService.UpdateCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidSettingsAreStored()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s =>
        {
            s.CurrencyCode = "SAR";
            s.FiscalYearStartMonth = 4;
            s.FiscalYearStartDay = 6;
        }));

        result.Succeeded.Should().BeTrue();
        context.SiteService.UpdateCount.Should().Be(1);

        var stored = await context.Service.GetAsync();
        stored.CurrencyCode.Should().Be("SAR");
        stored.FiscalYearStartMonth.Should().Be(4);
        stored.FiscalYearStartDay.Should().Be(6);
    }

    [Fact]
    public async Task SavingAShorterWorkingWeekReplacesTheDefaultRatherThanMergingWithIt()
    {
        // The Gulf week is Sunday to Thursday. Deserialising into a collection that already
        // holds the Monday-to-Friday default can populate rather than replace it, which would
        // quietly make Friday a working day again.
        var context = new TestContext();
        DayOfWeek[] gulfWeek =
        [
            DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        ];

        (await context.Service.UpdateAsync(Valid(s => s.WorkingDays = gulfWeek))).Succeeded.Should().BeTrue();

        var stored = await context.Service.GetAsync();

        stored.WorkingDays.Should().BeEquivalentTo(gulfWeek);
        stored.WorkingDays.Should().NotContain(DayOfWeek.Friday);
    }

    [Fact]
    public async Task ADefaultLocaleOutsideTheTenantsCulturesIsRejected()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s => s.DefaultCulture = "fr"));

        result.Status.Should().Be(SettingsUpdateStatus.Invalid);
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.DefaultCulture));
        context.SiteService.UpdateCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task AFiscalYearStartingOutsideTheTwelveMonthsIsRejected(int month)
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s => s.FiscalYearStartMonth = month));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.FiscalYearStartMonth));
    }

    [Fact]
    public async Task AFiscalYearStartingOnADayTheMonthDoesNotHaveIsRejected()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s =>
        {
            s.FiscalYearStartMonth = 2;
            s.FiscalYearStartDay = 30;
        }));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.FiscalYearStartDay));
    }

    [Fact]
    public async Task AFiscalYearCannotBePinnedToTheTwentyNinthOfFebruary()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s =>
        {
            s.FiscalYearStartMonth = 2;
            s.FiscalYearStartDay = 29;
        }));

        result.Status.Should().Be(SettingsUpdateStatus.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AE")]
    [InlineData("AEDX")]
    [InlineData("aed")]
    public async Task ACurrencyThatIsNotAThreeLetterIsoCodeIsRejected(string currency)
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s => s.CurrencyCode = currency));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.CurrencyCode));
    }

    [Fact]
    public async Task AWeekWithNoWorkingDaysIsRejected()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s => s.WorkingDays = []));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.WorkingDays));
    }

    [Fact]
    public async Task ADayListedTwiceAsAWorkingDayIsRejected()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(
            Valid(s => s.WorkingDays = [DayOfWeek.Monday, DayOfWeek.Monday]));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.WorkingDays));
    }

    [Theory]
    [InlineData("Gulf Trading")]
    [InlineData("1gulf")]
    [InlineData("gulf_trading")]
    public async Task ACustomerCodeThatIsNotASlugIsRejected(string code)
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s => s.CustomerCode = code));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(WorkMateSettings.CustomerCode));
    }

    [Fact]
    public async Task EveryValidationFailureIsReportedAtOnceRatherThanOneAtATime()
    {
        var context = new TestContext();

        var result = await context.Service.UpdateAsync(Valid(s =>
        {
            s.DefaultCulture = "fr";
            s.CurrencyCode = "nope";
            s.WorkingDays = [];
        }));

        result.Errors.Should().HaveCount(3);
    }

    private static WorkMateSettings Valid(Action<WorkMateSettings>? alter = null)
    {
        var settings = new WorkMateSettings();
        alter?.Invoke(settings);

        return settings;
    }

    private sealed class TestContext
    {
        public TestContext(string tenantName = "acme-config")
        {
            SiteService = new FakeSiteService();
            Localization = new FakeLocalizationService();
            Authorization = new FakeAuthorizationService();
            HttpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test")),
                },
            };

            Logger = new RecordingLogger<WorkMateSettingsService>();

            Service = new WorkMateSettingsService(
                SiteService,
                Localization,
                Authorization,
                HttpContextAccessor,
                new ShellSettings { Name = tenantName },
                Logger,
                new PassThroughStringLocalizer<WorkMateSettingsService>());
        }

        public RecordingLogger<WorkMateSettingsService> Logger { get; }

        public FakeSiteService SiteService { get; }

        public FakeLocalizationService Localization { get; }

        public FakeAuthorizationService Authorization { get; }

        public HttpContextAccessor HttpContextAccessor { get; }

        public WorkMateSettingsService Service { get; }

        public void Store(WorkMateSettings settings) => SiteService.Site.Put(settings);
    }
}
