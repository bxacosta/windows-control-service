namespace WindowsControlService.IntegrationTests.WebInterface;

/// <summary>
/// Rules about the shipped interface that no functional walkthrough would catch, kept as tests
/// so they stay true instead of being remembered. They read the files on disk rather than what
/// the host serves: a rule that only covers the files that happen to exist today is not a rule.
/// </summary>
public sealed class InterfaceAssetTests
{
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

        // The whole point of option A is that the interface works with no internet and no build
        // step. One CDN link would quietly undo both.
        Assert.DoesNotContain("http://", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", content, StringComparison.OrdinalIgnoreCase);
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

    private static string WebRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WindowsControlService.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "WindowsControlService", "wwwroot");
    }
}
