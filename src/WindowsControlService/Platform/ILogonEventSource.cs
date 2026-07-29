namespace WindowsControlService.Platform;

public enum LogonEventKind
{
    Logon,
    Reconnect,
    Disconnect,
    Logoff,
}

public enum LogonOrigin
{
    Unknown,
    Local,
    Remote,
}

/// <param name="OccurredAt">Always UTC.</param>
public sealed record LogonEvent(
    string Channel,
    long RecordId,
    int EventId,
    LogonEventKind Kind,
    DateTime OccurredAt,
    string UserName,
    int? SessionId,
    string? Address,
    LogonOrigin Origin)
{
    public bool IsSessionStart => Kind is LogonEventKind.Logon or LogonEventKind.Reconnect;
}

/// <summary>
/// Reads session transitions from the Terminal Services operational log.
/// </summary>
/// <remarks>
/// <para>
/// That channel rather than the Security log, and the reason is measured, not stylistic: on
/// a real machine the Security log was saturated at 20/20 MB and kept only a few hours,
/// while this one held 160 days in 1 MB because it only ever records session transitions.
/// </para>
/// <para>
/// The reader never throws. A missing channel, insufficient rights or surprising XML are
/// logged and produce an empty list: an unreadable log must not take the ingestion down.
/// </para>
/// </remarks>
public interface ILogonEventSource
{
    IReadOnlyList<LogonEvent> Read(TimeSpan window);
}
