var builder = WebApplication.CreateBuilder(args);

// The host contains no business code. Modules register themselves as Orchard features.
builder.Services.AddOrchardCms();

var app = builder.Build();
app.UseOrchardCore();
app.Run();
