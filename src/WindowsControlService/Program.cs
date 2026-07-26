using Microsoft.Extensions.Hosting.WindowsServices;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging first, and before anything that could fail: if creating the data directory or
// running the migrations is what breaks, the file sink may never get a chance to write.
var dataDirectory = builder.AddDataDirectory();
builder.ConfigureLogging(dataDirectory);

builder.Services.AddSingleton<ISequentialExecutor, SequentialExecutor>();
builder.Services.AddDatabase(builder.Configuration);

var app = builder.Build();

// Before serving anything. Starting up on a half-applied schema is worse than not starting.
app.Services.MigrateDatabase();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ServiceConstants.Name);
if (logger.IsEnabled(LogLevel.Information))
{
    logger.LogInformation(
        "{ServiceName} starting. Data directory: {DataDirectory}",
        ServiceConstants.Name,
        dataDirectory.Path);
}

if (!WindowsServiceHelpers.IsWindowsService())
{
    // Worth an operator's attention: outside the service, WDAC and registry work runs with
    // whatever rights the current user happens to have instead of LocalSystem's.
    logger.LogWarning(
        "Running interactively, not as the {ServiceName} Windows service. Privileged operations "
        + "will use the current user's rights.",
        ServiceConstants.Name);
}

app.Run();

/// <summary>Entry point, made accessible so the integration tests can host it.</summary>
public partial class Program;
