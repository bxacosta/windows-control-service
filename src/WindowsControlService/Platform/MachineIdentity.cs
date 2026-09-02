namespace WindowsControlService.Platform;

/// <inheritdoc />
public sealed class MachineIdentity : IMachineIdentity
{
    // Read on every access rather than captured once. It costs nothing, and a name cached at
    // startup would keep being reported after a rename until the service was restarted.
    public string MachineName => Environment.MachineName;
}
