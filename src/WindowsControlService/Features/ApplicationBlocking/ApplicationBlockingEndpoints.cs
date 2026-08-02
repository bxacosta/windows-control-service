using Microsoft.AspNetCore.Http.HttpResults;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.ApplicationBlocking;

/// <remarks>
/// Thin on purpose: the framework validates the shape, the service decides, and
/// <c>ToHttpResult()</c> maps the failure. No endpoint builds a ProblemDetails of its own.
/// </remarks>
public static class ApplicationBlockingEndpoints
{
    public static IEndpointRouteBuilder MapApplicationBlocking(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/applications").RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("ListBlockedApplications");

        // Declared before the {id} route and constrained to long, so "policy-state" can never be
        // parsed as an identifier.
        group.MapGet("/policy-state", GetPolicyStateAsync).WithName("GetPolicyState");

        group.MapGet("/{id:long}", GetByIdAsync).WithName("GetBlockedApplication");
        group.MapPost("/", AddAsync).WithName("AddBlockedApplication");
        group.MapPatch("/{id:long}", SetEnabledAsync).WithName("SetBlockedApplicationEnabled");
        group.MapDelete("/{id:long}", RemoveAsync).WithName("RemoveBlockedApplication");

        // Outside the /api/applications group by route, inside this feature by purpose: the
        // running process list exists only so a blocked application can be picked from a list
        // instead of having its path typed by hand.
        endpoints.MapGet("/api/processes", ListProcesses)
            .RequireAuthorization()
            .WithName("ListRunningApplications");

        return endpoints;
    }

    private static Ok<IReadOnlyList<RunningApplication>> ListProcesses(IProcessInventory inventory) =>
        TypedResults.Ok(inventory.GetRunningApplications());

    private static async Task<Ok<IReadOnlyList<BlockedApplication>>> ListAsync(
        HttpContext context,
        IApplicationBlockingService blocking) =>
        TypedResults.Ok(await blocking.GetAllAsync(context.RequestAborted));

    private static async Task<Results<Ok<BlockedApplication>, ProblemHttpResult>> GetByIdAsync(
        long id,
        HttpContext context,
        IApplicationBlockingService blocking)
    {
        var result = await blocking.GetByIdAsync(id, context.RequestAborted);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToHttpResult();
    }

    private static async Task<Results<Created<AddApplicationResponse>, ProblemHttpResult>> AddAsync(
        AddApplicationRequest request,
        HttpContext context,
        IApplicationBlockingService blocking)
    {
        var result = await blocking.AddAsync(request.ExecutablePath, request.Name, context.RequestAborted);

        return result.IsSuccess
            ? TypedResults.Created($"/api/applications/{result.Value}", new AddApplicationResponse(result.Value))
            : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok, ProblemHttpResult>> SetEnabledAsync(
        long id,
        SetApplicationEnabledRequest request,
        HttpContext context,
        IApplicationBlockingService blocking)
    {
        var result = await blocking.SetEnabledAsync(id, request.Enabled!.Value, context.RequestAborted);

        return result.IsSuccess ? TypedResults.Ok() : result.Error.ToHttpResult();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        long id,
        HttpContext context,
        IApplicationBlockingService blocking)
    {
        var result = await blocking.RemoveAsync(id, context.RequestAborted);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok<PolicyStateResponse>, ProblemHttpResult>> GetPolicyStateAsync(
        HttpContext context,
        IApplicationBlockingService blocking)
    {
        var result = await blocking.GetPolicyStateAsync(context.RequestAborted);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToHttpResult();
    }
}
