namespace WindowsControlService.Infrastructure.Hosting;

/// <summary>Where the database and the log files live. Resolved once, at startup.</summary>
public sealed record DataDirectory(string Path);

public static class DataDirectoryExtensions
{
    public const string DefaultPath = @"C:\ProgramData\WindowsControlService";

    /// <summary>Command-line switch <c>--data-dir=&lt;path&gt;</c>, used by the integration tests.</summary>
    public const string ConfigurationKey = "data-dir";

    /// <summary>
    /// Resolves the data directory, creates it and registers it. Returns it as well because
    /// logging has to be configured before the container exists.
    /// </summary>
    public static DataDirectory AddDataDirectory(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configured = builder.Configuration[ConfigurationKey];

        var path = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : builder.Environment.IsDevelopment()
                ? Directory.GetCurrentDirectory()
                : DefaultPath;

        // Created on every branch, not just the default one: a --data-dir pointing at a missing
        // directory otherwise fails much later, inside SQLite, with an error that never mentions
        // the directory.
        var fullPath = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);

        var dataDirectory = new DataDirectory(fullPath);
        builder.Services.AddSingleton(dataDirectory);
        return dataDirectory;
    }
}
