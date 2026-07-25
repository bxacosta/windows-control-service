using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;

namespace WindowsControlService.Infrastructure.Database;

public static class DatabaseModule
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.Section))
            .ValidateDataAnnotations()
            .Validate(
                options => options.BusyTimeout >= TimeSpan.FromSeconds(1),
                $"{DatabaseOptions.Section}:{nameof(DatabaseOptions.BusyTimeout)} must be at least one second.")
            // Turns "the service started but the busy timeout is zero" into "the service did not
            // start, and the reason is written down".
            .ValidateOnStart();

        services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();

        return services;
    }

    /// <summary>
    /// Enables WAL and applies pending migrations. Runs before the host serves anything: starting
    /// up on a half-applied schema is worse than not starting at all.
    /// </summary>
    public static void MigrateDatabase(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var factory = services.GetRequiredService<IDbConnectionFactory>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseModule).FullName!);

        EnableWriteAheadLogging(factory.ConnectionString, logger);

        var upgrader = DeployChanges.To
            .SqliteDatabase(factory.ConnectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .WithTransactionPerScript()
            .LogTo(new DbUpLogger(logger))
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        // Guarded because result.Scripts is a lazy sequence: counting it is work the analyzer
        // is right to keep out of a disabled log level.
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Database schema is current. {ScriptCount} script(s) applied.", result.Scripts.Count());
        }
    }

    /// <summary>
    /// Set once: SQLite persists the journal mode in the file, so it applies to every later
    /// connection. Without it a worker writing blocks the HTTP requests reading.
    /// </summary>
    private static void EnableWriteAheadLogging(string connectionString, ILogger logger)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        var mode = command.ExecuteScalar() as string;

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("SQLite refused write-ahead logging; journal mode is {JournalMode}.", mode);
        }
    }
}
