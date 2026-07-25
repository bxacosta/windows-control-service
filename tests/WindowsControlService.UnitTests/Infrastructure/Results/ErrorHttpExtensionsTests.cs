using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.UnitTests.Infrastructure.Results;

public sealed class ErrorHttpExtensionsTests
{
    private static readonly Dictionary<ErrorCode, int> Expected = new()
    {
        [ErrorCode.NotFound] = 404,
        [ErrorCode.Conflict] = 409,
        [ErrorCode.Invalid] = 400,
        [ErrorCode.AccessDenied] = 403,
        [ErrorCode.PlatformUnavailable] = 503,
        [ErrorCode.OperationFailed] = 500,
    };

    public static TheoryData<ErrorCode> AllErrorCodes()
    {
        var data = new TheoryData<ErrorCode>();
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllErrorCodes))]
    public void EveryErrorCodeMapsToItsStatusCode(ErrorCode code)
    {
        Assert.True(Expected.ContainsKey(code), $"{code} has no expected status code in this test.");

        Assert.Equal(Expected[code], new Error(code, "message").ToStatusCode());
    }

    [Fact]
    public void TheMappingCoversTheWholeEnum()
    {
        // Adding a value to ErrorCode without mapping it fails the build (CS8509, no default
        // arm). This keeps the test suite honest about it too.
        Assert.Equal(Enum.GetValues<ErrorCode>().Length, Expected.Count);
    }

    [Fact]
    public void ProblemResultCarriesTheMessageAndStatus()
    {
        var result = new Error(ErrorCode.Invalid, "The path is not absolute.").ToHttpResult();

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("The path is not absolute.", result.ProblemDetails.Detail);
    }
}
