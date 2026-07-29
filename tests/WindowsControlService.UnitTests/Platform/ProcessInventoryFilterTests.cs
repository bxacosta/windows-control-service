using WindowsControlService.Platform;

namespace WindowsControlService.UnitTests.Platform;

public sealed class ProcessInventoryFilterTests
{
    private const string Own = @"C:\Program Files\WindowsControlService\WindowsControlService.exe";

    [Fact]
    public void WindowsBinariesAreExcluded()
    {
        var kept = ProcessInventory.FilterExecutablePaths([@"C:\Windows\notepad.exe"], Own);

        Assert.Empty(kept);
    }

    [Fact]
    public void WindowsDotOldIsKept()
    {
        // The trailing-separator bug: "C:\Windows.old" starts with "C:\Windows" but is a
        // different directory, left behind by an in-place upgrade.
        var kept = ProcessInventory.FilterExecutablePaths([@"C:\Windows.old\legacy.exe"], Own);

        Assert.Equal([@"C:\Windows.old\legacy.exe"], kept);
    }

    [Fact]
    public void StoreApplicationsAreKept()
    {
        var path = @"C:\Program Files\WindowsApps\SomeStoreApp\app.exe";

        Assert.Equal([path], ProcessInventory.FilterExecutablePaths([path], Own));
    }

    [Fact]
    public void TheServiceDoesNotListItself()
    {
        var kept = ProcessInventory.FilterExecutablePaths([Own, @"D:\Games\game.exe"], Own);

        Assert.Equal([@"D:\Games\game.exe"], kept);
    }

    [Fact]
    public void TheServiceIsMatchedWithoutRegardToCase()
    {
        var kept = ProcessInventory.FilterExecutablePaths([Own.ToUpperInvariant()], Own);

        Assert.Empty(kept);
    }

    [Fact]
    public void SeveralInstancesOfTheSameProgramCollapseIntoOne()
    {
        var kept = ProcessInventory.FilterExecutablePaths(
            [@"D:\Apps\editor.exe", @"D:\APPS\Editor.EXE", @"D:\Apps\editor.exe"],
            Own);

        Assert.Equal([@"D:\Apps\editor.exe"], kept);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"D:\Apps\library.dll")]
    public void NonExecutablesAndBlanksAreDropped(string? path)
    {
        Assert.Empty(ProcessInventory.FilterExecutablePaths([path], Own));
    }

    [Fact]
    public void TheOriginalOrderIsPreserved()
    {
        string[] paths = [@"D:\b.exe", @"D:\a.exe", @"D:\c.exe"];

        Assert.Equal(paths, ProcessInventory.FilterExecutablePaths(paths, Own));
    }
}
