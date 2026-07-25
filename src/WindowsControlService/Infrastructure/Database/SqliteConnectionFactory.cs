using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Hosting;

namespace WindowsControlService.Infrastructure.Database;

/// <inheritdoc cref="IDbConnectionFactory"/>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    public SqliteConnectionFactory(DataDirectory dataDirectory, IOptions<DatabaseOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        ArgumentNullException.ThrowIfNull(options);

        // Built, never interpolated: a directory containing ';' or '"' would inject into the
        // connection string.
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory.Path, options.Value.FileName),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = (int)options.Value.BusyTimeout.TotalSeconds,
        }.ToString();
    }

    public string ConnectionString { get; }

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
