using OrchardCore.Modules.Manifest;

[assembly: Module(
    Id = "WorkMate.Platform",
    Name = "WorkMate Platform",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Tenancy, site settings, roles, cultures, base recipe and the shared UI shell.",
    Category = "WorkMate",
    // Declared here rather than only in the base recipe, because the two reach different tenants:
    // a recipe is applied once at setup, so a feature added to it arrives on tenants created after
    // the edit and on no others, while a manifest dependency is resolved every time the shell is
    // built. Every module in this repository depends on this one, so a feature declared here is a
    // feature every WorkMate tenant has.
    //
    // OrchardCore.Contents and OrchardCore.ContentTypes carry the content item machinery and the
    // content-definition editor that BilingualTextField and its settings driver plug into: without
    // the latter the field's settings simply do not render, with no error, because a driver nothing
    // asks for is not a failure.
    Dependencies =
    [
        "OrchardCore.Settings",
        "OrchardCore.Roles",
        "OrchardCore.Localization",
        "OrchardCore.Admin",
        "OrchardCore.Navigation",
        "OrchardCore.Resources",
        "OrchardCore.Contents",
        "OrchardCore.ContentTypes",

        // Not used by this module directly, and required all the same. OrchardCore.Contents
        // registers a data-localization provider that takes a dependency on OrchardCore.AdminMenu,
        // and OrchardCore.DataLocalization — which the base recipe enables so that content type
        // names can be translated — resolves every such provider. With AdminMenu off, the one from
        // Contents cannot be constructed, so resolving that collection throws, and every screen
        // whose display pipeline touches it fails with a dependency-injection error naming three
        // features and no cause. The deployment plan screen is the one that found it.
        "OrchardCore.AdminMenu",
    ]
)]
