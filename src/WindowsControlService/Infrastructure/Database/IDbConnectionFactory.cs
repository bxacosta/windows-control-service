using System.Data.Common;

namespace WindowsControlService.Infrastructure.Database;

/// <summary>
/// Hands out open connections. Injected everywhere instead of passing a connection string
/// around, which is what lets the tests point at a temporary database.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Exposed for DbUp, which takes a connection string rather than a connection.</summary>
    string ConnectionString { get; }

    Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
