using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "WorkMate.Records",
    Author = "Aramis Enterprise Solutions",
    Version = "0.1.0",
    Description = "The employee record and the form designer built on runtime content-type definition.",
    Category = "WorkMate",
    // Every Orchard feature this module needs at runtime, declared here rather than only in the
    // base recipe. The two are not interchangeable: a recipe is applied once, at setup, so adding
    // a feature to it reaches tenants created afterwards and no others, while a manifest dependency
    // is resolved every time the shell is built and therefore reaches existing tenants too.
    //
    // OrchardCore.Flows was the one that proved it. The employee's sections are BagParts, Flows was
    // added to the base recipe in session A1, and on a tenant created before that the sections
    // simply did not work — no error, because an unregistered part is not a broken part, it is a
    // part Orchard has never heard of. OrchardCore.Title supplies the title part the Employee type
    // binds to the generated display name; OrchardCore.ContentFields supplies the picker field the
    // line manager uses; OrchardCore.AuditTrail is where this module's three events are recorded,
    // which specification section 5 requires; OrchardCore.Deployment is what renders the export
    // step. Contents and ContentTypes arrive through WorkMate.Dimensions, which declares them.
    Dependencies = new[]
    {
        "WorkMate.Platform",
        "WorkMate.Dimensions",
        "OrchardCore.Flows",
        "OrchardCore.Title",
        "OrchardCore.ContentFields",
        "OrchardCore.AuditTrail",
        "OrchardCore.Deployment",
        "OrchardCore.Media",
    }
)]
