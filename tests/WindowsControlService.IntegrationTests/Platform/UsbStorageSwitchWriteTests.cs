using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// Writes to the real registry. There is no isolated machine to do this on: what is under test is
/// the effect on Windows itself, and a double would only prove the double works.
/// </summary>
/// <remarks>
/// So the rule is that the machine is left exactly as it was found, whatever it was found as, and
/// whether or not the test passes. That includes the case where USB storage was already blocked
/// when the suite started: the original value is put back, not the value this test would like it
/// to have. Note that for the length of a run a machine that was blocked is briefly not, which is
/// unavoidable when the subject is the switch itself.
/// </remarks>
[Trait("Requires", "Admin")]
public sealed class UsbStorageSwitchWriteTests
{
    private const string DriverKeyPath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
    private const string StoragePolicyKeyPath = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";
    private const int Manual = 3;
    private const int Disabled = 4;

    private readonly UsbStorageSwitch _switch = new(NullLogger<UsbStorageSwitch>.Instance);

    /// <summary>
    /// Everything about this machine that <see cref="UsbStorageSwitch"/> can change, including
    /// whether the key it writes into existed at all.
    /// </summary>
    private readonly record struct UsbState(int Start, int? WriteProtect, bool StoragePolicyKeyExisted);

    private static UsbState Capture() =>
        new(ReadStart(), ReadWriteProtect(), StoragePolicyKeyExists());

    /// <remarks>
    /// Restoring the values is not enough. <c>Block</c> reaches StorageDevicePolicies with
    /// <c>CreateSubKey</c>, so on a machine that never had that key -- the normal case on Windows
    /// 11 -- a run used to leave an empty one behind for good. Putting the value back and calling
    /// that "as we found it" was wrong by exactly one key.
    /// </remarks>
    private static void Restore(UsbState state)
    {
        WriteStart(state.Start);

        if (!state.StoragePolicyKeyExisted)
        {
            Registry.LocalMachine.DeleteSubKeyTree(StoragePolicyKeyPath, throwOnMissingSubKey: false);
            return;
        }

        WriteWriteProtect(state.WriteProtect);
    }

    /// <summary>The machine is back exactly as it was, asserted rather than hoped for.</summary>
    private static void AssertRestored(UsbState original)
    {
        Assert.Equal(original.Start, ReadStart());
        Assert.Equal(original.StoragePolicyKeyExisted, StoragePolicyKeyExists());
        Assert.Equal(original.WriteProtect, ReadWriteProtect());
    }

    [Fact]
    public void BlockingSetsStartToDisabledAndUnblockingPutsItBack()
    {
        var original = Capture();
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
            Restore(original);
        }

        AssertRestored(original);
    }

    [Fact]
    public void TheWriteProtectValueFollowsTheBlock()
    {
        var original = Capture();
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
            Restore(original);
        }

        AssertRestored(original);
    }

    [Fact]
    public void BlockingTwiceIsHarmless()
    {
        var original = Capture();
        try
        {
            Assert.True(_switch.Block().IsSuccess);
            Assert.True(_switch.Block().IsSuccess);
            Assert.Equal(Disabled, ReadStart());
        }
        finally
        {
            Restore(original);
        }

        AssertRestored(original);
    }

    private static int ReadStart()
    {
        using var key = Registry.LocalMachine.OpenSubKey(DriverKeyPath);
        return (int)key!.GetValue("Start")!;
    }

    private static void WriteStart(int value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(DriverKeyPath, writable: true);
        key!.SetValue("Start", value, RegistryValueKind.DWord);
    }

    private static bool StoragePolicyKeyExists()
    {
        using var key = Registry.LocalMachine.OpenSubKey(StoragePolicyKeyPath);
        return key is not null;
    }

    private static int? ReadWriteProtect()
    {
        using var key = Registry.LocalMachine.OpenSubKey(StoragePolicyKeyPath);
        return key?.GetValue("WriteProtect") as int?;
    }

    private static void WriteWriteProtect(int? value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(StoragePolicyKeyPath, writable: true);

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
