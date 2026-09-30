using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Approvals",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Approval definitions compiled to Orchard workflows; inbox, delegation, escalation.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records" }
)]
