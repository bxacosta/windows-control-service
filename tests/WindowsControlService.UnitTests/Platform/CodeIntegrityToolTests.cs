using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;
using WindowsControlService.UnitTests.Fakes;

namespace WindowsControlService.UnitTests.Platform;

/// <summary>
/// Drives the tool with a fake process runner. Every trap in reading CiTool's output lives
/// here; the real-system check is a separate, read-only integration test.
/// </summary>
public sealed class CodeIntegrityToolTests
{
    private const string PolicyId = "{4F1F6B3C-9C21-4E4B-BE1E-6E2E5A5D0A11}";

    private readonly FakeProcessRunner _processRunner = new();

    private CodeIntegrityTool Tool => new(
        _processRunner,
        Options.Create(new CodeIntegrityOptions()),
        NullLogger<CodeIntegrityTool>.Instance);

    [Fact]
    public async Task WellFormedJsonWithOnlyAnOperationResultIsUnknown()
    {
        // The test that matters. This payload is how CiTool reports failure: valid JSON, no
        // Policies array. Reading it as "nothing is installed" makes the reconciliation worker
        // reinstall the policy every minute, forever.
        _processRunner.Default = new ProcessResult(0, """{"OperationResult":-2147024891}""", string.Empty);

        var state = await Tool.GetPolicyStateAsync(PolicyId);

        Assert.True(state.IsSuccess);
        Assert.Equal(PolicyState.Unknown, state.Value);
    }

    [Fact]
    public async Task ANonZeroExitCodeIsUnknown()
    {
        _processRunner.Default = new ProcessResult(1, string.Empty, "access denied");

        Assert.Equal(PolicyState.Unknown, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task ATimeoutIsUnknown()
    {
        _processRunner.Default = new ProcessResult(ProcessResult.TimedOutExitCode, string.Empty, string.Empty);

        Assert.Equal(PolicyState.Unknown, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task OutputThatIsNotJsonIsUnknown()
    {
        _processRunner.Default = new ProcessResult(0, "Press Enter to Continue", string.Empty);

        Assert.Equal(PolicyState.Unknown, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task JsonWithoutAPoliciesArrayIsUnknown()
    {
        _processRunner.Default = new ProcessResult(0, """{"Something":"else"}""", string.Empty);

        Assert.Equal(PolicyState.Unknown, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task AnEmptyPolicyListIsNotEnforced()
    {
        // Distinct from the case above on purpose: this one really does mean "nothing installed".
        _processRunner.Default = new ProcessResult(0, """{"Policies":[],"OperationResult":0}""", string.Empty);

        Assert.Equal(PolicyState.NotEnforced, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task OurPolicyPresentAndEnforcedIsEnforced()
    {
        _processRunner.Default = new ProcessResult(0, ListWith(PolicyId, enforced: true), string.Empty);

        Assert.Equal(PolicyState.Enforced, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task OurPolicyPresentButNotEnforcedIsNotEnforced()
    {
        _processRunner.Default = new ProcessResult(0, ListWith(PolicyId, enforced: false), string.Empty);

        Assert.Equal(PolicyState.NotEnforced, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task SomeoneElsesPolicyIsNotOurs()
    {
        _processRunner.Default = new ProcessResult(
            0,
            ListWith("d2bda982-ccf6-4344-ac5b-0b44427b6816", enforced: true),
            string.Empty);

        Assert.Equal(PolicyState.NotEnforced, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task PolicyIdsMatchWithoutBracesAndWithoutRegardToCase()
    {
        // CiTool prints ids unbraced and in its own casing, while ours is a braced constant.
        _processRunner.Default = new ProcessResult(
            0,
            ListWith("4f1f6b3c-9c21-4e4b-be1e-6e2e5a5d0a11", enforced: true),
            string.Empty);

        Assert.Equal(PolicyState.Enforced, (await Tool.GetPolicyStateAsync(PolicyId)).Value);
    }

    [Fact]
    public async Task RemovingAPolicyThatIsNotInstalledSucceedsWithoutCallingCiTool()
    {
        _processRunner.Default = new ProcessResult(0, """{"Policies":[],"OperationResult":0}""", string.Empty);

        var result = await Tool.RemovePolicyAsync(PolicyId);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(_processRunner.Calls, call => call.Arguments.Contains("--remove-policy"));
    }

    [Fact]
    public async Task RemovingAnInstalledPolicyCallsCiToolWithABracedId()
    {
        _processRunner
            .When("--list-policies", new ProcessResult(0, ListWith(PolicyId, enforced: true), string.Empty))
            .When("--remove-policy", new ProcessResult(0, """{"OperationResult":0}""", string.Empty));

        var result = await Tool.RemovePolicyAsync(PolicyId);

        Assert.True(result.IsSuccess);
        var removal = Assert.Single(_processRunner.Calls, call => call.Arguments.Contains("--remove-policy"));
        Assert.Contains(removal.Arguments, argument => string.Equals(argument, PolicyId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RemovalDoesNotProceedWhenTheStateCannotBeRead()
    {
        // Removing blind after a failed query could tear down a policy the database still wants.
        _processRunner.Default = new ProcessResult(1, string.Empty, "boom");

        var result = await Tool.RemovePolicyAsync(PolicyId);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.OperationFailed, result.Error.Code);
        Assert.DoesNotContain(_processRunner.Calls, call => call.Arguments.Contains("--remove-policy"));
    }

    [Fact]
    public async Task AnEmptyPolicyDocumentIsRejectedBeforeTouchingTheSystem()
    {
        var result = await Tool.ApplyPolicyAsync(ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Invalid, result.Error.Code);
        Assert.Empty(_processRunner.Calls);
    }

    private static string ListWith(string policyId, bool enforced) =>
        $$"""
        {"Policies":[{"PolicyID":"{{policyId.Trim('{', '}')}}","FriendlyName":"WindowsControlService","IsEnforced":{{(enforced ? "true" : "false")}}}],"OperationResult":0}
        """;
}
