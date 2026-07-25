using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.UnitTests.Infrastructure.Results;

public sealed class ResultTests
{
    [Fact]
    public void SuccessIsSuccessful()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void FailureCarriesCodeAndMessage()
    {
        var result = Result.Failure(ErrorCode.Conflict, "Already registered.");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Conflict, result.Error.Code);
        Assert.Equal("Already registered.", result.Error.Message);
    }

    [Fact]
    public void SuccessOfTExposesTheValue()
    {
        Result<int> result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ValueThrowsOnFailure()
    {
        var result = Result<int>.Failure(ErrorCode.NotFound, "No such application.");

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);

        // The message has to name the failure, otherwise the stack trace alone says nothing.
        Assert.Contains("NotFound", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No such application.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueConvertsImplicitly()
    {
        Result<string> result = "citool.exe";

        Assert.True(result.IsSuccess);
        Assert.Equal("citool.exe", result.Value);
    }

    [Fact]
    public void FailureOfTKeepsTheError()
    {
        var error = new Error(ErrorCode.PlatformUnavailable, "CiTool.exe is not present.");

        var result = Result<string>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
