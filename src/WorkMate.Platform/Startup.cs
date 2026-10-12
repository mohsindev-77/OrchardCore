using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrchardCore.Admin.Models;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.ContentTypes.Editors;
using OrchardCore.Data.Migration;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.ResourceManagement;
using OrchardCore.Security.Permissions;
using WorkMate.Platform.Components;
using WorkMate.Platform.Drivers;
using WorkMate.Platform.Fields;
using WorkMate.Platform.Logging;
using WorkMate.Platform.Navigation;
using WorkMate.Platform.Services;
using WorkMate.Platform.Shell;

namespace WorkMate.Platform;

public sealed class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddDataMigration<Migrations>();
        services.AddPermissionProvider<Permissions>();
        services.AddNavigationProvider<AdminMenu>();

        services.AddScoped<IWorkMateSettingsService, WorkMateSettingsService>();
        services.AddScoped<IBilingualNamePolicy, BilingualNamePolicy>();

        // Who may see which employees. A stub that allows everything until specification section 9's
        // visibility by node is built in prompt 9 — declared and wired now so that every candidate
        // set in the product already goes through it, and prompt 9 has nothing to go and find. See
        // IVisibilityService for why a permissive stub is the right one.
        services.AddScoped<IVisibilityService, AllowAllVisibilityService>();

        // Whether a capability is entitled on this tenant. The same shape and the same reasoning as
        // the line above: a stub that allows everything until prompt 8 builds the signed entitlement
        // record specification section 8 describes, declared and called now so that every service
        // already asks and prompt 8 has nothing to go and find. The seam is here rather than in
        // WorkMate.Entitlement because nothing depends on that module and everything depends on this
        // one. See IEntitlementService.
        services.AddScoped<IEntitlementService, AllowAllEntitlementService>();

        // System authority: the explicit opt-in a recipe step or background task enters when it
        // genuinely runs as the platform. Scoped, so it cannot outlive the shell scope that
        // entered it. Every module's services consult it, which is why it lives here.
        services.AddScoped<ISystemOperation, SystemOperation>();
        services.AddSiteDisplayDriver<WorkMateSettingsDisplayDriver>();

        // The bilingual field, per ADR-0003: the value object is in WorkMate.Core, the Orchard
        // field and its drivers are here.
        services.AddContentField<BilingualTextField>()
            .UseDisplayDriver<BilingualTextFieldDisplayDriver>();

        services.AddScoped<IContentPartFieldDefinitionDisplayDriver, BilingualTextFieldSettingsDriver>();

        // The shared shell: the admin stylesheet and WorkMate's navbar chrome.
        services.AddTransient<IConfigureOptions<ResourceManagementOptions>, WorkMateResourceManifest>();
        services.AddDisplayDriver<Navbar, WorkMateNavbarDisplayDriver>();

        // Structured logging with tenant, user and correlation id on every line.
        services.AddScoped<IWorkMateLogScope, WorkMateLogScope>();
        services.AddScoped<WorkMateLogScopeMiddleware>();
    }

    public override void Configure(IApplicationBuilder app, IEndpointRouteBuilder routes, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(app);

        // First in this module's pipeline, so that everything logged during the request — this
        // module's code and Orchard's alike — carries the scope.
        app.UseMiddleware<WorkMateLogScopeMiddleware>();
    }

    /// <summary>
    /// Ahead of the default so the logging scope is opened before other modules' middleware runs.
    /// </summary>
    public override int ConfigureOrder => -100;
}
