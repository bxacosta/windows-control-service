using Microsoft.Extensions.Logging.Abstractions;
using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Features.ApplicationBlocking;

/// <summary>
/// Runs the real <c>ConvertFrom-CIPolicy</c> against the generated document. It writes a binary
/// to a temp file and nothing else: no policy is deployed, so the machine is untouched.
/// </summary>
/// <remarks>
/// The XSD test catches structural mistakes; this one catches the rest. ConvertFrom-CIPolicy
/// rejects documents the schema accepts -- the BOM problem is the obvious example -- and finding
/// that out here is far cheaper than finding it out while deploying.
/// </remarks>
[Trait("Requires", "Admin")]
public sealed class WdacPolicyConversionTests : IDisposable
{
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "wcs-policy-conversion", Guid.NewGuid().ToString("N"));

    public WdacPolicyConversionTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    [Fact]
    public async Task WindowsConvertsTheGeneratedDocumentToABinaryPolicy()
    {
        var applications = new List<BlockedApplication>
        {
            new() { Id = 1, Name = "Test target", MatchValue = "wcs-test-target.dll", MatchAttribute = "FileName" },
            new() { Id = 2, Name = "Awkward & name", MatchValue = "other.exe", MatchAttribute = "FileName" },
        };

        var (exitCode, standardError, binaryPath) = await ConvertAsync(WdacPolicyDocument.Build(applications));

        Assert.True(File.Exists(binaryPath), $"ConvertFrom-CIPolicy exit {exitCode}: {standardError}");
        Assert.True(new FileInfo(binaryPath).Length > 0);
    }

    [Fact]
    public async Task WindowsAlsoConvertsAPolicyWithNoDenyRules()
    {
        var (exitCode, standardError, binaryPath) = await ConvertAsync(WdacPolicyDocument.Build([]));

        Assert.True(File.Exists(binaryPath), $"ConvertFrom-CIPolicy exit {exitCode}: {standardError}");
    }

    private async Task<(int ExitCode, string StandardError, string BinaryPath)> ConvertAsync(byte[] document)
    {
        var stem = Path.Combine(_workDirectory, Guid.NewGuid().ToString("N"));
        var xmlPath = stem + ".xml";
        var binaryPath = stem + ".bin";

        await File.WriteAllBytesAsync(xmlPath, document, CancellationToken.None);

        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var result = await runner.RunAsync(
            powerShell,
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                $"ConvertFrom-CIPolicy -XmlFilePath '{xmlPath}' -BinaryFilePath '{binaryPath}'",
            ],
            TimeSpan.FromSeconds(60),
            CancellationToken.None);

        return (result.ExitCode, result.StandardError, binaryPath);
    }
}
