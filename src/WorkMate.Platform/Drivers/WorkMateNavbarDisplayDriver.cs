using OrchardCore.Admin.Models;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;

namespace WorkMate.Platform.Drivers;

/// <summary>
/// Puts WorkMate's chrome into the admin navbar: at present the approval badge, which section 3
/// asks for in the header.
/// </summary>
/// <remarks>
/// This is the extension point rather than a layout override on purpose. TheAdmin's views are
/// compiled into its assembly and, in Orchard, a theme's templates outrank a module's, so a
/// module cannot replace the admin Layout. Orchard Core 3.0.1 provides a Navbar model that
/// modules contribute to through a display driver — OrchardCore.Admin, OrchardCore.Localization
/// and OrchardCore.Notifications all add their navbar items this way — so WorkMate uses the same
/// door as everything else rather than forcing one.
///
/// The count is not wired up: WorkMate.Approvals owns pending tasks and does not exist yet. The
/// badge renders in its empty state, which is honest, and the driver moves to the approvals
/// module when there is something to count.
/// </remarks>
public sealed class WorkMateNavbarDisplayDriver : DisplayDriver<Navbar>
{
    public override IDisplayResult Display(Navbar model, BuildDisplayContext context) =>
        View("WorkMateApprovalBadge", model)
            .Location("DetailAdmin", "Content:8");
}
