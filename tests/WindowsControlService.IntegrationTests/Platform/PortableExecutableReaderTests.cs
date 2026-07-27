using Microsoft.Extensions.Logging.Abstractions;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>Reads real Windows binaries. No privileges needed, no side effects.</summary>
public sealed class PortableExecutableReaderTests : IDisposable
{
    private static readonly string SystemDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.System);

    private readonly PortableExecutableReader _reader = new(NullLogger<PortableExecutableReader>.Instance);
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "wcs-pe-reader-tests", Guid.NewGuid().ToString("N"));

    public PortableExecutableReaderTests() => Directory.CreateDirectory(_workDirectory);

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
    public void ReadOriginalFileNameDoesNotFollowMuiRedirection()
    {
        var notepad = Path.Combine(SystemDirectory, "notepad.exe");

        var name = _reader.ReadOriginalFileName(notepad);

        Assert.Equal("NOTEPAD.EXE", name, ignoreCase: true);

        // This is the assertion that matters. FileVersionInfo answers NOTEPAD.EXE.MUI here,
        // and a WDAC rule built from that value never matches anything.
        Assert.DoesNotContain(".MUI", name!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileVersionInfoWouldHaveGivenTheMuiNameInstead()
    {
        // Not a test of our code: it pins the platform behaviour the workaround exists for, so
        // that if Windows ever stops redirecting, we find out here instead of by deploying a
        // policy that blocks nothing.
        var notepad = Path.Combine(SystemDirectory, "notepad.exe");

        var viaFileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(notepad).OriginalFilename;

        Assert.EndsWith(".MUI", viaFileVersionInfo!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(viaFileVersionInfo, _reader.ReadOriginalFileName(notepad), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cmd.exe", "Cmd.Exe")]
    [InlineData("ping.exe", "ping.exe")]
    public void ReadOriginalFileNameMatchesTheNeutralResourceOfSystemBinaries(string fileName, string expected)
    {
        var name = _reader.ReadOriginalFileName(Path.Combine(SystemDirectory, fileName));

        Assert.Equal(expected, name, ignoreCase: true);
        Assert.DoesNotContain(".MUI", name!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOriginalFileNameReturnsNullForAMissingFile()
    {
        var missing = Path.Combine(_workDirectory, "no-such-binary.exe");

        Assert.Null(_reader.ReadOriginalFileName(missing));
    }

    [Fact]
    public async Task ReadOriginalFileNameReturnsNullWhenThereIsNoVersionResource()
    {
        var file = Path.Combine(_workDirectory, "not-really-a-binary.exe");
        await File.WriteAllTextAsync(file, "this is not a portable executable");

        Assert.Null(_reader.ReadOriginalFileName(file));
    }

    [Fact]
    public void ReadDisplayInfoReturnsSomethingForASystemBinary()
    {
        var (description, product) = _reader.ReadDisplayInfo(Path.Combine(SystemDirectory, "notepad.exe"));

        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.False(string.IsNullOrWhiteSpace(product));
    }

    [Fact]
    public void ReadDisplayInfoReturnsNullsForAMissingFile()
    {
        var (description, product) = _reader.ReadDisplayInfo(Path.Combine(_workDirectory, "missing.exe"));

        Assert.Null(description);
        Assert.Null(product);
    }
}
