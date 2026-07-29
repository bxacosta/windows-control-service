using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

public sealed class PlatformModuleTests
{
    [Fact]
    public void EveryPlatformInterfaceResolves()
    {
        using var host = BuildHost([]);

        // Constructing them is the point: a missing registration otherwise surfaces at the first
        // HTTP request that happens to need it.
        Assert.NotNull(host.Services.GetRequiredService<IProcessRunner>());
        Assert.NotNull(host.Services.GetRequiredService<IPortableExecutableReader>());
        Assert.NotNull(host.Services.GetRequiredService<ICodeIntegrityTool>());
        Assert.NotNull(host.Services.GetRequiredService<IUsbStorageSwitch>());
        Assert.NotNull(host.Services.GetRequiredService<IProcessInventory>());
        Assert.NotNull(host.Services.GetRequiredService<ILogonEventSource>());
    }

    [Fact]
    public async Task AZeroOperationTimeoutStopsTheHostFromStarting()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["CodeIntegrity:OperationTimeout"] = "00:00:00",
        });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(CancellationToken.None));

        Assert.Contains(
            "CodeIntegrity:OperationTimeout",
            string.Join(" ", exception.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultOperationTimeoutIsThirtySeconds()
    {
        using var host = BuildHost([]);

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            host.Services.GetRequiredService<IOptions<CodeIntegrityOptions>>().Value.OperationTimeout);
    }

    private static IHost BuildHost(Dictionary<string, string?> overrides)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(overrides);
        builder.Services.AddPlatform(builder.Configuration);
        return builder.Build();
    }
}
