using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Data.Migration;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Security.Permissions;
using WorkMate.Platform.Drivers;
using WorkMate.Platform.Navigation;
using WorkMate.Platform.Services;

namespace WorkMate.Platform;

public sealed class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddDataMigration<Migrations>();
        services.AddPermissionProvider<Permissions>();
        services.AddNavigationProvider<AdminMenu>();

        services.AddScoped<IWorkMateSettingsService, WorkMateSettingsService>();
        services.AddSiteDisplayDriver<WorkMateSettingsDisplayDriver>();
    }
}
