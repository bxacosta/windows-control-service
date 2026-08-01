using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// Writes to the real registry. Every test captures the original value first and restores it in
/// a <c>finally</c>, so a failure cannot leave this machine unable to mount USB drives.
/// </summary>
[Trait("Requires", "Admin")]
public sealed class UsbStorageSwitchWriteTests
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
    private const int Manual = 3;
    private const int Disabled = 4;

    private readonly UsbStorageSwitch _switch = new(NullLogger<UsbStorageSwitch>.Instance);

    [Fact]
    public void BlockingSetsStartToDisabledAndUnblockingPutsItBack()
    {
        var original = ReadStart();
        try
        {
            Assert.True(_switch.Block().IsSuccess);
            Assert.Equal(Disabled, ReadStart());
            Assert.True(_switch.IsBlocked().Value);

            Assert.True(_switch.Unblock().IsSuccess);
            Assert.Equal(Manual, ReadStart());
            Assert.False(_switch.IsBlocked().Value);
        }
        finally
        {
            WriteStart(original);
        }

        Assert.Equal(original, ReadStart());
    }

    [Fact]
    public void TheWriteProtectValueFollowsTheBlock()
    {
        var original = ReadStart();
        var originalWriteProtect = ReadWriteProtect();
        try
        {
            _switch.Block();

            // Defence in depth, and allowed to be missing: the real block is the driver setting.
            Assert.Equal(1, ReadWriteProtect());

            _switch.Unblock();
            Assert.Equal(0, ReadWriteProtect());
        }
        finally
        {
            WriteStart(original);
            WriteWriteProtect(originalWriteProtect);
        }
    }

    [Fact]
    public void BlockingTwiceIsHarmless()
    {
        var original = ReadStart();
        try
        {
            Assert.True(_switch.Block().IsSuccess);
            Assert.True(_switch.Block().IsSuccess);
            Assert.Equal(Disabled, ReadStart());
        }
        finally
        {
            WriteStart(original);
        }
    }

    private static int ReadStart()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return (int)key!.GetValue("Start")!;
    }

    private static void WriteStart(int value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        key!.SetValue("Start", value, RegistryValueKind.DWord);
    }

    private static int? ReadWriteProtect()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies");
        return key?.GetValue("WriteProtect") as int?;
    }

    private static void WriteWriteProtect(int? value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies",
            writable: true);

        if (key is null)
        {
            return;
        }

        if (value is { } original)
        {
            key.SetValue("WriteProtect", original, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue("WriteProtect", throwOnMissingValue: false);
        }
    }
}
