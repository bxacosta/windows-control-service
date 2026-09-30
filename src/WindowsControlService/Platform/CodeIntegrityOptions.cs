namespace WindowsControlService.Platform;

public sealed class CodeIntegrityOptions
{
    public const string Section = "CodeIntegrity";

    /// <summary>
    /// Applies to each external call separately: converting the XML and updating the policy.
    /// </summary>
    /// <remarks>
    /// <c>HostOptions.ShutdownTimeout</c> is derived from this in <c>Program.cs</c> to stay above
    /// the sum of both, or a stop request could cut a policy update in half.
    /// </remarks>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
