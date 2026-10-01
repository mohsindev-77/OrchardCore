using Microsoft.Extensions.Options;
using OrchardCore.ResourceManagement;

namespace WorkMate.Platform.Shell;

/// <summary>
/// Registers WorkMate's shared admin stylesheet so that any module can ask for it by name with
/// <c>&lt;style asp-name="workmate-admin" /&gt;</c> rather than knowing a path.
/// </summary>
/// <remarks>
/// The stylesheet uses CSS logical properties — inline-start and inline-end rather than left and
/// right — so a single sheet is correct in both directions. Orchard Core 3.0.1 already switches
/// the admin shell's direction with the culture and ships a bootstrap-rtl resource, so there is
/// no second RTL stylesheet to keep in step.
/// </remarks>
public sealed class WorkMateResourceManifest : IConfigureOptions<ResourceManagementOptions>
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

        manifest.DefineStyle("workmate-admin")
            .SetUrl("~/WorkMate.Platform/css/workmate-admin.css")
            .SetVersion("1.0.0");

        return manifest;
    }
}
