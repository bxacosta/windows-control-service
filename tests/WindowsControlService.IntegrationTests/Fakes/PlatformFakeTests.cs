using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Fakes;

/// <summary>
/// Every HTTP test leans on the doubles, so their own behaviour is pinned here. A
/// fake that silently succeeds when it was told to fail hides the bug it was meant to expose.
/// </summary>
public sealed class PlatformFakeTests
{
    [Fact]
    public async Task ApplyingAPolicyMovesTheFakeToEnforced()
    {
        var tool = new FakeCodeIntegrityTool();

        var result = await tool.ApplyPolicyAsync(new byte[] { 1, 2, 3 });

        Assert.True(result.IsSuccess);
        Assert.Equal(PolicyState.Enforced, (await tool.GetPolicyStateAsync("{id}")).Value);
        Assert.Equal([1, 2, 3], Assert.Single(tool.AppliedDocuments));
    }

    [Fact]
    public async Task AConfiguredFailureIsReturnedAndNothingIsRecorded()
    {
        var tool = new FakeCodeIntegrityTool
        {
            ApplyFailure = new Error(ErrorCode.AccessDenied, "no"),
        };

        var result = await tool.ApplyPolicyAsync(new byte[] { 1 });

        Assert.Equal(ErrorCode.AccessDenied, result.Error.Code);
        Assert.Empty(tool.AppliedDocuments);
        Assert.Equal(PolicyState.NotEnforced, (await tool.GetPolicyStateAsync("{id}")).Value);
    }

    [Fact]
    public void TheUsbSwitchRemembersWhatItWasTold()
    {
        var usb = new FakeUsbStorageSwitch();

        Assert.False(usb.IsBlocked().Value);
        Assert.True(usb.Block().IsSuccess);
        Assert.True(usb.IsBlocked().Value);
        Assert.True(usb.Unblock().IsSuccess);
        Assert.False(usb.IsBlocked().Value);
    }

    [Fact]
    public void TheUsbSwitchCanSimulateAccessDenied()
    {
        var usb = new FakeUsbStorageSwitch
        {
            Failure = new Error(ErrorCode.AccessDenied, "no"),
        };

        Assert.Equal(ErrorCode.AccessDenied, usb.Block().Error.Code);
        Assert.Equal(ErrorCode.AccessDenied, usb.IsBlocked().Error.Code);
        Assert.False(usb.Blocked);
    }

    [Fact]
    public void TheLogonSourceRecordsTheWindowItWasAskedFor()
    {
        var source = new FakeLogonEventSource();

        source.Read(TimeSpan.FromDays(30));

        Assert.Equal(TimeSpan.FromDays(30), Assert.Single(source.RequestedWindows));
    }

    [Fact]
    public void TheExecutableReaderAnswersRegardlessOfPathCasing()
    {
        var reader = new FakePortableExecutableReader();
        reader.WithOriginalFileName(@"C:\Apps\Editor.exe", "editor.exe");

        Assert.Equal("editor.exe", reader.ReadVersionFields(@"c:\apps\editor.EXE").OriginalFileName);
        Assert.Null(reader.ReadVersionFields(@"C:\Apps\other.exe").OriginalFileName);
    }
}
