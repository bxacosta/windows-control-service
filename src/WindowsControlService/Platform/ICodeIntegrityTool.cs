using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Platform;

public enum PolicyState
{
    /// <summary>CiTool could not be queried. Not the same as "there is no policy" -- do not act on it.</summary>
    Unknown,

    /// <summary>Not installed, or installed and not enforced.</summary>
    NotEnforced,

    Enforced,
}

/// <summary>
/// Deploys and queries WDAC policies. Takes the policy document already built, never the list
/// of blocked applications: producing the XML belongs to the feature, deploying it belongs to
/// the platform.
/// </summary>
/// <remarks>
/// The policy id travels as a parameter rather than living here. Platform code must not depend
/// on a feature for a constant, and the alternative -- Platform reaching into
/// <c>Features/ApplicationBlocking</c> -- inverts the dependency the whole layer exists to
/// keep straight.
/// </remarks>
public interface ICodeIntegrityTool
{
    Task<Result<PolicyState>> GetPolicyStateAsync(string policyId, CancellationToken cancellationToken = default);

    Task<Result> ApplyPolicyAsync(ReadOnlyMemory<byte> policyXml, CancellationToken cancellationToken = default);

    Task<Result> RemovePolicyAsync(string policyId, CancellationToken cancellationToken = default);
}
