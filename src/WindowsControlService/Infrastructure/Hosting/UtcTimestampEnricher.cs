using Serilog.Core;
using Serilog.Events;

namespace WindowsControlService.Infrastructure.Hosting;

/// <summary>
/// Adds the event timestamp in UTC as a property.
/// </summary>
/// <remarks>
/// Serilog stamps events with local time and its output templates have no UTC form, so the
/// file sink would disagree with everything else the service records. The Windows Event Log
/// keeps its own local <c>TimeCreated</c> that no application can influence; correlating the
/// two means reading this property.
/// </remarks>
internal sealed class UtcTimestampEnricher : ILogEventEnricher
{
    public const string PropertyName = "UtcTimestamp";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        logEvent.AddPropertyIfAbsent(
            new LogEventProperty(PropertyName, new ScalarValue(logEvent.Timestamp.UtcDateTime)));
    }
}
