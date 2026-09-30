using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;
using WorkMate.Platform.Drivers;

namespace WorkMate.Platform.Navigation;

/// <summary>
/// Puts the platform settings editor under Configuration in the admin menu. The link is
/// hidden from anyone without <see cref="Permissions.ManageWorkMateSettings"/>.
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

        builder.Add(S["Configuration"], configuration => configuration
            .Add(S["Settings"], settings => settings
                .Add(S["WorkMate"], S["WorkMate"].PrefixPosition(), workMate => workMate
                    .Action("Index", "Admin", new
                    {
                        area = "OrchardCore.Settings",
                        groupId = WorkMateSettingsDisplayDriver.GroupId,
                    })
                    .Permission(Permissions.ManageWorkMateSettings)
                    .LocalNav())));

        return ValueTask.CompletedTask;
    }
}
