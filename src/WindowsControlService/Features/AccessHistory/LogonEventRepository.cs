using System.Globalization;
using Dapper;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Platform;

namespace WindowsControlService.Features.AccessHistory;

/// <summary>A <see cref="LogonEvent"/> with the identity it got when it was stored.</summary>
public sealed record StoredLogonEvent(long Id, LogonEvent Event);

public interface ILogonEventRepository
{
    /// <summary>Inserts what is not already there, in one transaction. Returns how many were new.</summary>
    Task<int> InsertMissingAsync(IEnumerable<LogonEvent> events, CancellationToken cancellationToken);

    /// <summary>Every stored event, oldest first.</summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a paged SELECT. Durations and inherited origins need the neighbouring
    /// events, and the origin filter is applied after deriving them, so paging in SQL would give
    /// wrong durations at every page boundary.
    /// </para>
    /// <para>
    /// <b>This is the line to revisit if the volume grows.</b> The measured rate is about three
    /// events a day, roughly ninety rows in a thirty day window, which makes loading everything
    /// irrelevant. At tens of thousands of rows it stops being true and the pairing would have
    /// to move into ingestion, or into window functions.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<StoredLogonEvent>> GetAllAscendingAsync(CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}

public sealed class LogonEventRepository(IDbConnectionFactory connectionFactory) : ILogonEventRepository
{
    public async Task<int> InsertMissingAsync(IEnumerable<LogonEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        var batch = events.ToList();
        if (batch.Count == 0)
        {
            return 0;
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var inserted = 0;
        foreach (var logonEvent in batch)
        {
            // INSERT OR IGNORE against the (Channel, RecordId, OccurredAt) key. Re-reading the
            // whole window every cycle is what removes all the catch-up code.
            inserted += await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT OR IGNORE INTO LogonEvents
                        (Channel, RecordId, EventId, Kind, OccurredAt, UserName, SessionId, Address, Origin)
                    VALUES
                        (@Channel, @RecordId, @EventId, @Kind, @OccurredAt, @UserName, @SessionId, @Address, @Origin);
                    """,
                    new
                    {
                        logonEvent.Channel,
                        logonEvent.RecordId,
                        logonEvent.EventId,
                        Kind = logonEvent.Kind.ToString(),
                        OccurredAt = logonEvent.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                        logonEvent.UserName,
                        logonEvent.SessionId,
                        logonEvent.Address,
                        Origin = logonEvent.Origin.ToString(),
                    },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return inserted;
    }

    public async Task<IReadOnlyList<StoredLogonEvent>> GetAllAscendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<LogonEventRow>(
            new CommandDefinition(
                """
                SELECT Id, Channel, RecordId, EventId, Kind, OccurredAt, UserName, SessionId, Address, Origin
                FROM LogonEvents
                ORDER BY OccurredAt ASC, Id ASC;
                """,
                cancellationToken: cancellationToken));

        return [.. rows.Select(row => row.ToStoredEvent())];
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM LogonEvents;", cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Every integer column is a <see cref="long"/> because SQLite hands back INTEGER as Int64.
    /// Declaring EventId as int makes Dapper fail to find a matching constructor, at runtime,
    /// with a message that does not mention which column is to blame.
    /// </summary>
    private sealed record LogonEventRow(
        long Id,
        string Channel,
        long RecordId,
        long EventId,
        string Kind,
        string OccurredAt,
        string UserName,
        long? SessionId,
        string? Address,
        string Origin)
    {
        public StoredLogonEvent ToStoredEvent() => new(
            Id,
            new LogonEvent(
                Channel: Channel,
                RecordId: RecordId,
                EventId: (int)EventId,
                Kind: Enum.Parse<LogonEventKind>(Kind),
                // RoundtripKind, always: plain parsing converts to local time and hands back
                // Kind=Local, which breaks every later comparison against UTC in silence.
                OccurredAt: DateTime.Parse(OccurredAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                UserName: UserName,
                SessionId: (int?)SessionId,
                Address: Address,
                Origin: Enum.Parse<LogonOrigin>(Origin)));
    }
}
