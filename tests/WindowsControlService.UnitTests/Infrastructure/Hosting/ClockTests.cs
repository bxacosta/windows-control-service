using WindowsControlService.Infrastructure.Hosting;

namespace WindowsControlService.UnitTests.Infrastructure.Hosting;

/// <summary>
/// The project rule is that no service ever calls <c>DateTime.UtcNow</c>; they take a
/// <see cref="TimeProvider"/> instead. These tests pin the two facts the rule rests on.
/// </summary>
public sealed class ClockTests
{
    [Fact]
    public void TheContainerHandsOutTheSystemClock()
    {
        // Not a formality: .NET 10 does not register TimeProvider by default, so this is what
        // catches AddServiceInfrastructure being dropped from the composition root.
        var provider = new ServiceCollection().AddServiceInfrastructure().BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void TheContainerHandsOutASingleSequentialExecutor()
    {
        var provider = new ServiceCollection().AddServiceInfrastructure().BuildServiceProvider();

        // One lock for the whole service, or the guarantee it exists to give is worthless.
        Assert.Same(
            provider.GetRequiredService<ISequentialExecutor>(),
            provider.GetRequiredService<ISequentialExecutor>());
    }
}
