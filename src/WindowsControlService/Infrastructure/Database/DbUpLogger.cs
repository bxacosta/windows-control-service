using System.Globalization;
using DbUp.Engine.Output;

namespace WindowsControlService.Infrastructure.Database;

/// <summary>Sends DbUp's own output through the application logger instead of the console.</summary>
internal sealed class DbUpLogger(ILogger logger) : IUpgradeLog
{
    public void LogTrace(string format, params object[] args) => Write(LogLevel.Trace, format, args);

    public void LogDebug(string format, params object[] args) => Write(LogLevel.Debug, format, args);

    public void LogInformation(string format, params object[] args) => Write(LogLevel.Information, format, args);

    public void LogWarning(string format, params object[] args) => Write(LogLevel.Warning, format, args);

    public void LogError(string format, params object[] args) => Write(LogLevel.Error, format, args);

    public void LogError(Exception ex, string format, params object[] args) =>
        logger.Log(LogLevel.Error, ex, "DbUp: {Message}", Format(format, args));

    private void Write(LogLevel level, string format, object[] args)
    {
        // Checked first because Format allocates, and DbUp logs one line per script it inspects.
        if (!logger.IsEnabled(level))
        {
            return;
        }

        logger.Log(level, "DbUp: {Message}", Format(format, args));
    }

    // DbUp hands us a composite format string, which is not a logging template: it has to be
    // rendered here or the structured sink records "{0}" verbatim.
    private static string Format(string format, object[] args) =>
        args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
}
