using Microsoft.Extensions.Hosting.WindowsServices;
using WindowsControlService.Features.Authentication;
using WindowsControlService.Features.Health;
using WindowsControlService.Infrastructure.Database;
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
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddServiceInfrastructure();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddPlatform(builder.Configuration);

// 6. Features.
builder.Services.AddAuthenticationFeature(builder.Configuration);

var app = builder.Build();

// 7. Migrations before serving anything. Starting up on a half-applied schema is worse than
//    not starting at all.
app.Services.MigrateDatabase();

// Without UseExceptionHandler and AddProblemDetails an unhandled exception answers 500 with no
// body, which a client cannot tell apart from the service being down.
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapOpenApi("/openapi/{documentName}.yaml");
app.MapHealthEndpoints();
app.MapAuthenticationFeature();

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
