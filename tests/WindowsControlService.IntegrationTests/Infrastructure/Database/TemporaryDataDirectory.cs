using Microsoft.Data.Sqlite;

namespace WindowsControlService.IntegrationTests.Infrastructure.Database;

/// <summary>
/// A throwaway directory for one test. The database lives in a real file, never in
/// <c>:memory:</c>: an in-memory database disappears when its connection closes, and DbUp
/// opens several.
/// </summary>
internal sealed class TemporaryDataDirectory : IDisposable
{
    public TemporaryDataDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "wcs-database-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        // SQLite keeps the file handle in a connection pool; clearing it lets the delete succeed.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
