using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Hcm.Core",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Core HR: documents, letters, assets, announcements, air tickets, government data.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records", "WorkMate.Approvals" }
)]
