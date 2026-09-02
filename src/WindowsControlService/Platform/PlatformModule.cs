namespace WindowsControlService.Platform;

public static class PlatformModule
{
    /// <summary>
    /// Everything that talks to Windows, registered in one place. Features depend on these
    /// interfaces and never on the implementations, which is what lets the integration tests
    /// swap the whole layer for doubles.
    /// </summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CodeIntegrityOptions>()
            .Bind(configuration.GetSection(CodeIntegrityOptions.Section))
            .Validate(
                options => options.OperationTimeout >= TimeSpan.FromSeconds(1),
                $"{CodeIntegrityOptions.Section}:{nameof(CodeIntegrityOptions.OperationTimeout)} must be at least one second.")
            .ValidateOnStart();

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPortableExecutableReader, PortableExecutableReader>();
        services.AddSingleton<ICodeIntegrityTool, CodeIntegrityTool>();
        services.AddSingleton<IUsbStorageSwitch, UsbStorageSwitch>();
        services.AddSingleton<IProcessInventory, ProcessInventory>();
        services.AddSingleton<ILogonEventSource, LogonEventSource>();
        services.AddSingleton<IMachineIdentity, MachineIdentity>();

        return services;
    }
}
