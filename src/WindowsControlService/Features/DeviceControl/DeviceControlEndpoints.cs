using Microsoft.AspNetCore.Http.HttpResults;
using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Features.DeviceControl;

public static class DeviceControlEndpoints
{
    public static IEndpointRouteBuilder MapDeviceControl(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/devices").RequireAuthorization();

        group.MapGet("/usb", GetUsbAsync).WithName("GetUsbStorageState");
        group.MapPut("/usb", SetUsbAsync).WithName("SetUsbStorageState");

        return endpoints;
    }

    private static async Task<Results<Ok<UsbBlockStatus>, ProblemHttpResult>> GetUsbAsync(
        HttpContext context,
        IDeviceControlService devices)
    {
        var result = await devices.GetUsbStatusAsync(context.RequestAborted);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok, ProblemHttpResult>> SetUsbAsync(
        SetUsbBlockedRequest request,
        HttpContext context,
        IDeviceControlService devices)
    {
        var result = await devices.SetUsbBlockedAsync(request.Blocked!.Value, context.RequestAborted);

        return result.IsSuccess ? TypedResults.Ok() : result.Error.ToHttpResult();
    }
}
