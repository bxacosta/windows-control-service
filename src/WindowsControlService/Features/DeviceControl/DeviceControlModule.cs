using WindowsControlService.Infrastructure.Events;

namespace WindowsControlService.Features.DeviceControl;

public static class DeviceControlModule
{
    public static IServiceCollection AddDeviceControl(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // No options: the registry key and its values are genuine constants, not settings.
        services.AddSingleton<IDeviceControlService, DeviceControlService>();
        services.AddSingleton<IServiceEventSnapshot, UsbStatusSnapshot>();

        return services;
    }
}
