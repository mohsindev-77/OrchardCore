using OrchardCore.Logging;

var builder = WebApplication.CreateBuilder(args);

// File logging, additive to the console provider appsettings.json already configures: an error
// an operator cannot read off a scrolled-away console still lands under App_Data/logs. Orchard's
// own extension for this — see nlog.config for the file target, which stays file-only so console
// output is not duplicated.
builder.Host.UseNLogHost();

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
