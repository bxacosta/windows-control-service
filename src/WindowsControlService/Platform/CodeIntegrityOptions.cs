namespace WindowsControlService.Platform;

public sealed class CodeIntegrityOptions
{
    public const string Section = "CodeIntegrity";

    /// <summary>
    /// Applies to each external call separately: converting the XML and updating the policy.
    /// </summary>
    /// <remarks>
    /// <c>HostOptions.ShutdownTimeout</c> must stay above the sum of both, or a stop request can
    /// cut a policy update in half and leave the machine and the database disagreeing.
    /// </remarks>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
