using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Hcm.Payroll",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Payroll setups, the formula engine, run lifecycle, cost allocation, GL and statutory output.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records", "WorkMate.Approvals", "WorkMate.Hcm.Leave", "WorkMate.Hcm.Attendance" }
)]
