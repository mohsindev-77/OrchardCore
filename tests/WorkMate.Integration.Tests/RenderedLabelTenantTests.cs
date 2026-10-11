using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// No admin screen prints the name of a type where a label should be.
/// </summary>
/// <remarks>
/// <b>The defect.</b> The "Appoint a head" form's picker was labelled
/// <c>Microsoft.AspNetCore.Mvc.Localization.LocalizedHtmlString</c>. The view wrote
/// <c>label="@T["Head of this unit"]"</c>, which is the natural thing to write; the tag helper's
/// <c>Label</c> was a <c>string</c>; and Razor binds an expression to a <c>string</c> property by
/// calling <c>ToString()</c>, which <c>LocalizedHtmlString</c> does not override. Nothing failed —
/// it compiled, it rendered, and the label was the name of a type.
///
/// Four views had it right with <c>.Value</c> and three had it wrong without, which is the real
/// defect: an API that accepts the obvious thing and renders it wrongly. The properties take
/// <c>object?</c> now and go through <c>TagHelperLabel</c>.
///
/// <b>Why this test is a sweep rather than an assertion about one label.</b> The mistake is
/// available at every call site of every tag helper, present and future, and it always looks the
/// same on the page. Checking for the symptom across the screens catches the next one without
/// anybody having to remember the rule — including in places a browser test would not look, such as
/// an <c>aria-label</c> or a <c>title</c>, which are invisible until somebody is relying on them.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class RenderedLabelTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public RenderedLabelTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    /// <summary>
    /// The two type names that a mis-bound localised string renders as.
    /// </summary>
    /// <remarks>
    /// Both, because the same mistake has two shapes: <c>IViewLocalizer</c>'s <c>@T</c> hands out
    /// <c>LocalizedHtmlString</c> and <c>IStringLocalizer</c>'s <c>S</c> hands out
    /// <c>LocalizedString</c>. The second does override <c>ToString()</c> and so is harmless today;
    /// it is here because "harmless today" is not a property anybody checks before changing a
    /// property's type, and because a screen showing either is wrong in the same way.
    /// </remarks>
    private static readonly string[] TypeNamesThatMeanAMisBoundLabel =
    [
        "LocalizedHtmlString",
        "Microsoft.Extensions.Localization.LocalizedString",
    ];

    [Fact]
    public async Task NoAdminScreenRendersTheNameOfALocalisedStringType()
    {
        var screens = await ScreensAsync();

        screens.Should().HaveCountGreaterThan(
            5, "a sweep that visits almost nothing proves almost nothing");

        foreach (var (path, html) in screens)
        {
            foreach (var typeName in TypeNamesThatMeanAMisBoundLabel)
            {
                html.Should().NotContain(
                    typeName,
                    "{0} renders '{1}', which means a localised string was handed to something that "
                    + "called ToString() on it — almost always a tag helper attribute whose property "
                    + "is the wrong type. The label a person reads is the name of a class.",
                    path,
                    typeName);
            }
        }
    }

    /// <summary>
    /// Every admin screen this suite can reach without first inventing data, with its HTML.
    /// </summary>
    /// <remarks>
    /// Enough of a structure and an employee are seeded first, because the screens that carry the
    /// tag helpers are the ones that need something to point at: there is no "appoint a head" form
    /// without a unit to appoint one to, and that form is where the defect was found.
    /// </remarks>
    private async Task<IReadOnlyList<(string Path, string Html)>> ScreensAsync()
    {
        string structureId = null!, recordId = null!, employeeId = null!, typeId = null!;

        await _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("rendered label test"))
            {
                var types = await DimensionGraphScenario.TypesAsync(services);

                typeId = types.Department;
                structureId = await DimensionGraphScenario.StructureAsync(services, "label-sweep");

                recordId = await DimensionGraphScenario.RecordAsync(
                    services, types.Department, "label-sweep-unit", new DateOnly(2024, 1, 1));

                employeeId = await EmployeeScenario.ActiveEmployeeAsync(services, "label-sweep-1");
            }
        });

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        string[] paths =
        [
            "/Admin/Employees/Index",
            "/Admin/Employees/Create",
            $"/Admin/Employees/Edit/{employeeId}",
            $"/Admin/Employees/Placement/Index/{employeeId}",
            $"/Admin/Employees/Placement/Place/{employeeId}",
            "/Admin/Dimensions/Designer/Index",
            $"/Admin/Dimensions/Designer/Index?structureId={structureId}",
            $"/Admin/Dimensions/Designer/AddUnit?structureId={structureId}&asAt={today}",
            $"/Admin/Dimensions/Designer/Rename?structureId={structureId}&recordId={recordId}&asAt={today}",
            $"/Admin/Dimensions/Heads/Set?structureId={structureId}&recordId={recordId}&asAt={today}",
            "/Admin/Dimensions/Structures/Index",
            $"/Admin/Dimensions/Structures/Edit?structureId={structureId}",
            "/Admin/Dimensions/Types/Index",
            $"/Admin/Dimensions/Types/Edit?dimensionTypeId={typeId}",
        ];

        var screens = new List<(string, string)>();

        foreach (var path in paths)
        {
            var response = await _tenant.Administrator.GetAsync(path);

            // A screen this suite cannot reach is not this test's business — it is covered by the
            // tests that own it, and failing here would only move a routing problem somewhere
            // confusing. What matters is that enough of them answered, which is asserted above.
            if (response.StatusCode == HttpStatusCode.OK)
            {
                screens.Add((path, await response.Content.ReadAsStringAsync()));
            }
        }

        return screens;
    }
}
