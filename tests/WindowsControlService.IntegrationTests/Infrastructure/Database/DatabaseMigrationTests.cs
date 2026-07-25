using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;

namespace WindowsControlService.IntegrationTests.Infrastructure.Database;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public void FirstRunAppliesTheScriptsAndTheSecondAppliesNone()
    {
        using var directory = new TemporaryDataDirectory();

        var firstRun = ApplyMigrations(directory.Path);
        Assert.NotEmpty(firstRun);

        var secondRun = ApplyMigrations(directory.Path);
        Assert.Empty(secondRun);
    }

    [Fact]
    public void MigrationCreatesTheJournalTable()
    {
        using var directory = new TemporaryDataDirectory();

        ApplyMigrations(directory.Path);

        using var host = BuildHost(directory.Path);
        using var connection = new SqliteConnection(
            host.Services.GetRequiredService<IDbConnectionFactory>().ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = JournalTableExistsSql;

        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void MigrationTurnsOnWriteAheadLogging()
    {
        using var directory = new TemporaryDataDirectory();

        ApplyMigrations(directory.Path);

        using var host = BuildHost(directory.Path);
        using var connection = new SqliteConnection(
            host.Services.GetRequiredService<IDbConnectionFactory>().ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        // Persisted in the file itself, so a connection opened later still reports wal.
        Assert.Equal("wal", (command.ExecuteScalar() as string)?.ToLowerInvariant());
    }

    [Fact]
    public async Task ConnectionFactoryOpensAgainstTheDataDirectory()
    {
        using var directory = new TemporaryDataDirectory();
        ApplyMigrations(directory.Path);

        using var host = BuildHost(directory.Path);
        var factory = host.Services.GetRequiredService<IDbConnectionFactory>();

        await using var connection = await factory.OpenAsync(CancellationToken.None);

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        Assert.Contains(directory.Path, factory.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    private const string JournalTableExistsSql =
        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersions';";

    private static IReadOnlyList<string> ApplyMigrations(string dataDirectory)
    {
        using var host = BuildHost(dataDirectory);
        var factory = host.Services.GetRequiredService<IDbConnectionFactory>();

        var before = AppliedScriptNames(factory.ConnectionString);
        host.Services.MigrateDatabase();
        var after = AppliedScriptNames(factory.ConnectionString);

        return [.. after.Except(before)];
    }

    private static List<string> AppliedScriptNames(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var exists = connection.CreateCommand();
        exists.CommandText = JournalTableExistsSql;
        if (exists.ExecuteScalar() is not 1L)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static IHost BuildHost(string dataDirectory)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [$"--{DataDirectoryExtensions.ConfigurationKey}={dataDirectory}"],
        });

        builder.AddDataDirectory();
        builder.Services.AddDatabase(builder.Configuration);

        return builder.Build();
    }
}
