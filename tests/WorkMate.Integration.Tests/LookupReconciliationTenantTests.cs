using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentFields.Fields;
using OrchardCore.ContentManagement;
using OrchardCore.Flows.Models;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Finding the values records hold that no entry matches, and clearing them up deliberately.
/// </summary>
/// <remarks>
/// <b>Its own tenant, not the shared one.</b> The scan reads every employee record in the tenant,
/// so on the shared base tenant it would see whatever the rest of the suite happened to have
/// created, and a count assertion would pass or fail depending on which tests ran first. A tenant
/// of its own is the only way the numbers mean anything.
/// </remarks>
public sealed class LookupReconciliationTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a <c>WebApplicationFactory</c>, which is disposable. <see cref="DisposeAsync"/>
    /// is what actually releases it; this exists because the analyser cannot see that, and
    /// disposing twice is harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    public async Task InitializeAsync()
    {
        _tenant = new BaseTenantFixture();

        await _tenant.InitializeAsync();
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    /// <summary>
    /// The scan finds what no entry matches, counts it, and leaves everything alone.
    /// </summary>
    [Fact]
    public async Task TheScanFindsUnmatchedValuesAndChangesNothing()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
        {
            var scan = await services.GetRequiredService<ILookupValueReconciliation>().ScanAsync(
                StandardLookupLists.Grade);

            scan.WasTruncated.Should().BeFalse();
            scan.EmployeesScanned.Should().Be(3);

            // Most-used first: the value on two records before the one on one.
            scan.Values.Select(value => value.Value).Should().Equal(["Grade One", "g-one"]);

            var first = scan.Values[0];

            first.Count.Should().Be(2);
            first.SampleEmployeeCodes.Should().BeEquivalentTo(["recon-1", "recon-2"]);

            // G1 is an entry, so a record holding it is not unmatched; and the third employee's
            // row holds it.
            scan.Values.Should().NotContain(value => value.Value == "G1");
        });

        await InTenantAsync(async services =>
        {
            var grades = await GradesAsync(services);

            grades.Should().BeEquivalentTo(["Grade One", "Grade One", "g-one", "G1"],
                "a scan reads and never writes");
        });
    }

    /// <summary>
    /// A retired entry is not unmatched: it is a value the list knows and no longer offers.
    /// </summary>
    [Fact]
    public async Task ARetiredEntryIsNotReportedAsUnmatched()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();
            var grade = (await lists.GetByCodeAsync(StandardLookupLists.Grade))!;

            var retired = await lists.UpdateAsync(
                grade.LookupListId,
                grade.Name,
                [.. grade.Items.Select(item => item.Code == "G1" ? item with { IsActive = false } : item)]);

            retired.Succeeded.Should().BeTrue(retired.Describe());
        });

        await InTenantAsync(async services =>
        {
            var scan = await services.GetRequiredService<ILookupValueReconciliation>().ScanAsync(
                StandardLookupLists.Grade);

            scan.Values.Should().NotContain(
                value => value.Value == "G1",
                "retiring an entry stops it being offered; it does not make the records holding it wrong");
        });
    }

    /// <summary>
    /// Mapping rewrites every record holding the value, and nothing else.
    /// </summary>
    [Fact]
    public async Task MappingRewritesEveryRecordHoldingTheValue()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
        {
            var result = await services.GetRequiredService<ILookupValueReconciliation>().MapAsync(
                StandardLookupLists.Grade, "Grade One", "G1");

            result.Succeeded.Should().BeTrue(result.Describe());
            result.Value.Should().Be(2, "two rows held it");
        });

        await InTenantAsync(async services =>
        {
            var grades = await GradesAsync(services);

            grades.Should().BeEquivalentTo(["G1", "G1", "g-one", "G1"],
                "the two that held 'Grade One' now hold G1, and the other two are untouched");
        });
    }

    /// <summary>
    /// Mapping is recorded, once, as a change to records rather than as a change to the list.
    /// </summary>
    [Fact]
    public async Task MappingIsAudited()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
            await services.GetRequiredService<ILookupValueReconciliation>().MapAsync(
                StandardLookupLists.Grade, "Grade One", "G1"));

        var auditTrail = await BaseTenantFixture.GetPageAsync(
            _tenant.Administrator, "/Admin/AuditTrail");

        auditTrail.Should().Contain(
            "LookupValuesRemapped",
            "a bulk rewrite of records is the kind of thing an auditor comes looking for");
    }

    [Fact]
    public async Task MappingOntoSomethingThatIsNotAnEntryIsRefused()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
        {
            var result = await services.GetRequiredService<ILookupValueReconciliation>().MapAsync(
                StandardLookupLists.Grade, "Grade One", "NOSUCHGRADE");

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.UnknownLookupItem);
        });

        await InTenantAsync(async services =>
            (await GradesAsync(services)).Should().Contain("Grade One", "nothing was written"));
    }

    /// <summary>
    /// A value that is already an entry is not unmatched, so mapping it is refused.
    /// </summary>
    /// <remarks>
    /// The screen never offers it — only unmatched values appear — so reaching this takes a
    /// hand-made request. It is refused rather than performed because the records holding it are
    /// not wrong, and rewriting them would be changing data for no reason anybody asked for.
    /// </remarks>
    [Fact]
    public async Task MappingAValueThatIsAlreadyAnEntryIsRefused()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
        {
            var result = await services.GetRequiredService<ILookupValueReconciliation>().MapAsync(
                StandardLookupLists.Grade, "G1", "G2");

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.ImmutableOnceInUse);
        });
    }

    /// <summary>
    /// The core nationality is scanned and mapped alongside the section fields.
    /// </summary>
    /// <remarks>
    /// It is the one bound location that is not a content field, so it is the one that a scan built
    /// only from content definitions would silently miss — and nationality is the list a customer
    /// is most likely to have typed by hand.
    /// </remarks>
    [Fact]
    public async Task TheCoreNationalityIsScannedAndMapped()
    {
        await SeedAsync();

        await InTenantAsync(async services =>
        {
            var scan = await services.GetRequiredService<ILookupValueReconciliation>().ScanAsync(
                StandardLookupLists.Nationality);

            scan.Values.Select(value => value.Value).Should().Contain("ZZ");

            var mapped = await services.GetRequiredService<ILookupValueReconciliation>().MapAsync(
                StandardLookupLists.Nationality, "ZZ", "BH");

            mapped.Succeeded.Should().BeTrue(mapped.Describe());
            mapped.Value.Should().Be(1);
        });

        await InTenantAsync(async services =>
            (await NationalityOfAsync(services, "recon-1")).Should().Be("BH"));
    }

    /// <summary>
    /// The nationality on an employee's record, read through the one call that carries it.
    /// </summary>
    /// <remarks>
    /// <b>Through <c>GetAsync</c> rather than <c>GetByCodeAsync</c>, and the difference is a trap
    /// worth naming.</b> <c>GetByCodeAsync</c> and <c>ListAsync</c> build an <c>EmployeeRecord</c>
    /// from the <c>EmployeeIndex</c> row, which carries code, names, status and dates and nothing
    /// else — so nationality, date of birth and photo come back empty from those two and populated
    /// from <c>GetAsync</c>. One type, two fidelities, no indication which you have. An earlier
    /// version of this test asserted through <c>GetByCodeAsync</c> and spent a long time looking
    /// for a write that had worked all along.
    /// </remarks>
    private static async Task<string> NationalityOfAsync(IServiceProvider services, string code)
    {
        var employees = services.GetRequiredService<IEmployeeService>();
        var found = await employees.GetByCodeAsync(code);

        found.Should().NotBeNull();

        return (await employees.GetAsync(found!.EmployeeId))!.NationalityCode;
    }

    /// <summary>
    /// Where the scan looks comes from the live definitions, and names the core separately.
    /// </summary>
    [Fact]
    public async Task TheBoundLocationsAreReadFromTheDefinitions() =>
        await InTenantAsync(async services =>
        {
            var reconciliation = services.GetRequiredService<ILookupValueReconciliation>();

            var grades = await reconciliation.BoundLocationsAsync(StandardLookupLists.Grade);

            grades.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new
                {
                    SectionName = EmployeeSections.JobDetails,
                    ItemTypeName = EmployeeSections.JobDetailType,
                    FieldName = "GradeCode",
                });

            var nationalities = await reconciliation.BoundLocationsAsync(StandardLookupLists.Nationality);

            nationalities.Should().Contain(
                location => location.FieldName == LookupValueReconciliation.CoreNationality,
                "the employee core's nationality is not a content field and has to be named");

            nationalities.Should().Contain(
                location => location.FieldName == "DependantNationalityCode",
                "and a dependant's is, so both are checked together");

            // A list nothing offers has nowhere to look, which is what the screen says rather than
            // reporting "no unmatched values" and letting a reader conclude the data is clean.
            (await reconciliation.BoundLocationsAsync(StandardLookupLists.JobTitle))
                .Should().BeEmpty("job-title ships as a list and is deliberately bound to nothing");
        });

    // ---- harness ---------------------------------------------------------------------------

    /// <summary>
    /// Three employees: two holding one unmatched grade, one holding another, and one holding a
    /// real entry. Plus an unmatched nationality on the core.
    /// </summary>
    /// <remarks>
    /// <b>Creating and adding section rows are two scopes, deliberately.</b> Written as one, the
    /// seed created an employee through <c>IEmployeeService</c> and then loaded the same item back
    /// through <c>IContentManager</c> before anything had flushed — which left the published and
    /// the latest versions saying different things about the core, so the scan (which reads one)
    /// and <c>IEmployeeService</c> (which reads the other) disagreed about the same employee. That
    /// is a thing a test harness can do and the product cannot: a section row is only ever added
    /// through Orchard's content editor, in a request of its own.
    /// </remarks>
    private async Task SeedAsync()
    {
        await InTenantAsync(async services =>
        {
            await CreateAsync(services, "recon-1", nationality: "ZZ");
            await CreateAsync(services, "recon-2");
            await CreateAsync(services, "recon-3");
        });

        await InTenantAsync(async services =>
        {
            await AddGradesAsync(services, "recon-1", "Grade One");
            await AddGradesAsync(services, "recon-2", "Grade One");
            await AddGradesAsync(services, "recon-3", "g-one", "G1");
        });
    }

    private static async Task CreateAsync(IServiceProvider services, string code, string? nationality = null)
    {
        var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
            code,
            new BilingualText(code, string.Empty),
            new DateOnly(2024, 1, 1),
            new EmployeeDetails(null, nationality, Gender.Unspecified, null, null));

        created.Succeeded.Should().BeTrue(created.Describe());
    }

    /// <summary>
    /// Adds job detail rows the way Orchard's content editor does: latest version, update, publish.
    /// </summary>
    private static async Task AddGradesAsync(IServiceProvider services, string code, params string[] grades)
    {
        var employee = await services.GetRequiredService<IEmployeeService>().GetByCodeAsync(code);
        var contentManager = services.GetRequiredService<IContentManager>();

        var item = await contentManager.GetAsync(employee!.EmployeeId, VersionOptions.Latest);

        foreach (var grade in grades)
        {
            var row = await contentManager.NewAsync(EmployeeSections.JobDetailType);

            row.Alter<ContentPart>(EmployeeSections.JobDetailType, part =>
                part.Alter<TextField>("GradeCode", field => field.Text = grade));

            item!.Alter<BagPart>(EmployeeSections.JobDetails, bag => bag.ContentItems.Add(row));
        }

        await contentManager.UpdateAsync(item!);
        await contentManager.PublishAsync(item!);
    }

    /// <summary>Every grade stored on every job detail row in the tenant.</summary>
    private static async Task<List<string>> GradesAsync(IServiceProvider services)
    {
        var employees = await services.GetRequiredService<IEmployeeService>().ListAsync(skip: 0, take: 100);
        var contentManager = services.GetRequiredService<IContentManager>();

        var grades = new List<string>();

        foreach (var record in employees.Items)
        {
            var employee = await contentManager.GetAsync(record.EmployeeId);
            var bag = employee?.Get<BagPart>(EmployeeSections.JobDetails);

            foreach (var row in bag?.ContentItems ?? [])
            {
                var text = row.Get<ContentPart>(EmployeeSections.JobDetailType)
                    ?.Get<TextField>("GradeCode")?.Text;

                if (!string.IsNullOrEmpty(text))
                {
                    grades.Add(text);
                }
            }
        }

        return grades;
    }

    private Task InTenantAsync(Func<IServiceProvider, Task> work) =>
        _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup reconciliation test"))
            {
                await work(services);
            }
        });
}
