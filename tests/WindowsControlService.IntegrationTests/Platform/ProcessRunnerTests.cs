using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// Runs against real Windows processes. cmd.exe and ping.exe are always present and need no
/// privileges, so these run in the normal test pass.
/// </summary>
public sealed class ProcessRunnerTests : IDisposable
{
    private static readonly string CommandProcessor =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(30);

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "wcs-process-runner-tests", Guid.NewGuid().ToString("N"));

    public ProcessRunnerTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind is not worth failing a test over.
        }
    }

    [Fact]
    public async Task CapturesStandardOutput()
    {
        var result = await _runner.RunAsync(CommandProcessor, ["/c", "echo", "hola"], GenerousTimeout);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hola", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsTheExitCode()
    {
        var result = await _runner.RunAsync(CommandProcessor, ["/c", "exit", "3"], GenerousTimeout);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task CapturesStandardError()
    {
        var result = await _runner.RunAsync(CommandProcessor, ["/c", "echo", "malo", "1>&2"], GenerousTimeout);

        Assert.Contains("malo", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("malo", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotDeadlockOnOutputLargerThanThePipeBuffer()
    {
        // The Windows pipe buffer is 4 KB. This is the test that fails -- by hanging -- if the
        // two streams are drained one after the other instead of in parallel.
        const int lineCount = 4000;
        var file = Path.Combine(_workDirectory, "large.txt");
        await File.WriteAllLinesAsync(file, Enumerable.Range(0, lineCount).Select(i => $"line {i:D6} {new string('x', 60)}"));
        var expectedBytes = new FileInfo(file).Length;
        Assert.True(expectedBytes > 4096, "the fixture must exceed the pipe buffer");

        var result = await _runner.RunAsync(CommandProcessor, ["/c", "type", file], GenerousTimeout);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(lineCount, result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task PassesArgumentsContainingSpacesIntact()
    {
        var directory = Path.Combine(_workDirectory, "una carpeta con espacios");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "un archivo.txt");
        await File.WriteAllTextAsync(file, "contenido intacto");

        // If the argument were concatenated instead of passed through ArgumentList, type would
        // receive three broken paths and fail.
        var result = await _runner.RunAsync(CommandProcessor, ["/c", "type", file], GenerousTimeout);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Contains("contenido intacto", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KillsTheProcessTreeOnTimeoutAndLeavesNoOrphans()
    {
        var before = PingProcessIds();

        // cmd.exe spawns ping.exe as a child: killing only the parent would orphan it.
        var result = await _runner.RunAsync(
            CommandProcessor,
            ["/c", "ping", "-n", "20", "127.0.0.1"],
            TimeSpan.FromSeconds(1));

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessResult.TimedOutExitCode, result.ExitCode);

        // Termination is asynchronous, so poll rather than assert immediately.
        HashSet<int> survivors = [];
        for (var attempt = 0; attempt < 20; attempt++)
        {
            survivors = [.. PingProcessIds().Except(before)];
            if (survivors.Count == 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Empty(survivors);
    }

    private static HashSet<int> PingProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("PING"))
        {
            using (process)
            {
                ids.Add(process.Id);
            }
        }

        return ids;
    }
}
