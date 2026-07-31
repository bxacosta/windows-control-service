using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace WindowsControlService.Features.Authentication;

public static class AuthenticationModule
{
    public const string LoginRateLimitPolicy = "login";
    public const string CookieName = "wcs_session";

    internal const string SecurityStampClaim = "wcs:stamp";

    public static IServiceCollection AddAuthenticationFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationOptions.Section))
            .ValidateDataAnnotations()
            .Validate(
                options => options.SessionTimeout >= TimeSpan.FromMinutes(1),
                $"{AuthenticationOptions.Section}:{nameof(AuthenticationOptions.SessionTimeout)} must be at least one minute.")
            .ValidateOnStart();

        services.AddSingleton<IPasswordService, PasswordService>();

        var authentication = configuration.GetSection(AuthenticationOptions.Section).Get<AuthenticationOptions>()
            ?? new AuthenticationOptions();

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;

                // The service is plain HTTP on loopback, so requiring a secure cookie would mean
                // no cookie at all.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.ExpireTimeSpan = authentication.SessionTimeout;
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = ValidateSecurityStampAsync;
            });

        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            // Without this the rejection goes out as 503 Service Unavailable, which a client
            // reads as "the service is down" instead of "you are going too fast".
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter(LoginRateLimitPolicy, limiter =>
            {
                limiter.PermitLimit = authentication.LoginAttemptsPerMinute;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        return services;
    }

    /// <summary>
    /// Compares the stamp in the cookie against the stored one. This is what makes changing the
    /// password sign out every open session, including the one that changed it.
    /// </summary>
    private static async Task ValidateSecurityStampAsync(CookieValidatePrincipalContext context)
    {
        var passwords = context.HttpContext.RequestServices.GetRequiredService<IPasswordService>();

        var current = await passwords.GetSecurityStampAsync(context.HttpContext.RequestAborted);
        var fromCookie = context.Principal?.FindFirst(SecurityStampClaim)?.Value;

        if (current is null
            || fromCookie is null
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(current),
                Encoding.UTF8.GetBytes(fromCookie)))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    internal static ClaimsPrincipal BuildPrincipal(string securityStamp) =>
        new(new ClaimsIdentity(
            [
                // A fixed name: there is one password and no users, so the identity carries no
                // information beyond "this session is signed in".
                new Claim(ClaimTypes.Name, "owner"),
                new Claim(SecurityStampClaim, securityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

    internal static TimeSpan SessionTimeout(this IOptions<AuthenticationOptions> options) =>
        options.Value.SessionTimeout;
}
