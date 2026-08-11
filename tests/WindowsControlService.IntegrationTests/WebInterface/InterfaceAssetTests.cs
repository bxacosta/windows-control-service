using System.Text;

namespace WindowsControlService.IntegrationTests.WebInterface;

/// <summary>
/// Rules about the shipped interface that no functional walkthrough would catch, kept as tests
/// so they stay true instead of being remembered. They read the files on disk rather than what
/// the host serves: a rule that only covers the files that happen to exist today is not a rule.
/// </summary>
public sealed class InterfaceAssetTests
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly string[] ForbiddenCursors =
        ["cursor-wait", "cursor:wait", "cursor: wait", "cursor-not-allowed", "cursor:not-allowed", "cursor: not-allowed"];

    public static TheoryData<string> AssetFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(WebRoot(), "*", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(WebRoot(), file));
        }

        return data;
    }

    [Fact]
    public void TheWebRootShipsTheShell()
    {
        Assert.True(File.Exists(Path.Combine(WebRoot(), "index.html")));
    }

    [Theory]
    [MemberData(nameof(AssetFiles))]
    public async Task NoAssetChangesTheCursorWhileLoading(string relativePath)
    {
        var content = await File.ReadAllTextAsync(Path.Combine(WebRoot(), relativePath));

        // The spinner already says "wait". cursor-wait repeats it, and cursor-not-allowed says
        // something different and false: the control is busy, not forbidden.
        foreach (var forbidden in ForbiddenCursors)
        {
            Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(AssetFiles))]
    public async Task NoAssetReachesOutsideThisMachine(string relativePath)
    {
        var content = await File.ReadAllTextAsync(Path.Combine(WebRoot(), relativePath));

        // The XML namespace of SVG is a name, not an address: nothing dereferences it, and
        // createElementNS cannot build an <svg> without it. Removed before the check rather than
        // spelled around in the source, because obfuscating a constant to satisfy a string search
        // is how a rule stops meaning what it says.
        var withoutNamespaces = content.Replace(SvgNamespace, string.Empty, StringComparison.Ordinal);

        // The whole point of option A is that the interface works with no internet and no build
        // step. One CDN link would quietly undo both.
        Assert.DoesNotContain("http://", withoutNamespaces, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", withoutNamespaces, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AssetFiles))]
    public void EveryAssetIsCleanUtf8(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(WebRoot(), relativePath));
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var text = strict.GetString(bytes);

        // Not just "decodes": text that was encoded twice decodes perfectly and renders as
        // "Checkingâ€¦" on screen. These two sequences are what double encoding always leaves
        // behind, and one of them shipped in index.html unnoticed because no test looked.
        Assert.DoesNotContain("Ã¢", text, StringComparison.Ordinal);
        Assert.DoesNotContain("â€", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Â ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyOneModuleTalksToTheService()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot(), "js"), "*.js"))
        {
            if (Path.GetFileName(file) is "api.js")
            {
                continue;
            }

            if ((await File.ReadAllTextAsync(file)).Contains("fetch(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        // One door for every request is what makes "a 401 anywhere lands on login, once" a code
        // path rather than a convention each section has to remember.
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task OnlyOneModuleOpensAStream()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot(), "js"), "*.js"))
        {
            if (Path.GetFileName(file) is "events.js")
            {
                continue;
            }

            if ((await File.ReadAllTextAsync(file)).Contains("new EventSource", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        // Six connections per origin on plain HTTP/1.1, and a stream holds one for as long as it
        // is open. One per section would spend the budget and requests would hang with no error.
        Assert.Empty(offenders);
    }

    private static string WebRoot() => Repository.WebRoot;
}
