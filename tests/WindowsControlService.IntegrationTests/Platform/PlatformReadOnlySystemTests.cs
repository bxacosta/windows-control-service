using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// Reads real machine state. Nothing here writes; the tests that do live in
/// <see cref="UsbStorageSwitchWriteTests"/>, which captures the original registry value and
/// restores it in a finally block.
/// </summary>
[Trait("Requires", "Admin")]
public sealed class PlatformReadOnlySystemTests
{
    [Fact]
    public void IsBlockedAgreesWithTheRegistry()
    {
        var switchUnderTest = new UsbStorageSwitch(NullLogger<UsbStorageSwitch>.Instance);

        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USBSTOR");
        var start = (int)key!.GetValue("Start")!;

        var result = switchUnderTest.IsBlocked();

        Assert.True(result.IsSuccess);
        Assert.Equal(start == 4, result.Value);
    }

    [Fact]
    public void ReadingUsbStateLeavesTheRegistryUntouched()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USBSTOR");
        var before = (int)key!.GetValue("Start")!;

        new UsbStorageSwitch(NullLogger<UsbStorageSwitch>.Instance).IsBlocked();

        Assert.Equal(before, (int)key.GetValue("Start")!);
    }

    [Fact]
    public void TheRunningApplicationsSmokeTest()
    {
        var inventory = new ProcessInventory(
            new PortableExecutableReader(NullLogger<PortableExecutableReader>.Instance),
            NullLogger<ProcessInventory>.Instance);

        var applications = inventory.GetRunningApplications();

        Assert.NotEmpty(applications);

        var windowsPrefix = Path.TrimEndingDirectorySeparator(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)) + Path.DirectorySeparatorChar;

        Assert.All(applications, application =>
        {
            Assert.False(string.IsNullOrWhiteSpace(application.Name));
            Assert.EndsWith(".exe", application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(windowsPrefix, application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal(
            applications.Select(application => application.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            applications.Count);
    }
}
