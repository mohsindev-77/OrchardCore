using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Entitlement",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Entitlement record, capability checks, headcount licensing and the control plane.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform" }
)]
