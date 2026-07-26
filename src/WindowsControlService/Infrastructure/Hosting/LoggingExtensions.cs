using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace WindowsControlService.Infrastructure.Hosting;

public static class LoggingExtensions
{
    public const string LogFolderName = "logs";

    private const string FileOutputTemplate =
        "{UtcTimestamp:yyyy-MM-dd HH:mm:ss.fff}Z [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private const string EventLogOutputTemplate =
        "{Message:lj}{NewLine}{NewLine}Source context: {SourceContext}{NewLine}UTC: {UtcTimestamp:o}{NewLine}{Exception}";

    /// <summary>
    /// Two destinations: a rolling file in the data directory, and the Windows Event Log from
    /// <see cref="LogEventLevel.Warning"/> up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are Serilog sinks, and that is not a stylistic choice. Registering Serilog installs
    /// its own <c>ILoggerFactory</c>, which does not forward to the other logging providers in
    /// the container. Adding the Event Log through <c>ILoggingBuilder.AddEventLog()</c> -- the
    /// obvious route, and the one <c>UseWindowsService()</c> takes -- therefore registers a
    /// provider that never gets consulted. It fails silently: the file sink keeps working and
    /// Event Viewer keeps showing "Service started successfully", which
    /// <c>ServiceBase.AutoLog</c> writes by another route entirely, so the sink looks alive
    /// while not one application log reaches it. Verified by running the service and reading
    /// the log.
    /// </para>
    /// <para>
    /// The same reasoning replaces the usual warning about <c>ClearProviders()</c> after
    /// <c>UseWindowsService()</c>: with Serilog owning the factory, the MEL provider is out of
    /// the picture either way.
    /// </para>
    /// </remarks>
    public static void ConfigureLogging(this WebApplicationBuilder builder, DataDirectory dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dataDirectory);

        var logDirectory = Path.Combine(dataDirectory.Path, LogFolderName);
        Directory.CreateDirectory(logDirectory);

        var configuration = new LoggerConfiguration()
            // Verbose here on purpose: the Logging:LogLevel section filters first, so levels are
            // governed in one place instead of two.
            .MinimumLevel.Verbose()
            .Enrich.With(new UtcTimestampEnricher())
            .WriteTo.File(
                path: Path.Combine(logDirectory, "wcs-.log"),
                outputTemplate: FileOutputTemplate,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 32 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 31,
                shared: true);

        if (EventSourceExists(ServiceConstants.Name))
        {
            // Event Viewer is for what an operator has to act on, not a trace of every request.
            configuration.WriteTo.EventLog(
                source: ServiceConstants.Name,
                logName: "Application",
                outputTemplate: EventLogOutputTemplate,
                restrictedToMinimumLevel: LogEventLevel.Warning,
                // Registering a source needs administrator rights and belongs to the installer.
                // Doing it here would make a developer run on an unprovisioned machine fail at
                // startup, for logging.
                manageEventSource: false);
        }

        builder.Services.AddSerilog(configuration.CreateLogger(), dispose: true);
    }

    private static bool EventSourceExists(string source)
    {
        try
        {
            return EventLog.SourceExists(source);
        }
        catch (System.Security.SecurityException)
        {
            // Reading the source registry needs rights this process may not have. No Event Log
            // sink then; the file sink still records everything.
            return false;
        }
    }
}
