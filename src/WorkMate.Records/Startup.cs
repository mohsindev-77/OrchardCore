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

        // The managed lookup lists a choice field offers. Documents of ours rather than Orchard
        // taxonomies, per ADR-0014 — read it before changing any of this.
        services.AddIndexProvider<LookupListIndexProvider>();
        services.AddScoped<ILookupListService, LookupListService>();

        // The read side, cached per tenant. The concrete uncached lookup is registered as itself
        // so the decorator can take it, exactly as WorkMate.Dimensions registers its own pair —
        // and so that a write path, which must not read through a cache, has something to take.
        services.AddScoped<LookupListLookup>();
        services.AddScoped<ILookupListLookup, CachedLookupListLookup>();

        // What a bound field offers, asked by the tag helper, the alternate editor view and the
        // reconciliation screen alike.
        services.AddScoped<ILookupChoiceProvider, LookupChoiceProvider>();

        // The Load standard lists button on People > Forms. It runs this module's own recipe rather
        // than holding a second copy of the entries, so a tenant seeded at setup and one seeded
        // afterwards get the same lists from the same file.
        services.AddScoped<IStandardLookupListsLoader, StandardLookupListsLoader>();

        // Finding the values records hold that are in no list, and mapping them.
        services.AddScoped<ILookupValueReconciliation, LookupValueReconciliation>();

        // This module's answer to the dimension engine's question "who is this employee". Declared
        // there, implemented here, because that module needs the answer and must not depend on the
        // module that holds it — the same shape as IDimensionDeletionBlockerProvider.
        services.AddScoped<IEmployeeLookup, EmployeeLookup>();

        // The other two seams another module declared and this one answers: the shared component
        // set's employee picker, and the candidate set behind an employee picker field.
        services.AddScoped<EmployeeUnitNames>();

        // The words for a broken split rule. A service rather than a static helper so that the
        // rules themselves — PlacementSplit — stay a pure function with no localiser in them and
        // every case stays a unit test.
        services.AddScoped<PlacementSplitMessages>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<IContentPickerResultProvider, EmployeePickerResultProvider>();

        // Where an employee sits, derived from the position they hold (D30). One read behind the
        // Create form, the employee page, the Placement screen and the Change-position form, so
        // four screens cannot compose it four slightly different ways.
        services.AddScoped<IEmployeeOrganisationService, EmployeeOrganisationService>();

        // The one-time repair behind Migrations.UpdateFrom5Async (D31 addendum 2). A service rather
        // than a body inside the step, because a shipped step is never edited and so the logic it
        // calls has to be somewhere a test can run twice and assert counts on.
        services.AddScoped<ILineManagerBackfill, LineManagerBackfill>();

        // The one place the product says "this position changed — look at the access roles" (D26 I).
        // A class rather than three copies of a string, so the security slice has one thing to
        // replace when the notice gains somewhere to link to.
        services.AddScoped<AccessRoleReminder>();

        // The sections THIS TENANT has, read from its content definition rather than from the six
        // the product ships. The employee page renders its fixed core and then loops over this, so
        // a seventh section arriving with the form designer in B3 appears without anybody editing a
        // view — the design note's §9.2 Rule 2.
        services.AddScoped<IEmployeeSectionCatalogue, EmployeeSectionCatalogue>();

        // This module's audit trail category and its three events, so an administrator can find and
        // filter them alongside Orchard's own.
        services.AddTransient<IConfigureOptions<AuditTrailOptions>, RecordsAuditTrailOptionsConfiguration>();

        // And the form configuration category beside it. A second category rather than more events
        // in the first: changing a lookup list changes what every record may say, which is a
        // different question from what any one of them does say. See FormsAuditTrail.
        services.AddTransient<IConfigureOptions<AuditTrailOptions>, FormsAuditTrailOptionsConfiguration>();

        // The lists, before anything that names a value from one.
        services.AddRecipeExecutionStep<LookupListsRecipeStep>();

        // People, by code — the step the dimension engine's employee-assignments and unit-heads
        // steps resolve their references against, so a recipe lists this one before either of
        // them.
        services.AddRecipeExecutionStep<EmployeesRecipeStep>();

        // And the export side of it. ADR-0011, the same shape the dimension engine's export takes:
        // a deployment step producing the recipe step the importer already reads.
        services.AddDeployment<RecordsDeploymentSource, RecordsDeploymentStep, RecordsDeploymentStepDriver>();
    }
}
