using System.Diagnostics;

namespace WindowsControlService.Platform;

/// <inheritdoc cref="IProcessInventory"/>
public sealed class ProcessInventory(
    IPortableExecutableReader executableReader,
    ILogger<ProcessInventory> logger) : IProcessInventory
{
    private static readonly string WindowsDirectoryPrefix =
        Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        + Path.DirectorySeparatorChar;

    public IReadOnlyList<RunningApplication> GetRunningApplications()
    {
        var paths = new List<string?>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // MainModule throws for protected processes and for processes in other sessions.
                // That is the normal case here, not an error worth reporting.
                paths.Add(process.MainModule?.FileName);
            }
            catch (Exception exception) when (exception
                is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
            {
                logger.LogTrace(exception, "Skipped a process whose module could not be read.");
            }
            finally
            {
                process.Dispose();
            }
        }

        var applications = new List<RunningApplication>();
        foreach (var path in FilterExecutablePaths(paths, Environment.ProcessPath))
        {
            var (description, product) = executableReader.ReadDisplayInfo(path);

            applications.Add(new RunningApplication(
                Name: description ?? Path.GetFileNameWithoutExtension(path),
                ExecutablePath: path,
                ProductName: product));
        }

        return applications;
    }

    /// <summary>
    /// The filtering rules, split out from the process walk so they can be tested against a
    /// fixed list instead of whatever happens to be running.
    /// </summary>
    internal static IReadOnlyList<string> FilterExecutablePaths(
        IEnumerable<string?> executablePaths,
        string? ownExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(executablePaths);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();

        foreach (var path in executablePaths)
        {
            if (string.IsNullOrWhiteSpace(path)
                || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, ownExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The trailing separator is the whole point. Comparing against "C:\Windows" alone
            // also excludes sibling directories that merely start the same way -- C:\Windows.old,
            // left behind by an in-place upgrade, is the realistic case.
            if (path.StartsWith(WindowsDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(path))
            {
                kept.Add(path);
            }
        }

        return kept;
    }
}
