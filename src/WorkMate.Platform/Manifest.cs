using OrchardCore.Modules.Manifest;

[assembly: Module(
    Id = "WorkMate.Platform",
    Name = "WorkMate Platform",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Tenancy, site settings, roles, cultures, base recipe and the shared UI shell.",
    Category = "WorkMate",
    Dependencies =
    [
        "OrchardCore.Settings",
        "OrchardCore.Roles",
        "OrchardCore.Localization",
        "OrchardCore.Admin",
        "OrchardCore.Navigation",
        "OrchardCore.Resources",
    ]
)]
