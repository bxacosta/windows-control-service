using System.Globalization;
using Dapper;
using WindowsControlService.Infrastructure.Database;

namespace WindowsControlService.Features.ApplicationBlocking;

public interface IBlockedApplicationRepository
{
    Task<IReadOnlyList<BlockedApplication>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockedApplication>> GetEnabledAsync(CancellationToken cancellationToken);

    Task<BlockedApplication?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<bool> ExistsByPathAsync(string executablePath, CancellationToken cancellationToken);

    Task<long> InsertAsync(BlockedApplication application, CancellationToken cancellationToken);

    Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}

public sealed class BlockedApplicationRepository(IDbConnectionFactory connectionFactory)
    : IBlockedApplicationRepository
{
    private const string SelectColumns =
        "SELECT Id, Name, ExecutablePath, MatchAttribute, MatchValue, ProductName, IsEnabled, CreatedAt FROM BlockedApplications";

    public async Task<IReadOnlyList<BlockedApplication>> GetAllAsync(CancellationToken cancellationToken) =>
        await QueryAsync($"{SelectColumns} ORDER BY Id;", parameters: null, cancellationToken);

    public async Task<IReadOnlyList<BlockedApplication>> GetEnabledAsync(CancellationToken cancellationToken) =>
        await QueryAsync($"{SelectColumns} WHERE IsEnabled = 1 ORDER BY Id;", parameters: null, cancellationToken);

    public async Task<BlockedApplication?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync($"{SelectColumns} WHERE Id = @Id;", new { Id = id }, cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<bool> ExistsByPathAsync(string executablePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        // COLLATE NOCASE, matching the unique index: Windows paths do not differ by case, and a
        // second entry for the same executable would produce a second rule for one file.
        var count = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM BlockedApplications WHERE ExecutablePath = @Path COLLATE NOCASE;",
                new { Path = executablePath },
                cancellationToken: cancellationToken));

        return count > 0;
    }

    public async Task<long> InsertAsync(BlockedApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                INSERT INTO BlockedApplications
                    (Name, ExecutablePath, MatchAttribute, MatchValue, ProductName, IsEnabled, CreatedAt)
                VALUES
                    (@Name, @ExecutablePath, @MatchAttribute, @MatchValue, @ProductName, @IsEnabled, @CreatedAt);
                SELECT last_insert_rowid();
                """,
                new
                {
                    application.Name,
                    application.ExecutablePath,
                    MatchAttribute = application.MatchAttribute.ToString(),
                    application.MatchValue,
                    application.ProductName,
                    IsEnabled = application.IsEnabled ? 1 : 0,
                    CreatedAt = application.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE BlockedApplications SET IsEnabled = @Enabled WHERE Id = @Id;",
                new { Id = id, Enabled = enabled ? 1 : 0 },
                cancellationToken: cancellationToken));

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM BlockedApplications WHERE Id = @Id;",
                new { Id = id },
                cancellationToken: cancellationToken));

        return affected > 0;
    }

    private async Task<IReadOnlyList<BlockedApplication>> QueryAsync(
        string sql,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<BlockedApplicationRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        return [.. rows.Select(row => row.ToApplication())];
    }

    /// <summary>
    /// Read as text and parsed here rather than letting the driver hand back a
    /// <see cref="DateTime"/>. Plain parsing converts to local time and returns
    /// <see cref="DateTimeKind.Local"/>, which silently breaks every later comparison against
    /// UTC; <see cref="DateTimeStyles.RoundtripKind"/> is what keeps the value honest.
    /// </summary>
    private sealed record BlockedApplicationRow(
        long Id,
        string Name,
        string ExecutablePath,
        string MatchAttribute,
        string MatchValue,
        string? ProductName,
        long IsEnabled,
        string CreatedAt)
    {
        public BlockedApplication ToApplication() => new()
        {
            Id = Id,
            Name = Name,
            ExecutablePath = ExecutablePath,
            MatchAttribute = ParseMatchAttribute(Id, MatchAttribute),
            MatchValue = MatchValue,
            ProductName = ProductName,
            IsEnabled = IsEnabled != 0,
            CreatedAt = DateTime.Parse(CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };

    }

    /// <summary>
    /// The reading end of the barrier. The CHECK constraint stops a bad value going in, and this
    /// stops one that is already there coming out: the value becomes the name of an attribute in
    /// a deployed policy, so a row nobody can turn into a rule has to fail here, loudly and with
    /// the id, rather than three layers later while the policy is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Enum.TryParse</c> alone does not validate anything. It also accepts the numeric form
    /// and answers <see langword="true"/> with a value that is no member at all: <c>"7"</c>
    /// parses, and <c>ToString()</c> gives back <c>"7"</c>, which is what would reach the policy
    /// as an attribute name.
    /// </para>
    /// <para>
    /// <c>Enum.IsDefined</c> closes that one but not <c>"0"</c>, which parses to a real member
    /// and would silently become <c>FileName</c>. So the rule is stricter than either: the
    /// stored text has to be exactly the name of a member. It always is, because that is what
    /// the insert writes; anything else came from somewhere this service cannot vouch for.
    /// </para>
    /// </remarks>
    internal static RuleMatchField ParseMatchAttribute(long id, string stored) =>
        Enum.TryParse<RuleMatchField>(stored, ignoreCase: false, out var parsed)
        && Enum.IsDefined(parsed)
        && string.Equals(stored, parsed.ToString(), StringComparison.Ordinal)
            ? parsed
            : throw new InvalidOperationException(
                $"Blocked application {id} records MatchAttribute '{stored}', which is not a WDAC "
                + "rule attribute this service can write.");
}
