using System.ComponentModel;
using System.Diagnostics;

namespace WindowsControlService.IntegrationTests.WebInterface;

/// <summary>
/// Runs the interface rules through Node's own test runner and reports its result here, so that
/// <c>dotnet test</c> stays the one answer to "is the tree green".
/// </summary>
/// <remarks>
/// <para>
/// The rules in <c>wwwroot/js/rules.js</c> are plain ESM with no DOM and no dependency, which is
/// what makes this possible with no framework, no bundler and nothing added to the service:
/// <c>node --test</c> runs the file as it is. Node is a tool on the machine of whoever develops
/// this, in the same way the .NET SDK is; it is not part of the build or the deployment.
/// </para>
/// <para>
/// It fails rather than passes quietly when Node is missing. A suite that goes green while a
/// whole layer went unverified is worse than one that says what it could not do.
/// </para>
/// </remarks>
public sealed class InterfaceRuleTests
{
    [Fact]
    public async Task EveryInterfaceRuleHolds()
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Repository.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--test");
        // Quoted through ArgumentList rather than expanded by the shell: Node does the globbing,
        // which is also what makes this work the same from any working directory.
        start.ArgumentList.Add("tests/interface/*.test.mjs");

        using var process = StartOrExplain(start);

        var output = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = await process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);

        Assert.True(process.ExitCode == 0, $"node --test failed:\n{output}\n{errors}");
    }

    private static Process StartOrExplain(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start)
                ?? throw new InvalidOperationException("node did not start.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "node is not on PATH, so the rules in wwwroot/js/rules.js were not verified. "
                + "Install Node and run the suite again, or run "
                + "'node --test tests/interface/*.test.mjs' by hand to see what it says.",
                exception);
        }
    }
}
