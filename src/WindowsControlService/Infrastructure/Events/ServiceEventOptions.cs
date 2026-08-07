using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Infrastructure.Events;

public sealed class ServiceEventOptions
{
    public const string Section = "Events";

    /// <summary>
    /// How long one stream is allowed to stay open before the server ends it and the browser
    /// reconnects on its own.
    /// </summary>
    /// <remarks>
    /// This is not a cleanup detail, it is what keeps the session alive. The session cookie has
    /// a sliding expiration, and an open stream sends no further requests: a tab left open for
    /// hours would find its session dead on the next click. Ending the stream forces a fresh
    /// authenticated request, which renews the cookie.
    /// </remarks>
    public TimeSpan StreamLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Events held for a subscriber that is not reading. Beyond this the oldest are dropped.
    /// </summary>
    [Range(1, 1000)]
    public int SubscriberQueueCapacity { get; set; } = 32;
}
