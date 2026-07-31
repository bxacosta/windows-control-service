using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.IntegrationTests.Fakes;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests;

/// <summary>
/// Hosts the real application over HTTP with a throwaway database and the platform layer swapped
/// for doubles, so nothing here can touch WDAC, the registry or the event log.
/// </summary>
public sealed class ServiceApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory;
    private readonly bool _ownsDataDirectory;
    private readonly Dictionary<string, string?> _settings = [];

    /// <param name="dataDirectory">
    /// Pass an existing directory to simulate a restart: a second factory over the same data
    /// keeps the database and, with it, the password and the security stamp.
    /// </param>
    public ServiceApplicationFactory(string? dataDirectory = null)
    {
        _ownsDataDirectory = dataDirectory is null;
        _dataDirectory = dataDirectory
            ?? Path.Combine(Path.GetTempPath(), "wcs-http-tests", Guid.NewGuid().ToString("N"));
    }

    public string DataDirectory => _dataDirectory;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));

    public FakeCodeIntegrityTool CodeIntegrity { get; } = new();

    public FakeUsbStorageSwitch UsbStorage { get; } = new();

    public FakeProcessInventory ProcessInventory { get; } = new();

    public FakeLogonEventSource LogonEvents { get; } = new();

    public FakePortableExecutableReader ExecutableReader { get; } = new();

    /// <summary>
    /// Raises the login limit for the tests that are not testing it. The rate limiter is
    /// process-wide state, so a test that logs in repeatedly would otherwise exhaust the window
    /// and fail whatever runs next.
    /// </summary>
    public ServiceApplicationFactory WithGenerousLoginLimit()
    {
        _settings["Authentication:LoginAttemptsPerMinute"] = "100";
        return this;
    }

    public ServiceApplicationFactory With(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Directory.CreateDirectory(_dataDirectory);

        builder.UseSetting(DataDirectoryExtensions.ConfigurationKey, _dataDirectory);

        // Port 0: the test server never actually listens, but UseUrls in Program.cs would
        // otherwise pin every instance to 5150.
        builder.UseSetting("urls", "http://127.0.0.1:0");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<ICodeIntegrityTool>();
            services.AddSingleton<ICodeIntegrityTool>(CodeIntegrity);

            services.RemoveAll<IUsbStorageSwitch>();
            services.AddSingleton<IUsbStorageSwitch>(UsbStorage);

            services.RemoveAll<IProcessInventory>();
            services.AddSingleton<IProcessInventory>(ProcessInventory);

            services.RemoveAll<ILogonEventSource>();
            services.AddSingleton<ILogonEventSource>(LogonEvents);

            services.RemoveAll<IPortableExecutableReader>();
            services.AddSingleton<IPortableExecutableReader>(ExecutableReader);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (!_ownsDataDirectory)
        {
            return;
        }

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
