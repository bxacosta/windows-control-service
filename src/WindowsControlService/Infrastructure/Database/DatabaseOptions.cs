using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Infrastructure.Database;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>File name inside the data directory. The directory itself is not configuration.</summary>
    [Required]
    [MinLength(1)]
    public string FileName { get; set; } = "windows-control-service.db";

    /// <summary>
    /// How long a command waits on SQLITE_BUSY before giving up. Workers and HTTP requests use
    /// separate connections, so this window is what keeps a writer from failing a reader.
    /// </summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
