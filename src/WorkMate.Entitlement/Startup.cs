using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Modules;

namespace WorkMate.Entitlement;

public sealed class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // Register services, parts, drivers, handlers, indexes and recipe steps here.
        // See /docs/technical-specification.md for what this module owns.
    }
}
