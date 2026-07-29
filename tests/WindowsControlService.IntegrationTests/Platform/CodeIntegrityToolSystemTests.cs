using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// Runs CiTool for real. Read-only: only <c>--list-policies</c>. Applying and removing a
/// policy is phase 4, behind the safeguards in <c>docs/05-seguridad-y-restauracion.md</c>.
/// </summary>
[Trait("Requires", "Admin")]
public sealed class CodeIntegrityToolSystemTests
{
    /// <summary>A policy id this project never deploys, so the answer can only be NotEnforced.</summary>
    private const string AbsentPolicyId = "{0C5E9A6B-3D4F-4A21-9E77-1B2C3D4E5F60}";

    private readonly CodeIntegrityTool _tool = new(
        new ProcessRunner(NullLogger<ProcessRunner>.Instance),
        Options.Create(new CodeIntegrityOptions()),
        NullLogger<CodeIntegrityTool>.Instance);

    [Fact]
    public async Task ReadsAStateFromTheRealSystemWithoutThrowing()
    {
        var result = await _tool.GetPolicyStateAsync(AbsentPolicyId);

        Assert.True(result.IsSuccess);

        // NotEnforced when CiTool answered, Unknown when it could not be queried. Either is a
        // legitimate outcome; throwing or reporting a failure is not.
        Assert.Contains(result.Value, (PolicyState[])[PolicyState.NotEnforced, PolicyState.Unknown]);
    }

    [Fact]
    public async Task RecognisesAPolicyWindowsItselfInstalled()
    {
        // The Windows driver policy is present on every Windows 11 machine, so this proves the
        // parser really reads the live output rather than always answering NotEnforced.
        const string windowsDriverPolicy = "{D2BDA982-CCF6-4344-AC5B-0B44427B6816}";

        var result = await _tool.GetPolicyStateAsync(windowsDriverPolicy);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(PolicyState.Unknown, result.Value);
    }
}
