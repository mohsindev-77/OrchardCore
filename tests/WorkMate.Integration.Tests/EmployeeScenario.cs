using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Records.Services;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Creates employees on the shared tenant for the record, lifecycle and head tests to work against.
/// </summary>
/// <remarks>
/// The same shape as <see cref="DimensionGraphScenario"/>, and resolved from the tenant each time
/// rather than cached in a static for the reason recorded there: a test that fails inside
/// <c>InTenantAsync</c> rolls its shell scope back, so anything it created disappears from the
/// database while a static would still hold its id — and every later test then fails looking for
/// it, hiding the one real failure behind a dozen false ones.
/// </remarks>
public static class EmployeeScenario
{
    /// <summary>The join date every scenario employee starts from, unless a test needs another.</summary>
    public static readonly DateOnly Joined = new(2024, 1, 1);

    /// <summary>
    /// Creates an employee with a bilingual name derived from their code, and activates them.
    /// </summary>
    /// <remarks>
    /// Activated, because almost every test that wants an employee wants one who is working: a
    /// prospective employee cannot be exited into a meaningful state and is not what a head
    /// appointment or a placement is normally about. A test about the prospective state itself calls
    /// <see cref="CreateAsync"/> and stops there.
    /// </remarks>
    public static async Task<string> ActiveEmployeeAsync(
        IServiceProvider services,
        string code,
        DateOnly? joined = null,
        string? nameEn = null,
        string? nameAr = null)
    {
        var employees = services.GetRequiredService<IEmployeeService>();
        var from = joined ?? Joined;

        var id = await CreateAsync(services, code, from, nameEn, nameAr);
        var activated = await employees.ActivateAsync(id, from);

        activated.Succeeded.Should().BeTrue(activated.Describe());

        return id;
    }

    /// <summary>Creates a prospective employee and returns their content item id.</summary>
    public static async Task<string> CreateAsync(
        IServiceProvider services,
        string code,
        DateOnly? joined = null,
        string? nameEn = null,
        string? nameAr = null)
    {
        var employees = services.GetRequiredService<IEmployeeService>();

        var existing = await employees.GetByCodeAsync(code);

        if (existing is not null)
        {
            return existing.EmployeeId;
        }

        var created = await employees.CreateAsync(
            code,
            new BilingualText(nameEn ?? code, nameAr ?? $"{code}-ar"),
            joined ?? Joined);

        created.Succeeded.Should().BeTrue(created.Describe());

        return created.Value!.EmployeeId;
    }
}
