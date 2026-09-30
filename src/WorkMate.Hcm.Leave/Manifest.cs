using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Hcm.Leave",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Leave types, policies, balances, accruals, TOIL and requests.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records", "WorkMate.Approvals" }
)]
