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
using WorkMate.Dimensions.Internal.Lookups;
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

        // The read side of each aggregate: an ISession is their only dependency, which is what
        // lets IDimensionValidator and the graph service depend on reads without depending on
        // the write services that in turn depend on the validator. See the module README for
        // the pattern.
        services.AddScoped<IDimensionTypeLookup, DimensionTypeLookup>();
        services.AddScoped<IStructureLookup, StructureLookup>();
        services.AddScoped<IDimensionRecordLookup, DimensionRecordLookup>();

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
        services.AddScoped<IEmployeeAssignmentService, EmployeeAssignmentService>();
        services.AddScoped<IDimensionService, DimensionService>();
        services.AddScoped<IDimensionValidator, DimensionValidator>();

        // This module's audit trail category and its three events, so an administrator can find
        // and filter them alongside Orchard's own.
        services.AddTransient<IConfigureOptions<AuditTrailOptions>, DimensionAuditTrailOptionsConfiguration>();
    }
}
