using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using WindowsControlService.Infrastructure.Results;

namespace WindowsControlService.Features.Authentication;

/// <remarks>
/// Every handler declares a concrete result type rather than <see cref="IResult"/>. That is not
/// decoration: .NET 10 only answers 401 instead of redirecting to a login page for endpoints it
/// recognises as API endpoints, and it recognises them from the statically known result type. An
/// <c>IResult</c> return leaves that metadata empty and a protected endpoint answers 302, which
/// is useless to a JSON client. It also gives OpenAPI the response shapes for free.
/// </remarks>
public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationFeature(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/auth").RequireAuthorization();

        group.MapGet("/session", GetSessionAsync)
            .AllowAnonymous()
            .WithName("GetSession");

        // Public only while no password exists. The state is dynamic, so the endpoint is
        // anonymous and the handler answers 409 once one is set. That leaks nothing that
        // GET /api/auth/session does not already say.
        group.MapPost("/password", ConfigurePasswordAsync)
            .AllowAnonymous()
            .WithName("ConfigurePassword");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthenticationModule.LoginRateLimitPolicy)
            .WithName("Login");

        // Cast because LogoutAsync(HttpContext) also fits RequestDelegate, and that overload
        // throws the returned result away. The cast picks the route-handler overload; the
        // concrete Task<Ok> return type still reaches metadata inference through the MethodInfo.
        group.MapPost("/logout", (Delegate)LogoutAsync).WithName("Logout");

        group.MapPut("/password", ChangePasswordAsync).WithName("ChangePassword");

        return endpoints;
    }

    /// <summary>Merges what used to be two calls: is a password set, and is this caller signed in.</summary>
    private static async Task<Ok<SessionResponse>> GetSessionAsync(HttpContext context, IPasswordService passwords) =>
        TypedResults.Ok(new SessionResponse(
            Initialized: await passwords.IsConfiguredAsync(context.RequestAborted),
            Authenticated: context.User.Identity?.IsAuthenticated ?? false));

    private static async Task<Results<Ok, ProblemHttpResult>> ConfigurePasswordAsync(
        ConfigurePasswordRequest request,
        HttpContext context,
        IPasswordService passwords)
    {
        var result = await passwords.ConfigureAsync(request.Password, context.RequestAborted);

        return result.IsSuccess ? TypedResults.Ok() : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IPasswordService passwords)
    {
        var result = await passwords.ValidateAsync(request.Password, context.RequestAborted);
        if (result.IsFailure)
        {
            return result.Error.ToHttpResult();
        }

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthenticationModule.BuildPrincipal(result.Value));

        return TypedResults.Ok();
    }

    private static async Task<Ok> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        IPasswordService passwords)
    {
        var result = await passwords.ChangeAsync(
            request.CurrentPassword,
            request.NewPassword,
            context.RequestAborted);

        if (result.IsFailure)
        {
            return result.Error.ToHttpResult();
        }

        // The stamp rotated, so this caller's cookie is already dead. Clearing it here saves the
        // client one rejected request before it notices.
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return TypedResults.Ok();
    }
}
