var builder = WebApplication.CreateBuilder(args);

// The host contains no business code. Modules register themselves as Orchard features.
builder.Services.AddOrchardCms();

var app = builder.Build();
app.UseOrchardCore();
app.Run();

/// <summary>
/// Named so that WorkMate.Integration.Tests can start the real host through
/// WebApplicationFactory. Top-level statements generate an internal Program, which a test project
/// cannot reach. This is not business code: it adds no behaviour, only a name.
/// </summary>
public partial class Program;
