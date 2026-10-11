using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;

namespace WorkMate.Dimensions.Navigation;

/// <summary>
/// The "Organisation" admin menu group: the designer, dimension types and structures.
/// </summary>
/// <remarks>
/// <b>Three entries, and deliberately no more.</b> A fourth was tried and withdrawn: one entry per
/// dimension type, built from the tenant's own configuration. It worked, and on a real tenant it
/// read <em>Branchs, Cost centres, Departments, Departments, Departments, Divisions, Divisions,
/// Divisions, Projects, Regions, Sections, Teams</em> — because several structures legitimately use
/// types of the same name, and because no rule pluralises an English noun correctly. A navigation
/// menu is a short list somebody scans; it is the wrong place to enumerate configuration.
///
/// A type's units are reached from the Dimension types list instead, by the <b>View units</b> link
/// on each row — one click further in, and in the place where somebody is already looking at the
/// list of types.
///
/// <b>Positions are explicit numbers.</b> The obvious alternative,
/// <c>LocalizedString.PrefixPosition()</c>, orders by the caption in whatever language the reader is
/// in — so an Arabic reader got a different order from an English one, for a menu whose order is a
/// product decision rather than an alphabetical accident. <c>OrganisationMenuTenantTests</c> asserts
/// the order in both languages.
/// </remarks>
public sealed class AdminMenu : INavigationProvider
{
    private readonly IStringLocalizer S;

    public AdminMenu(IStringLocalizer<AdminMenu> stringLocalizer) => S = stringLocalizer;

    /// <summary>
    /// The chart first: it is what somebody comes to this group to look at. The two configuration
    /// screens follow, in the order a tenant is built — types, then the structures over them.
    /// </summary>
    private const string DesignerPosition = "1";

    private const string TypesPosition = "2";

    private const string StructuresPosition = "3";

    public ValueTask BuildNavigationAsync(string name, NavigationBuilder builder)
    {
        if (!string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(S["Organisation"], organisation => organisation
            .Add(S["Organisation designer"], DesignerPosition, designer => designer
                .Action("Index", "OrganisationDesignerAdmin", new { area = "WorkMate.Dimensions" })
                .Permission(Permissions.ManageDimensionRecords)
                .LocalNav())
            .Add(S["Dimension types"], TypesPosition, types => types
                .Action("Index", "DimensionTypesAdmin", new { area = "WorkMate.Dimensions" })
                .Permission(Permissions.ManageDimensionTypes)
                .LocalNav())
            .Add(S["Structures"], StructuresPosition, structures => structures
                .Action("Index", "StructuresAdmin", new { area = "WorkMate.Dimensions" })
                .Permission(Permissions.ManageStructures)
                .LocalNav()));

        return ValueTask.CompletedTask;
    }
}
