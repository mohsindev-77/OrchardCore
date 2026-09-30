using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Reporting",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Standard report library, report builder and dashboards.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records" }
)]
