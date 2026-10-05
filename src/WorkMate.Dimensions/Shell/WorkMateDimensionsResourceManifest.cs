using Microsoft.Extensions.Options;
using OrchardCore.ResourceManagement;

namespace WorkMate.Dimensions.Shell;

/// <summary>
/// Registers this module's admin scripts and stylesheet with Orchard's resource manager, so views
/// ask for them by name and Orchard decides the URL.
/// </summary>
/// <remarks>
/// This is not tidiness. A plain <c>&lt;script src="~/WorkMate.Dimensions/js/..."&gt;</c> is served
/// by the static file middleware with <c>cache-control: public, max-age=2592000</c> at a URL that
/// never changes, so a browser that has fetched it once will not ask for it again for thirty days
/// — not even a conditional request. Rebuilding, restarting, or deploying changes nothing for that
/// browser: it keeps running the old script against freshly rendered HTML, which looks exactly
/// like a screen whose server code is correct and whose every control is dead.
///
/// Going through the resource manager makes Orchard append a content hash to the URL
/// (<c>?v=...</c>, the same treatment its own scripts get), so changing a file changes its URL and
/// the browser has no choice but to fetch it. The long cache lifetime then becomes a feature
/// rather than a trap.
///
/// Pinned by <c>RealDataDesignerBrowserTests.TheDesignersScriptAndStylesheetCarryAVersionToken…</c>
/// and by <c>DesignerAssetsAreVersionedTenantTests</c>, which fail if any WorkMate asset ever goes
/// back to being referenced by a bare path.
/// </remarks>
public sealed class WorkMateDimensionsResourceManifest : IConfigureOptions<ResourceManagementOptions>
{
    private static readonly ResourceManifest Manifest = Build();

    public void Configure(ResourceManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ResourceManifests.Add(Manifest);
    }

    private static ResourceManifest Build()
    {
        var manifest = new ResourceManifest();

        manifest.DefineStyle("workmate-organisation-designer")
            .SetUrl("~/WorkMate.Dimensions/css/organisation-designer.css")
            .SetVersion("1.0.0");

        manifest.DefineScript("workmate-organisation-designer")
            .SetUrl("~/WorkMate.Dimensions/js/organisation-designer.js")
            .SetVersion("1.0.0");

        manifest.DefineScript("workmate-dimension-types")
            .SetUrl("~/WorkMate.Dimensions/js/dimension-types.js")
            .SetVersion("1.0.0");

        manifest.DefineScript("workmate-structures")
            .SetUrl("~/WorkMate.Dimensions/js/structures.js")
            .SetVersion("1.0.0");

        return manifest;
    }
}
