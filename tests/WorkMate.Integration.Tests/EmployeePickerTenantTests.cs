using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Platform.Components;
using WorkMate.Platform.Services;
using WorkMate.Platform.Shell;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The employee picker: that everybody is reachable, that the search route is the one the control
/// points at, and that the rules each form asks for are the rules it gets.
/// </summary>
/// <remarks>
/// <b>The defect.</b> The picker was a <c>&lt;select&gt;</c> built from the first hundred employees
/// of any status. On the Zenith demo organisation that is a hundred of a hundred and fifty-eight —
/// so fifty-eight people could not be named as a head, as a line manager, or as anything else, from
/// any screen, and nothing anywhere said so. It also offered leavers to a form that cannot accept
/// one.
///
/// The fix is a server-side search, which means the count that matters is not "how many does the
/// control show" but "can every person be reached". That is what these assert.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class EmployeePickerTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public EmployeePickerTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// The URL the control is rendered with is a route this application actually serves.
    /// </summary>
    /// <remarks>
    /// <see cref="EmployeePickerTagHelper.SearchUrl"/> is a constant, because the tag helper is in
    /// <c>WorkMate.Platform</c> and the controller is in <c>WorkMate.Records</c>, which that module
    /// must not reference. A constant that nobody checks is a link that breaks the day somebody
    /// renames an action — and breaks quietly, because the control falls back to being the plain
    /// list it used to be. So it is requested here for real.
    /// </remarks>
    [Fact]
    public async Task TheSearchUrlTheControlPointsAtIsServed()
    {
        var response = await _tenant.Administrator.GetAsync(
            $"{EmployeePickerTagHelper.SearchUrl}?q=&take=1");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "'{0}' is the URL every employee picker is rendered with",
            EmployeePickerTagHelper.SearchUrl);
    }

    /// <summary>
    /// Somebody past the first page is reachable by typing part of their name.
    /// </summary>
    /// <remarks>
    /// The assertion the old control could not have passed. The seeded people are named so that a
    /// shared prefix puts them in one block and a distinctive suffix pulls one of them out of it,
    /// which is how somebody actually finds a colleague.
    /// </remarks>
    [Fact]
    public async Task SomebodyPastTheFirstPageIsFoundBySearching() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var directory = services.GetRequiredService<IEmployeeDirectory>();

            for (var index = 0; index < 60; index++)
            {
                await EmployeeScenario.ActiveEmployeeAsync(
                    services,
                    $"picker-many-{index:D2}",
                    nameEn: index == 57 ? "Zubaida Farrukhabadi" : $"Picker Person {index:D2}");
            }

            var firstPage = await directory.SearchAsync(new EmployeeDirectoryQuery(Take: 20));

            firstPage.Items.Should().HaveCount(20, "a page, not the tenant");
            firstPage.Total.Should().BeGreaterThan(20, "and the control is told there is more");

            // The one the old control could not have offered: 58th of the set, well past any page.
            var found = await directory.SearchAsync(new EmployeeDirectoryQuery("Farrukhabadi"));

            found.Items.Should().ContainSingle()
                .Which.Code.Should().Be("picker-many-57");

            // And by code, which is how somebody with a staff number in front of them searches.
            var byCode = await directory.SearchAsync(new EmployeeDirectoryQuery("picker-many-57"));

            byCode.Items.Should().ContainSingle().Which.Code.Should().Be("picker-many-57");
        });

    /// <summary>
    /// An option says where the person sits, and a leaver is offered only where leavers are wanted.
    /// </summary>
    [Fact]
    public async Task AnOptionNamesTheUnitAndLeaversAreExcludedWhenAsked() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var directory = services.GetRequiredService<IEmployeeDirectory>();
            var employees = services.GetRequiredService<IEmployeeService>();

            var staying = await EmployeeScenario.ActiveEmployeeAsync(
                services, "picker-where-1", nameEn: "Qasim Pickerton");

            var leaving = await EmployeeScenario.ActiveEmployeeAsync(
                services, "picker-gone-1", nameEn: "Hadiya Pickerton");

            (await employees.ExitAsync(leaving, new DateOnly(2025, 6, 30))).Succeeded.Should().BeTrue();

            var all = await directory.SearchAsync(new EmployeeDirectoryQuery("Pickerton"));

            all.Items.Select(item => item.Code).Should().Contain(
                ["picker-where-1", "picker-gone-1"],
                "a form recording something dated in the past must be able to name somebody who has left");

            var current = await directory.SearchAsync(
                new EmployeeDirectoryQuery("Pickerton", IncludeExited: false));

            current.Items.Select(item => item.Code).Should().BeEquivalentTo(
                ["picker-where-1"],
                "appointing a head cannot name somebody whose last day has passed, so the control does not offer it");

            // Where they sit, which is the third thing on an option and the one that tells two
            // people with the same name apart.
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "picker-org");
            var unit = await DimensionGraphScenario.RecordAsync(
                services, types.Department, "picker-dept", new DateOnly(2024, 1, 1), nameEn: "Treasury");

            (await services.GetRequiredService<WorkMate.Dimensions.Services.IEmployeeAssignmentService>()
                .PlaceAsync(staying, structure, unit, new DateOnly(2024, 1, 1))).Succeeded.Should().BeTrue();

            var placed = await directory.SearchAsync(new EmployeeDirectoryQuery("Qasim Pickerton"));

            placed.Items.Should().ContainSingle()
                .Which.Where.Should().Be("Treasury", "the option says which Pickerton this is");
        });

    /// <summary>
    /// The endpoint answers in the shape the control reads, and refuses an anonymous caller.
    /// </summary>
    [Fact]
    public async Task TheEndpointAnswersInTheShapeTheControlReadsAndNotToStrangers()
    {
        var anonymous = await _tenant.Anonymous.GetAsync($"{EmployeePickerTagHelper.SearchUrl}?q=a");

        anonymous.StatusCode.Should().NotBe(
            HttpStatusCode.OK, "the picker is an admin control, not an employee directory for the world");

        var body = await _tenant.Administrator.GetStringAsync($"{EmployeePickerTagHelper.SearchUrl}?take=5");

        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        document.RootElement.TryGetProperty("total", out _).Should().BeTrue();

        foreach (var item in items.EnumerateArray())
        {
            // Every name the script builds an option from. A renamed property is a control that
            // silently shows "undefined (undefined)".
            foreach (var property in new[] { "id", "code", "name", "where" })
            {
                item.TryGetProperty(property, out _).Should().BeTrue(
                    "employee-picker.js reads '{0}' off every result", property);
            }
        }
    }

    /// <summary>
    /// The script the control depends on is registered under the name the views ask for.
    /// </summary>
    /// <remarks>
    /// A view asking for a resource that does not exist renders nothing and says nothing, so the
    /// picker would quietly go back to being a plain list of the first page — which is the defect
    /// this whole change is about, returning by a different door.
    /// </remarks>
    [Fact]
    public void ThePickerScriptIsRegistered()
    {
        var options = new OrchardCore.ResourceManagement.ResourceManagementOptions();

        new WorkMateResourceManifest().Configure(options);

        options.ResourceManifests
            .SelectMany(manifest => manifest.GetResources("script"))
            .Should().Contain(resource => resource.Key == "workmate-employee-picker");
    }
}
