namespace WindowsControlService.Platform;

/// <summary>
/// Which machine this service is controlling. One line of <see cref="Environment"/> today, behind
/// an interface anyway: reading it is a call to Windows, and nothing above <c>Platform/</c> makes
/// those directly. It is also what lets a test answer with a fixed name instead of the name of
/// whatever machine happens to be running the suite.
/// </summary>
public interface IMachineIdentity
{
    string MachineName { get; }
}
