using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.Data;
using OrchardCore.Data.Migration;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.Modules;
using OrchardCore.Security.Permissions;
using WorkMate.Dimensions.Drivers;
using WorkMate.Dimensions.Handlers;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Internal.Graph;
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

        // The invariants run at the item level, not the part level: a part handler's validation
        // context carries its own result object that the caller never sees, so failures
        // recorded there are lost. DimensionRecordHandler explains it in full.
        services.AddScoped<DimensionRecordPartHandler>();
        services.AddScoped<IContentHandler, DimensionRecordHandler>();

        services.AddIndexProvider<DimensionRecordPartIndexProvider>();
        services.AddIndexProvider<DimensionNameIndexProvider>();

        // Graph layer: the link, closure and assignment tables, and the service that owns them.
        // The tables are internal by design � nothing outside this module reads them � so only
        // the interface is registered publicly.
        services.AddIndexProvider<DimensionLinkIndexProvider>();
        services.AddIndexProvider<DimensionClosureIndexProvider>();
        services.AddIndexProvider<EmployeeAssignmentIndexProvider>();

        services.AddScoped<IDimensionGraphService, DimensionGraphService>();

        // StructureService announces a level change to the graph, and the graph reads structure
        // configuration: a real cycle. Resolving one side on first use breaks it without
        // letting either service reach into the other's tables. See the remark on
        // StructureService for why the dependency genuinely runs both ways.
        services.AddScoped(provider =>
            new Lazy<IDimensionGraphService>(provider.GetRequiredService<IDimensionGraphService>));
        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
        services.AddScoped<IDimensionService, DimensionService>();

        // This module's audit trail category and its three events, so an administrator can find
        // and filter them alongside Orchard's own.
        services.AddTransient<IConfigureOptions<AuditTrailOptions>, DimensionAuditTrailOptionsConfiguration>();
    }
}
