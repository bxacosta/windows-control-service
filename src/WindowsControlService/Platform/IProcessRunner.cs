namespace WindowsControlService.Platform;

/// <summary>
/// The only way this service starts an external process. One implementation, because the
/// pipe-draining and process-tree rules are easy to get subtly wrong.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs <c>fileName</c> to completion, or kills it when <c>timeout</c> elapses.</summary>
    /// <remarks>
    /// The cancellation token is observed only before the process starts. It deliberately does
    /// not kill a running child: interrupting CiTool halfway through a policy update leaves
    /// the machine in a worse state than waiting for it.
    /// </remarks>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
