using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;

namespace WindowsControlService.IntegrationTests.Infrastructure.Database;

public sealed class DatabaseOptionsValidationTests
{
    [Fact]
    public async Task AZeroBusyTimeoutStopsTheHostFromStarting()
    {
        using var directory = new TemporaryDataDirectory();
        using var host = BuildHost(directory.Path, new Dictionary<string, string?>
        {
            ["Database:BusyTimeout"] = "00:00:00",
        });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));

        // The message has to name the option, or the operator is left guessing which one.
        Assert.Contains(
            "Database:BusyTimeout",
            string.Join(" ", exception.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyFileNameStopsTheHostFromStarting()
    {
        using var directory = new TemporaryDataDirectory();
        using var host = BuildHost(directory.Path, new Dictionary<string, string?>
        {
            ["Database:FileName"] = string.Empty,
        });

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheDefaultsStartCleanly()
    {
        using var directory = new TemporaryDataDirectory();
        using var host = BuildHost(directory.Path, []);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        Assert.Equal(
            TimeSpan.FromSeconds(5),
            host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.BusyTimeout);
    }

    private static IHost BuildHost(string dataDirectory, Dictionary<string, string?> overrides)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [$"--{DataDirectoryExtensions.ConfigurationKey}={dataDirectory}"],
        });

        builder.Configuration.AddInMemoryCollection(overrides);
        builder.AddDataDirectory();
        builder.Services.AddDatabase(builder.Configuration);

        return builder.Build();
    }
}
