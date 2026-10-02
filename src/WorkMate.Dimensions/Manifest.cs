using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Dimensions",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "The dimension engine: dimension types, structures, records, links, closure index and employee assignments.",
    Category = "WorkMate",
    // OrchardCore.Contents and OrchardCore.ContentTypes carry the content item and content
    // definition machinery a dimension record is built on. OrchardCore.Title supplies the title
    // part every generated type binds to its English name. OrchardCore.ContentFields supplies the
    // field types an attribute schema may declare. OrchardCore.AuditTrail is where every
    // structure and content-definition change is recorded, which specification section 4 requires
    // rather than merely suggests, so it is a dependency and not an optional extra.
    Dependencies = new[]
    {
        "WorkMate.Platform",
        "OrchardCore.Contents",
        "OrchardCore.ContentTypes",
        "OrchardCore.Title",
        "OrchardCore.ContentFields",
        "OrchardCore.AuditTrail",
    }
)]
