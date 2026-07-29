using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Platform;

/// <summary>
/// Turns USB mass storage on and off through the registry. The registry is the source of
/// truth for the current state; the database only records when it last changed.
/// </summary>
/// <remarks>
/// Blocking stops new drives from mounting. It does not unmount drives that are already
/// mounted -- see <c>docs/03-mecanismos-windows.md</c> section 3 for why ejecting them is not
/// attempted from a service running in session 0.
/// </remarks>
public interface IUsbStorageSwitch
{
    Result<bool> IsBlocked();

    Result Block();

    Result Unblock();
}
