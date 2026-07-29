namespace WindowsControlService.Platform;

public sealed record RunningApplication(string Name, string ExecutablePath, string? ProductName);

/// <summary>
/// Lists what is running, so a blocked application can be picked from a list instead of having
/// its path typed by hand.
/// </summary>
public interface IProcessInventory
{
    IReadOnlyList<RunningApplication> GetRunningApplications();
}
