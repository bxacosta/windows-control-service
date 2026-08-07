using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.DeviceControl;

/// <summary>
/// Feeds the event stream with the USB state. Worth pushing even though only this service writes
/// it: the registry is the source of truth, so a value changed by hand with regedit shows up in
/// the interface on the next snapshot instead of silently disagreeing with it.
/// </summary>
public sealed class UsbStatusSnapshot(IDeviceControlService devices) : IServiceEventSnapshot
{
    public const string EventName = "usb";

    public async ValueTask<ServiceEvent?> CaptureAsync(CancellationToken cancellationToken)
    {
        var status = await devices.GetUsbStatusAsync(cancellationToken);

        return status.IsSuccess ? new ServiceEvent(EventName, status.Value) : null;
    }
}
