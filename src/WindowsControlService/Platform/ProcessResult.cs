namespace WindowsControlService.Platform;

/// <summary>
/// The outcome of an external process. <see cref="TimedOut"/> uses the agreed exit code -1
/// because reading <see cref="System.Diagnostics.Process.ExitCode"/> of a process that never
/// exited throws.
/// </summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public const int TimedOutExitCode = -1;

    public bool Succeeded => ExitCode == 0;

    public bool TimedOut => ExitCode == TimedOutExitCode;
}
