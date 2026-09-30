using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Dimensions",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "The dimension engine: dimension types, structures, records, links, closure index and employee assignments.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform" }
)]
