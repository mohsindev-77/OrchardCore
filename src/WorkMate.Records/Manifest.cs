using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Records",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "The employee record and the form designer built on runtime content-type definition.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions" }
)]
