using System.Globalization;
using Dapper;

namespace WindowsControlService.Infrastructure.Database;

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>Writes every pair inside one transaction.</summary>
    /// <remarks>
    /// Not a convenience. The password is two values, the hash and the salt, and they have to
    /// land together: written separately, a failure in between leaves a new salt beside an old
    /// hash. No password ever validates again, and because the hash still exists the initial
    /// setup refuses to run a second time. The service becomes permanently unreachable.
    /// </remarks>
    Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken);
}

public sealed class SettingsRepository(IDbConnectionFactory connectionFactory, TimeProvider timeProvider)
    : ISettingsRepository
{
    private const string UpsertSql = """
        INSERT INTO Settings (Key, Value, UpdatedAt)
        VALUES (@Key, @Value, @UpdatedAt)
        ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = excluded.UpdatedAt;
        """;

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(
                "SELECT Value FROM Settings WHERE Key = @Key;",
                new { Key = key },
                cancellationToken: cancellationToken));
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await SetManyAsync(new Dictionary<string, string> { [key] = value }, cancellationToken);
    }

    public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return;
        }

        var updatedAt = timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var (key, value) in values)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    UpsertSql,
                    new { Key = key, Value = value, UpdatedAt = updatedAt },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
