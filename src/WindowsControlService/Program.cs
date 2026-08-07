using Microsoft.Extensions.Hosting.WindowsServices;
using WindowsControlService.Features.AccessHistory;
using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Features.Authentication;
using WindowsControlService.Features.DeviceControl;
using WindowsControlService.Features.Events;
using WindowsControlService.Features.Health;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Events;
using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.Platform;

var builder = WebApplication.CreateBuilder(args);

// 1. Logging first, and before anything that could fail: if creating the data directory or
//    running the migrations is what breaks, the file sink may never get a chance to write.
var dataDirectory = builder.AddDataDirectory();
builder.ConfigureLogging(dataDirectory);

// 2. Hosting as a Windows service.
builder.Host.UseWindowsService(options => options.ServiceName = ServiceConstants.Name);

// 3. The constant is the default, not a decree: pinning the port at compile time would make it
//    impossible to bring up a second instance for testing. UseUrls outranks ASPNETCORE_URLS,
//    --urls and launchSettings, so this is the single place the address is decided.
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? ServiceConstants.DefaultUrl);

// 4. Above the worst case WDAC operation. A stop request that cuts a policy update in half
//    leaves the machine and the database disagreeing.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(70));

// 5. Cross-cutting.
// The API contract spells out "kind": "Logon" and "origin": "Remote". Without this they go out
// as the integers behind the enums, and a client would have to know the member order.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddServiceInfrastructure();
builder.Services.AddServiceEvents(builder.Configuration);
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddPlatform(builder.Configuration);

// 6. Features.
builder.Services.AddAuthenticationFeature(builder.Configuration);
builder.Services.AddApplicationBlocking(builder.Configuration);
builder.Services.AddDeviceControl();
builder.Services.AddAccessHistory(builder.Configuration);

var app = builder.Build();

// 7. Migrations before serving anything. Starting up on a half-applied schema is worse than
//    not starting at all.
app.Services.MigrateDatabase();

// Without UseExceptionHandler and AddProblemDetails an unhandled exception answers 500 with no
// body, which a client cannot tell apart from the service being down.
app.UseExceptionHandler();
app.UseRateLimiter();
// The shell is served before authentication on purpose: it carries no data, and every endpoint
// behind it still demands a session.
//
// no-cache rather than a version marker in the URLs. It does not mean "do not cache", it means
// "revalidate every time", and the middleware already sends an ETag: after update.ps1 replaces
// a file the browser gets it on the next request. A version marker would have to be kept in
// sync by hand and would go stale in exactly the update where it mattered.
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = static context => context.Context.Response.Headers.CacheControl = "no-cache",
    });
}
else if (app.Logger.IsEnabled(LogLevel.Warning))
{
    // Not a silent skip. A published service without its web root is a packaging fault, and the
    // path is the only thing that tells that apart from "the interface was never built".
    app.Logger.LogWarning(
        "Web root not found at {WebRootPath}. The API is up but the interface will not be served.",
        app.Environment.WebRootPath);
}
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapOpenApi("/openapi/{documentName}.yaml");
app.MapHealthEndpoints();
app.MapAuthenticationFeature();
app.MapApplicationBlocking();
app.MapDeviceControl();
app.MapEventStream();
app.MapAccessHistory();

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
