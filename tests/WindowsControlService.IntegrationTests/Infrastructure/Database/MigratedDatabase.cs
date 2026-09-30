using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;

namespace WindowsControlService.IntegrationTests.Infrastructure.Database;

/// <summary>
/// A real SQLite file in its own directory, migrated the way the service migrates it at start,
/// with whatever the test registers on top.
/// </summary>
internal sealed class MigratedDatabase : IDisposable
{
    private readonly TemporaryDirectory _directory = new("wcs-database-tests");
    private readonly IHost _host;

    public MigratedDatabase(Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [$"--{DataDirectoryExtensions.ConfigurationKey}={_directory.Path}"],
        });

        builder.AddDataDirectory();
        builder.Services.AddDatabase(builder.Configuration);
        configure?.Invoke(builder.Services);

        _host = builder.Build();
        _host.Services.MigrateDatabase();
    }

    public string DirectoryPath => _directory.Path;

    public string ConnectionString => Get<IDbConnectionFactory>().ConnectionString;

    public T Get<T>()
        where T : notnull => _host.Services.GetRequiredService<T>();

    /// <summary>Raw SQL, for the tests that check what the schema itself allows.</summary>
    public async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _host.Dispose();
        _directory.Dispose();
    }
}
