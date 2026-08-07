using System.Globalization;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Events;
using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.DeviceControl;

/// <param name="LastModified">
/// Metadata, and null when the state has never been changed through this service. Not an error.
/// </param>
public sealed record UsbBlockStatus(bool Blocked, DateTime? LastModified);

public sealed record SetUsbBlockedRequest(
    // bool? rather than bool, and this is the whole reason: a non-nullable bool leaves a body
    // with no field sitting at false, so {} would silently read as "unblock" instead of 400.
    [System.ComponentModel.DataAnnotations.Required] bool? Blocked);

public interface IDeviceControlService
{
    Task<Result<UsbBlockStatus>> GetUsbStatusAsync(CancellationToken cancellationToken);

    Task<Result> SetUsbBlockedAsync(bool blocked, CancellationToken cancellationToken);
}

public sealed class DeviceControlService(
    IUsbStorageSwitch usbStorage,
    ISettingsRepository settings,
    IServiceEventBroadcaster events,
    ISequentialExecutor executor,
    TimeProvider timeProvider,
    ILogger<DeviceControlService> logger) : IDeviceControlService
{
    internal const string LastModifiedKey = "UsbBlock:LastModified";

    public async Task<Result<UsbBlockStatus>> GetUsbStatusAsync(CancellationToken cancellationToken)
    {
        // Always from the registry. If someone changes the value from outside, the API has to
        // report reality rather than what this service happens to remember.
        var blocked = usbStorage.IsBlocked();
        if (blocked.IsFailure)
        {
            return Result<UsbBlockStatus>.Failure(blocked.Error);
        }

        return Result<UsbBlockStatus>.Success(new UsbBlockStatus(blocked.Value, await ReadLastModifiedAsync(cancellationToken)));
    }

    public Task<Result> SetUsbBlockedAsync(bool blocked, CancellationToken cancellationToken) =>
        executor.RunAsync(token => SetUsbBlockedCoreAsync(blocked, token), cancellationToken);

    private async Task<Result> SetUsbBlockedCoreAsync(bool blocked, CancellationToken cancellationToken)
    {
        var current = usbStorage.IsBlocked();
        if (current.IsFailure)
        {
            return Result.Failure(current.Error);
        }

        // Idempotent: asking to block something already blocked succeeds without writing, and
        // without moving the timestamp to a moment when nothing changed.
        if (current.Value == blocked)
        {
            return Result.Success();
        }

        var applied = blocked ? usbStorage.Block() : usbStorage.Unblock();
        if (applied.IsFailure)
        {
            return applied;
        }

        // Only after the registry write succeeded. The other order would record a change that
        // never happened.
        var changedAt = timeProvider.GetUtcNow().UtcDateTime;
        await settings.SetAsync(
            LastModifiedKey,
            changedAt.ToString("O", CultureInfo.InvariantCulture),
            cancellationToken);

        // Published from what was just written rather than by reading it back: the value is
        // known here, and a second registry read would only be another chance to disagree.
        events.Publish(new ServiceEvent(UsbStatusSnapshot.EventName, new UsbBlockStatus(blocked, changedAt)));

        logger.LogWarning("USB mass storage is now {State}.", blocked ? "blocked" : "unblocked");

        return Result.Success();
    }

    private async Task<DateTime?> ReadLastModifiedAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync(LastModifiedKey, cancellationToken);

        // RoundtripKind, always: plain parsing converts to local time and returns Kind=Local,
        // which breaks every later comparison against UTC without saying so.
        return DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
