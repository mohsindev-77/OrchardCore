using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.Environment.Shell;
using OrchardCore.Flows.Models;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// A module brings the Orchard features it needs with it, on a tenant whose recipe never named them.
/// </summary>
/// <remarks>
/// <b>The defect.</b> The employee record's sections are <c>BagPart</c>s, registered by the
/// <c>OrchardCore.Flows</c> feature. Session A1 added Flows to <c>recipes/base.recipe.json</c> and to
/// nothing else, so it reached tenants created after that edit and no others — and on an existing
/// tenant the sections did not work, silently, because a part nothing registered is not a broken
/// part, it is a part Orchard has never heard of.
///
/// <b>Why the source-level guard is not enough on its own.</b>
/// <c>ModuleFeatureDependencyTests</c> asserts the manifest *says* the right thing. It cannot
/// assert that saying it has any effect: that Orchard resolves a manifest dependency every time it
/// builds a shell rather than only when a feature is first enabled, which is the whole premise. If
/// that were untrue, the declaration would be as useless as the recipe line and every test here
/// would still pass.
///
/// So this builds a tenant the way the broken one was built — the base recipe with
/// <c>OrchardCore.Flows</c> struck out of its enable list — and asks whether the employee sections
/// work anyway. They can only work if the manifest carried the feature.
/// </remarks>
public sealed class ModuleFeatureDependencyTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// The fixture is a WebApplicationFactory, which is disposable. DisposeAsync is what actually
    /// releases it; this exists because the analyser cannot see that, and disposing twice is
    /// harmless.
    /// </summary>
    public void Dispose() => _tenant?.Dispose();

    private BaseTenantFixture _tenant = default!;

    /// <summary>
    /// Everything the base recipe enables that a WorkMate manifest also declares.
    /// </summary>
    /// <remarks>
    /// Withheld together rather than Flows alone, because the fix was to the rule and not to one
    /// line: if any of these only ever arrived through the recipe, this tenant is the one that
    /// finds out. Media is in the list and is the reason the employee document section's attachment
    /// field exists at all.
    /// </remarks>
    private static readonly string[] Withheld =
    [
        "OrchardCore.Flows",
        "OrchardCore.Title",
        "OrchardCore.ContentFields",
        "OrchardCore.AuditTrail",
        "OrchardCore.Deployment",
        "OrchardCore.Media",
        "OrchardCore.ContentTypes",
        "OrchardCore.Contents",
    ];

    public async Task InitializeAsync()
    {
        _tenant = BaseTenantFixture.Withholding(Withheld);

        await _tenant.InitializeAsync();
    }

    public Task DisposeAsync() => _tenant.DisposeAsync();

    /// <summary>
    /// The features are on, although nothing in the recipe asked for them.
    /// </summary>
    [Fact]
    public async Task TheFeaturesAModuleDeclaresAreEnabledAlthoughTheRecipeWithheldThem()
    {
        var enabled = await EnabledFeatureIdsAsync();

        enabled.Should().Contain("WorkMate.Records", "the module itself is what the recipe enabled");

        foreach (var feature in Withheld)
        {
            enabled.Should().Contain(
                feature,
                "'{0}' is a manifest dependency of a WorkMate module, so enabling that module enables it — "
                + "even though this tenant's recipe never named it",
                feature);
        }
    }

    /// <summary>
    /// And the thing the feature exists for actually works: an employee's sections round-trip.
    /// </summary>
    /// <remarks>
    /// The feature being listed as enabled is a weaker claim than it looks — it says Orchard
    /// resolved the dependency, not that the part is usable. This is the assertion that would have
    /// failed on the broken tenant: `BagPart` is what the employee's sections are, and an employee
    /// whose section cannot be read back is the defect as it was actually experienced.
    /// </remarks>
    [Fact]
    public async Task AnEmployeesSectionsWorkOnThatTenant()
    {
        await _tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("feature dependency test"))
            {
                var definitions = services.GetRequiredService<IContentDefinitionManager>();
                var definition = await definitions.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

                definition.Should().NotBeNull("the migration ran and defined the type");

                // The sections are BagParts attached to the Employee type. Without OrchardCore.Flows
                // the type definition would still name them — a definition is data — but the part
                // would not resolve, which is exactly why "is it defined" is not the question.
                var created = await services.GetRequiredService<IEmployeeService>().CreateAsync(
                    "feature-dep-1",
                    new WorkMate.Core.BilingualText("Section Tester", "مختبر الأقسام"),
                    new DateOnly(2024, 1, 1));

                created.Succeeded.Should().BeTrue(created.Describe());

                var contentManager = services.GetRequiredService<IContentManager>();
                var item = await contentManager.GetAsync(created.Value!.EmployeeId);

                item.Should().NotBeNull();

                var sections = EmployeeSections.All
                    .Select(section => item!.Get<BagPart>(section.Section))
                    .ToList();

                sections.Should().NotBeEmpty("this module defines sections, or the test is asserting nothing");

                sections.Should().OnlyContain(
                    section => section != null,
                    "every section is a BagPart, and a BagPart only resolves when OrchardCore.Flows is on. "
                    + "A null here is the defect this test exists for: the editor would show no sections, "
                    + "with no error anywhere.");
            }
        });
    }

    private async Task<IReadOnlyCollection<string>> EnabledFeatureIdsAsync()
    {
        var shellHost = _tenant.Services.GetRequiredService<IShellHost>();

        shellHost.TryGetSettings(BaseTenantFixture.TenantName, out var settings).Should().BeTrue();

        var scope = await shellHost.GetScopeAsync(settings!);
        var ids = new List<string>();

        await scope.UsingAsync(shellScope =>
        {
            // The blueprint is what Orchard actually built this shell from, which is the question —
            // not what the shell descriptor records as enabled, which is only what was asked for.
            ids.AddRange(shellScope.ShellContext.Blueprint.Dependencies
                .SelectMany(dependency => dependency.Value)
                .Select(feature => feature.Id)
                .Distinct(StringComparer.Ordinal));

            return Task.CompletedTask;
        });

        return ids;
    }
}
