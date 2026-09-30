using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Hcm.SelfService",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Employee and manager web surfaces and the mobile API.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records", "WorkMate.Approvals", "WorkMate.Hcm.Leave", "WorkMate.Hcm.Attendance", "WorkMate.Hcm.Payroll" }
)]
