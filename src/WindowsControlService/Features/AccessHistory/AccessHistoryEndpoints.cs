using Microsoft.AspNetCore.Http.HttpResults;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.AccessHistory;

public static class AccessHistoryEndpoints
{
    public static IEndpointRouteBuilder MapAccessHistory(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/access-history", GetTimelineAsync)
            .RequireAuthorization()
            .WithName("GetAccessHistory");

        return endpoints;
    }

    private static async Task<Results<Ok<AccessHistoryPage>, ProblemHttpResult>> GetTimelineAsync(
        HttpContext context,
        IAccessHistoryService history,
        int? limit = null,
        int? offset = null,
        string? origin = null)
    {
        if (!TryParseOrigin(origin, out var parsed))
        {
            return new Error(ErrorCode.Invalid, "origin must be local, remote or all.").ToHttpResult();
        }

        return TypedResults.Ok(await history.GetTimelineAsync(limit, offset, parsed, context.RequestAborted));
    }

    /// <summary>
    /// Accepts local, remote, all and absent, any casing. <c>Unknown</c> is deliberately not a
    /// valid filter: it is an internal state, not something a caller should ask for.
    /// </summary>
    private static bool TryParseOrigin(string? origin, out LogonOrigin? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(origin) || string.Equals(origin, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(origin, "local", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LogonOrigin.Local;
            return true;
        }

        if (string.Equals(origin, "remote", StringComparison.OrdinalIgnoreCase))
        {
            parsed = LogonOrigin.Remote;
            return true;
        }

        return false;
    }
}
