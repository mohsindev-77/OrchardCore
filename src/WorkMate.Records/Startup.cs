using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.Data;
using OrchardCore.Data.Migration;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Security.Permissions;
using WorkMate.Dimensions.Services;
using OrchardCore.Deployment;
using OrchardCore.Recipes;
using WorkMate.Records.Deployment;
using WorkMate.Records.Handlers;
using WorkMate.Records.Recipes;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Navigation;
using WorkMate.Records.Models;
using WorkMate.Records.Services;

namespace WorkMate.Records;

public sealed class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddDataMigration<Migrations>();
        services.AddPermissionProvider<Permissions>();
        services.AddNavigationProvider<AdminMenu>();

        services.AddScoped<IRecordsAuthorisation, RecordsAuthorisation>();

        // The fixed core. Registered as a part so Orchard can materialise it from a content item's
        // JSON; its display driver lands in A2 with the screens.
        services.AddContentPart<EmployeePart>();
        services.AddIndexProvider<EmployeeIndexProvider>();

        // The invariants run at the item level, not the part level. A ContentPartHandler's
        // validation context carries its own result object that the caller never sees, so a rule
        // written there compiles, runs and silently rejects nothing — see EmployeeHandler, and
        // DimensionRecordHandler, which carries the full account of the trap.
        services.AddScoped<IContentHandler, EmployeeHandler>();

        services.AddScoped<IEmployeeService, EmployeeService>();

        // This module's answer to the dimension engine's question "who is this employee". Declared
        // there, implemented here, because that module needs the answer and must not depend on the
        // module that holds it — the same shape as IDimensionDeletionBlockerProvider.
        services.AddScoped<IEmployeeLookup, EmployeeLookup>();

        // The other two seams another module declared and this one answers: the shared component
        // set's employee picker, and the candidate set behind an employee picker field.
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<IContentPickerResultProvider, EmployeePickerResultProvider>();

        // This module's audit trail category and its three events, so an administrator can find and
        // filter them alongside Orchard's own.
        services.AddTransient<IConfigureOptions<AuditTrailOptions>, RecordsAuditTrailOptionsConfiguration>();

        // People, by code — the step the dimension engine's employee-assignments and unit-heads
        // steps resolve their references against, so a recipe lists this one before either of
        // them.
        services.AddRecipeExecutionStep<EmployeesRecipeStep>();

        // And the export side of it. ADR-0011, the same shape the dimension engine's export takes:
        // a deployment step producing the recipe step the importer already reads.
        services.AddDeployment<RecordsDeploymentSource, RecordsDeploymentStep, RecordsDeploymentStepDriver>();
    }
}
