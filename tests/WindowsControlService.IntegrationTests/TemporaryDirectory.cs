using Microsoft.Data.Sqlite;

namespace WindowsControlService.IntegrationTests;

/// <summary>
/// A throwaway directory for one test, deleted with it. A database inside one lives in a real
/// file, never in <c>:memory:</c>: an in-memory database disappears when its connection closes,
/// and DbUp opens several.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <param name="purpose">The parent folder under TEMP, so a leftover says which tests left it.</param>
    public TemporaryDirectory(string purpose = "wcs-tests")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), purpose, Guid.NewGuid().ToString("N"));
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
