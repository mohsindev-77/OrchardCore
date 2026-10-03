using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Builds organisation structures on the shared tenant for the graph tests to work against.
/// </summary>
/// <remarks>
/// The three dimension types are created once for the whole run and shared, because creating
/// one generates an Orchard content type and that is the expensive part. Structures and records
/// are cheap, so each test makes its own with codes derived from its name — which also keeps
/// tests from seeing each other's data on a tenant they share.
/// </remarks>
public static class DimensionGraphScenario
{
    /// <summary>The three levels every scenario uses: a division, a department and a section.</summary>
    public sealed record Types(string Division, string Department, string Section);

    /// <summary>
    /// The shared dimension types, created on first use and found by code after that.
    /// </summary>
    /// <remarks>
    /// Resolved from the tenant every time rather than cached in a static. Caching was tried
    /// and is actively wrong here: a test that fails inside <c>InTenantAsync</c> rolls its shell
    /// scope back, so the types it created disappear from the database while the static still
    /// holds their ids — and every later test then fails with "there is no dimension type with
    /// the id ...", hiding the one real failure behind a dozen false ones.
    /// </remarks>
    public static async Task<Types> TypesAsync(IServiceProvider services)
    {
        var types = services.GetRequiredService<IDimensionTypeService>();

        return new Types(
            await CreateTypeAsync(types, "graph-division", allowsSelfNesting: false),
            await CreateTypeAsync(types, "graph-department", allowsSelfNesting: false),
            await CreateTypeAsync(types, "graph-section", allowsSelfNesting: true));
    }

    private static async Task<string> CreateTypeAsync(
        IDimensionTypeService types,
        string code,
        bool allowsSelfNesting)
    {
        var existing = await types.GetByCodeAsync(code);

        if (existing is not null)
        {
            return existing.DimensionTypeId;
        }

        var created = await types.CreateAsync(
            code,
            new BilingualText(code, $"{code}-ar"),
            [],
            allowsSelfNesting);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.DimensionTypeId;
    }

    /// <summary>Creates a structure with the three levels, root first.</summary>
    public static async Task<string> StructureAsync(IServiceProvider services, string code)
    {
        var types = await TypesAsync(services);

        var created = await services.GetRequiredService<IStructureService>().CreateAsync(
            code,
            new BilingualText(code, $"{code}-ar"),
            [types.Division, types.Department, types.Section],
            allowSkipLevel: true,
            isStrict: false,
            isPrimaryOrganisation: false);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.StructureId;
    }

    /// <summary>Creates a record of a type, open ended from <paramref name="from"/>.</summary>
    public static async Task<string> RecordAsync(
        IServiceProvider services,
        string dimensionTypeId,
        string code,
        DateOnly from)
    {
        var created = await services.GetRequiredService<IDimensionService>().CreateAsync(
            dimensionTypeId,
            code,
            new BilingualText(code, $"{code}-ar"),
            new EffectiveRange(from, null));

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.RecordId;
    }

    /// <summary>Renders a verification report with record codes, for a readable test failure.</summary>
    public static async Task<string> DescribeAsync(
        IServiceProvider services,
        WorkMate.Dimensions.Services.ClosureVerificationReport report)
    {
        var records = services.GetRequiredService<IDimensionService>();
        var lines = new List<string>();

        foreach (var divergence in report.Divergences.Take(20))
        {
            var ancestor = await records.GetAsync(divergence.AncestorId, new DateOnly(2024, 6, 1));
            var descendant = await records.GetAsync(divergence.DescendantId, new DateOnly(2024, 6, 1));

            lines.Add(
                divergence.Kind
                + " " + (ancestor?.Code ?? divergence.AncestorId)
                + "->" + (descendant?.Code ?? divergence.DescendantId)
                + " on " + divergence.AsAt);
        }

        return string.Join("; ", lines);
    }

    /// <summary>Runs work inside the tenant on the platform's own authority.</summary>
    public static Task InTenantAsSystemAsync(this BaseTenantFixture tenant, Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("graph integration test"))
            {
                await work(services);
            }
        });
}
