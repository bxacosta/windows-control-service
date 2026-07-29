using Microsoft.Win32;
using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Platform;

/// <inheritdoc cref="IUsbStorageSwitch"/>
public sealed class UsbStorageSwitch(ILogger<UsbStorageSwitch> logger) : IUsbStorageSwitch
{
    private const string DriverKeyPath = @"SYSTEM\CurrentControlSet\Services\USBSTOR";
    private const string StartValueName = "Start";

    /// <summary>Manual start: drives mount normally.</summary>
    private const int StartManual = 3;

    /// <summary>Disabled: the driver never starts, so nothing mounts.</summary>
    private const int StartDisabled = 4;

    private const string StoragePolicyKeyPath = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";
    private const string WriteProtectValueName = "WriteProtect";

    public Result<bool> IsBlocked()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DriverKeyPath, writable: false);
            if (key is null)
            {
                return Result<bool>.Failure(
                    ErrorCode.PlatformUnavailable,
                    "The USB storage driver settings are not present on this machine.");
            }

            if (key.GetValue(StartValueName) is not int start)
            {
                return Result<bool>.Failure(
                    ErrorCode.PlatformUnavailable,
                    "The USB storage driver settings could not be read.");
            }

            return Result<bool>.Success(start == StartDisabled);
        }
        catch (UnauthorizedAccessException)
        {
            // Distinguished from the cases above on purpose: the endpoint builds its message
            // from the code, and "run as administrator" is useless advice when the real problem
            // is a missing key.
            return Result<bool>.Failure(
                ErrorCode.AccessDenied,
                "Administrator rights are required to read the USB storage settings.");
        }
        catch (System.Security.SecurityException)
        {
            return Result<bool>.Failure(
                ErrorCode.AccessDenied,
                "Administrator rights are required to read the USB storage settings.");
        }
    }

    public Result Block() => SetStart(StartDisabled, writeProtect: 1, "block USB storage");

    public Result Unblock() => SetStart(StartManual, writeProtect: 0, "unblock USB storage");

    private Result SetStart(int start, int writeProtect, string action)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DriverKeyPath, writable: true);
            if (key is null)
            {
                return Result.Failure(
                    ErrorCode.PlatformUnavailable,
                    "The USB storage driver settings are not present on this machine.");
            }

            key.SetValue(StartValueName, start, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure(ErrorCode.AccessDenied, $"Administrator rights are required to {action}.");
        }
        catch (System.Security.SecurityException)
        {
            return Result.Failure(ErrorCode.AccessDenied, $"Administrator rights are required to {action}.");
        }

        TrySetWriteProtect(writeProtect);
        return Result.Success();
    }

    /// <summary>
    /// Defence in depth. The key may not exist at all, and failing here must not fail the
    /// operation: the real block is the USBSTOR Start value, which is already written.
    /// </summary>
    private void TrySetWriteProtect(int value)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(StoragePolicyKeyPath, writable: true);
            key?.SetValue(WriteProtectValueName, value, RegistryValueKind.DWord);
        }
        catch (Exception exception) when (exception
            is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            logger.LogWarning(
                exception,
                "Could not set {ValueName} to {Value}. USB storage is still governed by the driver setting.",
                WriteProtectValueName,
                value);
        }
    }
}
