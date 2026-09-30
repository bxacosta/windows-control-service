namespace WindowsControlService.Infrastructure.Hosting;

/// <summary>Identity of the service. These are constants, not configuration.</summary>
public static class ServiceConstants
{
    public const string Name = "WindowsControlService";

    /// <summary>Default listening address. Overridable with <c>--urls</c>; localhost only.</summary>
    public const string DefaultUrl = "http://localhost:5150";
}
