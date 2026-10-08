using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.Entities;
using OrchardCore.Settings;
using WorkMate.Core;
using WorkMate.Platform.Models;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Creating and changing an employee record, against a real tenant.
/// </summary>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeeRecordTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public EmployeeRecordTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    [Fact]
    public async Task AnEmployeeIsCreatedProspectiveAndDatedFromTheirJoinDate() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var joined = new DateOnly(2025, 3, 10);

            var created = await employees.CreateAsync(
                "rec-new-1", new BilingualText("Imran Qureshi", "عمران قريشي"), joined);

            created.Succeeded.Should().BeTrue(created.Describe());

            var employee = created.Value!;

            employee.Status.Should().Be(
                EmploymentStatus.Prospective,
                "activating somebody is a dated decision a person makes, never inferred from the join date having passed");

            employee.StatusEffectiveFrom.Should().Be(joined);
            employee.JoinDate.Should().Be(joined);
            employee.NameAr.Should().Be("عمران قريشي");
        });

    [Fact]
    public async Task AnEmployeeIsFoundByTheirCodeWhateverItsCapitalisation() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            await EmployeeScenario.CreateAsync(services, "REC-Case-1");

            (await employees.GetByCodeAsync("rec-case-1")).Should().NotBeNull(
                "an import that shouts a code must find the same person, not create a second record");

            (await employees.GetByCodeAsync("REC-Case-1"))!
                .Code.Should().Be("REC-Case-1", "the customer's own capitalisation is what they see");
        });

    /// <summary>
    /// A code stays unique against employees who have left.
    /// </summary>
    /// <remarks>
    /// Payroll history, past approvals and last year's headcount all resolve through an exited
    /// employee. Reusing their code makes every one of those ambiguous about which person it meant,
    /// and nothing would report it — the figures would simply be for two people added together.
    /// </remarks>
    [Fact]
    public async Task ACodeStaysTakenAfterTheEmployeeHasLeft() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            var leaver = await EmployeeScenario.ActiveEmployeeAsync(services, "rec-leaver-1");

            (await employees.ExitAsync(leaver, new DateOnly(2026, 6, 30))).Succeeded.Should().BeTrue();

            var reused = await employees.CreateAsync(
                "rec-leaver-1", new BilingualText("Somebody Else", "شخص آخر"), EmployeeScenario.Joined);

            reused.Succeeded.Should().BeFalse();
            reused.Errors.Should().Contain(error => error.Rule == RecordRule.CodeUniqueness);
        });

    [Fact]
    public async Task TwoEmployeesCannotShareACodeDifferingOnlyInCase() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            await EmployeeScenario.CreateAsync(services, "rec-dup-1");

            var clash = await employees.CreateAsync(
                "REC-DUP-1", new BilingualText("Another Person", "شخص"), EmployeeScenario.Joined);

            clash.Succeeded.Should().BeFalse(
                "collation is the database's property, not ours — the comparison is folded in code so "
                + "SQLite and SQL Server give the same answer");

            clash.Errors.Should().Contain(error => error.Rule == RecordRule.CodeUniqueness);
        });

    [Fact]
    public async Task AMalformedCodeIsRefusedNamingTheField() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "rec bad 1", new BilingualText("Spaces Everywhere", "فراغات"), EmployeeScenario.Joined);

            created.Succeeded.Should().BeFalse();

            created.Errors.Should().Contain(error =>
                error.Rule == RecordRule.CodeFormat &&
                error.Field == nameof(EmployeePart.EmployeeCode));
        });

    /// <summary>
    /// ADR-0003's addendum, on the employee record: English required, Arabic optional by default.
    /// </summary>
    [Fact]
    public async Task AnEmployeeMayHaveNoArabicNameWhileTheTenantDoesNotRequireOne() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "rec-en-only-1", new BilingualText("English Only", string.Empty), EmployeeScenario.Joined);

            created.Succeeded.Should().BeTrue(created.Describe());
            created.Value!.NameAr.Should().BeEmpty();
        });

    [Fact]
    public async Task AnEmployeeAlwaysNeedsAnEnglishName() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "rec-no-en-1", new BilingualText(string.Empty, "بالعربية فقط"), EmployeeScenario.Joined);

            created.Succeeded.Should().BeFalse();

            created.Errors.Should().Contain(error =>
                error.Rule == RecordRule.NameRequired &&
                error.Field == nameof(EmployeePart.NameEn));
        });

    /// <summary>
    /// With the tenant setting on, the Arabic half is required here exactly as it is everywhere else.
    /// </summary>
    /// <remarks>
    /// Asked through <c>IBilingualNamePolicy</c>, the same seam the dimension engine uses, so the
    /// employee record and the organisation chart cannot end up disagreeing about the answer. The
    /// setting is put back afterwards because the tenant is shared by the whole collection.
    /// </remarks>
    [Fact]
    public async Task TheArabicNameIsRequiredWhenTheTenantSaysSo()
    {
        await _tenant.InTenantAsSystemAsync(RequireArabicNamesAsync(required: true));

        try
        {
            await _tenant.InTenantAsSystemAsync(async services =>
            {
                var refused = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                    "rec-ar-required-1", new BilingualText("No Arabic", string.Empty), EmployeeScenario.Joined);

                refused.Succeeded.Should().BeFalse();

                refused.Errors.Should().Contain(error =>
                    error.Rule == RecordRule.NameRequired &&
                    error.Field == nameof(EmployeePart.NameAr));
            });
        }
        finally
        {
            // Put back. The tenant is shared by the whole collection, and a test that left this on
            // would refuse every Arabic-less name in every test that ran after it.
            await _tenant.InTenantAsSystemAsync(RequireArabicNamesAsync(required: false));
        }
    }

    /// <summary>
    /// Flips the tenant's "Require Arabic names" setting.
    /// </summary>
    /// <remarks>
    /// Written onto the site document directly rather than through <c>IWorkMateSettingsService</c>,
    /// which authorises against a signed-in user — and there is none in a service-level test, even
    /// inside a system scope. The same helper <c>DimensionsRecipeStepsTenantTests</c> uses, for the
    /// same reason.
    /// </remarks>
    private static Func<IServiceProvider, Task> RequireArabicNamesAsync(bool required) =>
        async services =>
        {
            var siteService = services.GetRequiredService<ISiteService>();
            var site = await siteService.LoadSiteSettingsAsync();
            var settings = site.GetOrCreate<WorkMateSettings>();

            settings.RequireArabicNames = required;

            site.Put(settings);

            await siteService.UpdateSiteSettingsAsync(site);
        };

    [Fact]
    public async Task ADateOfBirthOnOrAfterTheJoinDateIsRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "rec-dob-1",
                new BilingualText("Born Later", "مولود لاحقا"),
                new DateOnly(2024, 1, 1),
                new EmployeeDetails(DateOfBirth: new DateOnly(2024, 6, 1)));

            created.Succeeded.Should().BeFalse();
            created.Errors.Should().Contain(error => error.Rule == RecordRule.DateOutOfOrder);
        });

    [Fact]
    public async Task AnEmployeeCannotReportToThemselves() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();
            var id = await EmployeeScenario.ActiveEmployeeAsync(services, "rec-self-manager-1");

            var updated = await employees.UpdateAsync(
                id,
                new BilingualText("Self Manager", "مدير نفسه"),
                new EmployeeDetails(LineManagerEmployeeId: id));

            updated.Succeeded.Should().BeFalse();
            updated.Errors.Should().Contain(error => error.Rule == RecordRule.ReportingLineCycle);
        });

    /// <summary>
    /// A reporting line cannot loop, however long the way round.
    /// </summary>
    /// <remarks>
    /// Three people, because a two-person loop is the case everybody tests and a three-person one is
    /// the case that actually happens: a reorganisation makes somebody their own grandmanager and
    /// every approval that walks the line then runs forever.
    /// </remarks>
    [Fact]
    public async Task AReportingLineCannotLoopBackThroughAChain() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            var top = await EmployeeScenario.ActiveEmployeeAsync(services, "rec-chain-top");
            var middle = await EmployeeScenario.ActiveEmployeeAsync(services, "rec-chain-middle");
            var bottom = await EmployeeScenario.ActiveEmployeeAsync(services, "rec-chain-bottom");

            (await employees.UpdateAsync(
                middle, new BilingualText("Middle", "وسط"), new EmployeeDetails(LineManagerEmployeeId: top)))
                .Succeeded.Should().BeTrue();

            (await employees.UpdateAsync(
                bottom, new BilingualText("Bottom", "أسفل"), new EmployeeDetails(LineManagerEmployeeId: middle)))
                .Succeeded.Should().BeTrue();

            var loop = await employees.UpdateAsync(
                top, new BilingualText("Top", "أعلى"), new EmployeeDetails(LineManagerEmployeeId: bottom));

            loop.Succeeded.Should().BeFalse("bottom already reports to top through middle");
            loop.Errors.Should().Contain(error => error.Rule == RecordRule.ReportingLineCycle);
        });

    [Fact]
    public async Task AnUnknownLineManagerIsRefusedRatherThanStored() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                "rec-ghost-manager-1",
                new BilingualText("Reports To Nobody Real", "لا أحد"),
                EmployeeScenario.Joined,
                new EmployeeDetails(LineManagerEmployeeId: "not-a-content-item-id"));

            created.Succeeded.Should().BeFalse();
            created.Errors.Should().Contain(error => error.Rule == RecordRule.UnknownEmployee);
        });

    /// <summary>
    /// The list searches a code and both halves of the name, and filters by status.
    /// </summary>
    [Fact]
    public async Task TheListFindsAnEmployeeByEitherLanguageAndFiltersByStatus() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var employees = services.GetRequiredService<IEmployeeService>();

            await EmployeeScenario.ActiveEmployeeAsync(
                services, "rec-search-1", nameEn: "Zubaida Rahmani", nameAr: "زبيدة رحماني");

            (await employees.ListAsync("Zubaida")).Items
                .Should().Contain(employee => employee.Code == "rec-search-1");

            (await employees.ListAsync("زبيدة")).Items
                .Should().Contain(employee => employee.Code == "rec-search-1",
                    "an Arabic reader searching in Arabic must find the same people");

            (await employees.ListAsync("rec-search-1")).Items
                .Should().Contain(employee => employee.Code == "rec-search-1");

            (await employees.ListAsync("rec-search-1", EmploymentStatus.Exited)).Items
                .Should().BeEmpty("they are active, not exited");
        });

    /// <summary>
    /// The employee's name is readable from the index, without loading the content item.
    /// </summary>
    /// <remarks>
    /// What every list page, picker and designer card actually reads — specification section 7's
    /// rule applied to people rather than to units: a tenant with five thousand employees must not
    /// load five thousand content items to draw a page of twenty.
    ///
    /// <b>Not asserted here: <c>ContentItem.DisplayText</c>.</b> The <c>Employee</c> type binds
    /// <c>TitlePart</c> to the English name with a generated pattern, exactly as every generated
    /// dimension type does — and on a real tenant both come back null. That is a pre-existing gap in
    /// the <c>TitlePart</c> binding rather than anything about the employee record, it predates this
    /// slice, and nothing in WorkMate reads <c>DisplayText</c>; Orchard's own content list screens
    /// do. It is reported rather than asserted, because a test written against today's behaviour
    /// would pin the defect rather than find it.
    /// </remarks>
    [Fact]
    public async Task AnEmployeesNameIsReadableWithoutLoadingTheContentItem() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            await EmployeeScenario.CreateAsync(
                services, "rec-title-1", nameEn: "Farhan Siddiqui", nameAr: "فرحان صديقي");

            var employee = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync("rec-title-1");

            employee.Should().NotBeNull();
            employee!.NameEn.Should().Be("Farhan Siddiqui");
            employee.NameAr.Should().Be("فرحان صديقي");
        });
}
