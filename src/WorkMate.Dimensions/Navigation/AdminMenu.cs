using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;

namespace WorkMate.Dimensions.Navigation;

/// <summary>
/// The "Organisation" admin menu group: dimension types, structures and the organisation
/// designer. One provider for all three, added to as each screen lands, so the group exists in
/// its final shape from the first screen rather than being rebuilt around it later.
/// </summary>
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

        builder.Add(S["Organisation"], organisation => organisation
            .Add(S["Dimension types"], S["Dimension types"].PrefixPosition(), types => types
                .Action("Index", "DimensionTypesAdmin", new { area = "WorkMate.Dimensions" })
                .Permission(Permissions.ManageDimensionTypes)
                .LocalNav()));

        return ValueTask.CompletedTask;
    }
}
