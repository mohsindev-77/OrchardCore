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
/// <b>Two entries, and the product owner has ruled that is all the form designer gets.</b>
/// Everything else the designer needs — lookup lists, the reconciliation screen, version history —
/// is reached from the Forms screen or from the employee editor, not from here. The dimension
/// engine's menu carries the account of what happens when configuration is enumerated in a
/// navigation menu: a fourth entry was tried, built from the tenant's own types, and read
/// <em>Branchs, Cost centres, Departments, Departments, Departments, …</em> on a real tenant. A
/// menu is a short list somebody scans.
///
/// <b>Positions are explicit numbers, not <c>PrefixPosition()</c>.</b> This changed when the second
/// entry arrived, and the second entry is what made it matter. <c>LocalizedString.PrefixPosition()</c>
/// orders by the caption in whatever language the reader is in: in English "Employees" sorts before
/// "Forms", and in Arabic "النماذج" sorts before "الموظفون" — so the same menu came out in a
/// different order for an Arabic reader, for a menu whose order is a product decision rather than
/// an alphabetical accident. <c>WorkMate.Dimensions.Navigation.AdminMenu</c> found this first and
/// records it; <c>PeopleMenuTenantTests</c> asserts the order in both languages here.
/// </remarks>
public sealed class AdminMenu : INavigationProvider
{
    /// <summary>
    /// Employees first: it is what somebody comes to this group to do. Forms second, because it is
    /// how the employee record is configured — the same ordering the Organisation group uses, where
    /// the chart comes before the two screens that shape it.
    /// </summary>
    private const string EmployeesPosition = "1";

    private const string FormsPosition = "2";

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
            .Add(S["Employees"], EmployeesPosition, employees => employees
                .Action("Index", "EmployeesAdmin", new { area = "WorkMate.Records" })
                .Permission(Permissions.ViewEmployees)
                .LocalNav())
            .Add(S["Forms"], FormsPosition, forms => forms
                .Action("Index", "FormsAdmin", new { area = "WorkMate.Records" })
                // Gated on what is behind it today. Slice B4 adds the form definitions themselves
                // and will widen this to whichever permission covers both; until then an entry
                // offered to somebody who can reach nothing through it would be a menu item that
                // leads to a 403.
                .Permission(Permissions.ManageLookupLists)
                .LocalNav()));

        return ValueTask.CompletedTask;
    }
}
