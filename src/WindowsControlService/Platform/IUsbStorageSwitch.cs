using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Platform;

/// <summary>
/// Turns USB mass storage on and off through the registry. The registry is the source of
/// truth for the current state; the database only records when it last changed.
/// </summary>
/// <remarks>
/// Blocking stops new drives from mounting. It does not unmount drives that are already
/// mounted: a service runs in session 0, where the shell APIs that eject a volume have no
/// window station to act on, so ejecting is not attempted.
/// </remarks>
public interface IUsbStorageSwitch
{
    Result<bool> IsBlocked();

    Result Block();

    Result Unblock();
}
