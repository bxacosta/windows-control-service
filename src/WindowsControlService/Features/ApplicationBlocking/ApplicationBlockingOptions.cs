namespace WindowsControlService.Features.ApplicationBlocking;

public sealed class ApplicationBlockingOptions
{
    public const string Section = "ApplicationBlocking";

    /// <summary>
    /// How often the deployed policy is compared against the database. An administrator can run
    /// <c>CiTool --remove-policy</c> and nothing can prevent that; this is what notices.
    /// </summary>
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromMinutes(1);
}
