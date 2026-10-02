using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.Data;
using OrchardCore.Data.Migration;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.Modules;
using OrchardCore.Security.Permissions;
using WorkMate.Dimensions.Drivers;
using WorkMate.Dimensions.Handlers;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions;

public sealed class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddDataMigration<Migrations>();
        services.AddPermissionProvider<Permissions>();

        // Configuration layer: the dimension types and the structures they are levels of.
        services.AddIndexProvider<DimensionTypeIndexProvider>();
        services.AddIndexProvider<StructureIndexProvider>();

        services.AddScoped<IDimensionAuthorisation, DimensionAuthorisation>();
        services.AddScoped<IDimensionTypeService, DimensionTypeService>();
        services.AddScoped<IStructureService, StructureService>();

        // Record layer: the part every generated dimension content type carries, and the dated
        // name history behind corrective and substantive renames.
        services.AddContentPart<DimensionRecordPart>()
            .UseDisplayDriver<DimensionRecordPartDisplayDriver>()
            .AddHandler<DimensionRecordPartHandler>();

        services.AddIndexProvider<DimensionRecordPartIndexProvider>();
        services.AddIndexProvider<DimensionNameIndexProvider>();

        // This module's audit trail category and its three events, so an administrator can find
        // and filter them alongside Orchard's own.
        services.AddTransient<IConfigureOptions<AuditTrailOptions>, DimensionAuditTrailOptionsConfiguration>();
    }
}
