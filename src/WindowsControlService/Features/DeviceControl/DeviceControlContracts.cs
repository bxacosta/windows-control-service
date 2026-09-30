namespace WindowsControlService.Features.DeviceControl;

/// <param name="LastModified">
/// Metadata, and null when the state has never been changed through this service. Not an error.
/// </param>
public sealed record UsbBlockStatus(bool Blocked, DateTime? LastModified);

public sealed record SetUsbBlockedRequest(
    // bool? rather than bool, and this is the whole reason: a non-nullable bool leaves a body
    // with no field sitting at false, so {} would silently read as "unblock" instead of 400.
    [System.ComponentModel.DataAnnotations.Required] bool? Blocked);
