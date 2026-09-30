using Microsoft.Extensions.Logging.Abstractions;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>Reads real Windows binaries. No privileges needed, no side effects.</summary>
public sealed class PortableExecutableReaderTests : IDisposable
{
    private static readonly string SystemDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.System);

    private readonly PortableExecutableReader _reader = new(NullLogger<PortableExecutableReader>.Instance);
    private readonly TemporaryDirectory _work = new("wcs-pe-reader-tests");

    public void Dispose()
    {
        _work.Dispose();
    }

    [Fact]
    public void ReadOriginalFileNameDoesNotFollowMuiRedirection()
    {
        var notepad = Path.Combine(SystemDirectory, "notepad.exe");

        var name = _reader.ReadVersionFields(notepad).OriginalFileName;

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
        Assert.NotEqual(viaFileVersionInfo, _reader.ReadVersionFields(notepad).OriginalFileName, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cmd.exe", "Cmd.Exe")]
    [InlineData("ping.exe", "ping.exe")]
    public void ReadOriginalFileNameMatchesTheNeutralResourceOfSystemBinaries(string fileName, string expected)
    {
        var name = _reader.ReadVersionFields(Path.Combine(SystemDirectory, fileName)).OriginalFileName;

        Assert.Equal(expected, name, ignoreCase: true);
        Assert.DoesNotContain(".MUI", name!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOriginalFileNameReturnsNullForAMissingFile()
    {
        var missing = Path.Combine(_work.Path, "no-such-binary.exe");

        Assert.Null(_reader.ReadVersionFields(missing).OriginalFileName);
    }

    [Fact]
    public async Task ReadOriginalFileNameReturnsNullWhenThereIsNoVersionResource()
    {
        var file = Path.Combine(_work.Path, "not-really-a-binary.exe");
        await File.WriteAllTextAsync(file, "this is not a portable executable");

        Assert.Null(_reader.ReadVersionFields(file).OriginalFileName);
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
        var (description, product) = _reader.ReadDisplayInfo(Path.Combine(_work.Path, "missing.exe"));

        Assert.Null(description);
        Assert.Null(product);
    }
}
