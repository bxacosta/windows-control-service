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
    /// Set to <c>false</c> to keep this process out of the machine's Application log. The
    /// integration tests set it; nothing else should.
    /// </summary>
    public const string EventLogEnabledKey = "Logging:EventLog:Enabled";

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
            .Enrich.With(new UtcTimestampEnricher())
            .WriteTo.File(
                path: Path.Combine(logDirectory, "wcs-.log"),
                outputTemplate: FileOutputTemplate,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 32 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 31,
                shared: true);

        // The Event Log sink belongs to the installed service and to nothing else. On by default,
        // so a real deployment cannot lose it by omission -- and turned off by the integration
        // tests, because a test host booting on a machine where the service is installed finds
        // the source registered and writes into the operator's own Application log under the
        // service's name. Measured: an afternoon of test runs put 315 warnings there, describing
        // temp directories that were never the service's, in the same list an operator reads to
        // find out what the service did.
        var eventLogEnabled = builder.Configuration.GetValue(EventLogEnabledKey, defaultValue: true);

        if (eventLogEnabled && EventSourceExists(ServiceConstants.Name))
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

        ApplyLogLevels(configuration, builder.Configuration.GetSection("Logging:LogLevel"));

        builder.Services.AddSerilog(configuration.CreateLogger(), dispose: true);
    }

    /// <summary>
    /// Carries the standard <c>Logging:LogLevel</c> section over to Serilog.
    /// </summary>
    /// <remarks>
    /// Serilog's factory replaces the one that applies those rules, so without this the section
    /// is read by nothing and every category logs down to Verbose. Measured on the installed
    /// service: Kestrel's per-connection Debug lines filled the file under a Default of
    /// Information. <c>None</c> has no Serilog equivalent and becomes Fatal.
    /// </remarks>
    private static void ApplyLogLevels(LoggerConfiguration configuration, IConfigurationSection levels)
    {
        foreach (var entry in levels.GetChildren())
        {
            if (!Enum.TryParse<LogLevel>(entry.Value, ignoreCase: true, out var level))
            {
                continue;
            }

            var serilogLevel = level switch
            {
                LogLevel.Trace => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                _ => LogEventLevel.Fatal,
            };

            if (entry.Key == "Default")
            {
                configuration.MinimumLevel.Is(serilogLevel);
            }
            else
            {
                configuration.MinimumLevel.Override(entry.Key, serilogLevel);
            }
        }
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
