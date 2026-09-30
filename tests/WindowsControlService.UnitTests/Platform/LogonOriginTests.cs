using WindowsControlService.Platform;

namespace WindowsControlService.UnitTests.Platform;

/// <summary>What the address in a session event says about where the session came from.</summary>
public sealed class LogonOriginTests
{
    [Theory]
    [InlineData(null, LogonOrigin.Unknown)]
    [InlineData("", LogonOrigin.Unknown)]
    [InlineData("   ", LogonOrigin.Unknown)]
    [InlineData("LOCAL", LogonOrigin.Local)]
    [InlineData("local", LogonOrigin.Local)]
    [InlineData("203.0.113.40", LogonOrigin.Remote)]
    [InlineData("fe80::1", LogonOrigin.Remote)]
    public void OriginIsClassifiedFromTheAddress(string? address, LogonOrigin expected)
    {
        Assert.Equal(expected, LogonEventSource.ToOrigin(address));
    }
}
