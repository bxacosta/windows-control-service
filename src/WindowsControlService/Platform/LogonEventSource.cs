using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;

namespace WindowsControlService.Platform;

/// <inheritdoc cref="ILogonEventSource"/>
public sealed class LogonEventSource : ILogonEventSource
{
    public const string DefaultChannel = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";

    private const int LogonEventId = 21;
    private const int LogoffEventId = 23;
    private const int DisconnectEventId = 24;
    private const int ReconnectEventId = 25;

    private readonly ILogger<LogonEventSource> _logger;

    public LogonEventSource(ILogger<LogonEventSource> logger)
        : this(logger, DefaultChannel)
    {
    }

    /// <summary>
    /// Lets a test point the reader at a channel that does not exist. Proving that an
    /// unreadable log yields an empty list instead of an exception is not provable otherwise.
    /// </summary>
    internal LogonEventSource(ILogger<LogonEventSource> logger, string channel)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        _logger = logger;
        Channel = channel;
    }

    public string Channel { get; }

    public IReadOnlyList<LogonEvent> Read(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var events = new List<LogonEvent>();

        try
        {
            // Filtered on the server so Windows discards what we do not want before handing over
            // anything. timediff(@SystemTime) is milliseconds from the event until now.
            var xpath = string.Create(
                CultureInfo.InvariantCulture,
                $"*[System[(EventID={LogonEventId} or EventID={LogoffEventId} or EventID={DisconnectEventId} "
                + $"or EventID={ReconnectEventId}) and TimeCreated[timediff(@SystemTime) <= {(long)window.TotalMilliseconds}]]]");

            var query = new EventLogQuery(Channel, PathType.LogName, xpath) { TolerateQueryErrors = true };
            using var reader = new EventLogReader(query);

            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    if (Parse(record) is { } logonEvent)
                    {
                        events.Add(logonEvent);
                    }
                }
            }
        }
        catch (EventLogNotFoundException exception)
        {
            _logger.LogWarning(exception, "The event channel {Channel} does not exist on this machine.", Channel);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Not allowed to read the event channel {Channel}.", Channel);
        }
        catch (EventLogException exception)
        {
            _logger.LogWarning(exception, "The event channel {Channel} could not be read.", Channel);
        }

        return events;
    }

    private LogonEvent? Parse(EventRecord record)
    {
        try
        {
            if (record.Id is not (LogonEventId or LogoffEventId or DisconnectEventId or ReconnectEventId)
                || record.RecordId is not { } recordId
                || record.TimeCreated is not { } timeCreated)
            {
                return null;
            }

            var payload = XDocument.Parse(record.ToXml());

            // By element name, never by position: event 23 carries no Address at all, so index
            // based reading shifts every later field and quietly corrupts the lot.
            var user = ElementValue(payload, "User") ?? string.Empty;
            var address = ElementValue(payload, "Address");
            var sessionId = int.TryParse(ElementValue(payload, "SessionID"), CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;

            return new LogonEvent(
                Channel: Channel,
                RecordId: recordId,
                EventId: record.Id,
                Kind: ToKind(record.Id),
                OccurredAt: timeCreated.ToUniversalTime(),
                UserName: user,
                SessionId: sessionId,
                Address: address,
                Origin: ToOrigin(address));
        }
        catch (Exception exception) when (exception
            is System.Xml.XmlException
            or EventLogException
            or FormatException)
        {
            _logger.LogWarning(exception, "Skipped an unreadable record in {Channel}.", Channel);
            return null;
        }
    }

    private static LogonEventKind ToKind(int eventId) => eventId switch
    {
        LogonEventId => LogonEventKind.Logon,
        ReconnectEventId => LogonEventKind.Reconnect,
        DisconnectEventId => LogonEventKind.Disconnect,
        LogoffEventId => LogonEventKind.Logoff,
        _ => LogonEventKind.Logoff,
    };

    internal static LogonOrigin ToOrigin(string? address) => address switch
    {
        null or "" => LogonOrigin.Unknown,
        _ when string.IsNullOrWhiteSpace(address) => LogonOrigin.Unknown,
        _ when string.Equals(address.Trim(), "LOCAL", StringComparison.OrdinalIgnoreCase) => LogonOrigin.Local,
        _ => LogonOrigin.Remote,
    };

    /// <summary>Matches on local name so the varying payload namespaces do not matter.</summary>
    private static string? ElementValue(XDocument payload, string localName)
    {
        var value = payload.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
