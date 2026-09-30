using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Hcm.Attendance",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "Shifts, rosters, device integration, daily processing and corrections.",
    Category = "WorkMate",
    Dependencies = new[] { "WorkMate.Platform", "WorkMate.Dimensions", "WorkMate.Records", "WorkMate.Approvals" }
)]
