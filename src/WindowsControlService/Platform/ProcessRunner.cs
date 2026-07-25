using System.Diagnostics;

namespace WindowsControlService.Platform;

/// <inheritdoc cref="IProcessRunner"/>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        // Checked here and nowhere else: once the child is running, only the timeout may end it.
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // ArgumentList applies the Windows quoting rules for us. Concatenating by hand means
        // escaping quotes by hand, which is where the bugs live.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

        // CiTool without -json prints "Press Enter to Continue" and waits forever. EOF ends it.
        process.StandardInput.Close();

        using var timeoutSource = new CancellationTokenSource(timeout);

        // Both reads start before the wait. The Windows pipe buffer is 4 KB: draining one
        // stream to the end before touching the other deadlocks as soon as the child fills
        // the buffer nobody is reading.
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var standardErrorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await standardOutputTask, await standardErrorTask);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Process {FileName} did not exit within {Timeout}; killing its process tree.",
                fileName,
                timeout);

            try
            {
                // entireProcessTree, because powershell.exe spawns children and killing only
                // the parent leaves them orphaned.
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the timeout firing and the kill.
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                logger.LogWarning(exception, "Could not kill the process tree of {FileName}.", fileName);
            }

            return new ProcessResult(
                ProcessResult.TimedOutExitCode,
                await DrainAsync(standardOutputTask),
                await DrainAsync(standardErrorTask));
        }
    }

    private static async Task<string> DrainAsync(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
