using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;

namespace WorkMate.Records.Navigation;

/// <summary>
/// The "People" admin menu group.
/// </summary>
/// <remarks>
/// Its own group rather than an entry under "Organisation", because the two answer different
/// questions: Organisation is the shape of the company and People is who is in it. They meet on the
/// placement screen and on the designer's cards, which is exactly where a reader expects them to.
///
/// The group will grow — the form designer's definitions land under it in session B — so it is
/// created in the shape it will keep rather than as a single link to be rearranged later.
/// </remarks>
public sealed class AdminMenu : INavigationProvider
{
    private readonly IStringLocalizer S;

    public AdminMenu(IStringLocalizer<AdminMenu> stringLocalizer) => S = stringLocalizer;

    public ValueTask BuildNavigationAsync(string name, NavigationBuilder builder)
    {
        if (!string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(S["People"], people => people
            .Add(S["Employees"], S["Employees"].PrefixPosition(), employees => employees
                .Action("Index", "EmployeesAdmin", new { area = "WorkMate.Records" })
                .Permission(Permissions.ViewEmployees)
                .LocalNav()));

        return ValueTask.CompletedTask;
    }
}
