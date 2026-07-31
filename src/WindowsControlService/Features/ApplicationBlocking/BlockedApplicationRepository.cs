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
        "SELECT Id, Name, ExecutablePath, OriginalFileName, ProductName, IsEnabled, CreatedAt FROM BlockedApplications";

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
                    (Name, ExecutablePath, OriginalFileName, ProductName, IsEnabled, CreatedAt)
                VALUES
                    (@Name, @ExecutablePath, @OriginalFileName, @ProductName, @IsEnabled, @CreatedAt);
                SELECT last_insert_rowid();
                """,
                new
                {
                    application.Name,
                    application.ExecutablePath,
                    application.OriginalFileName,
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
        string OriginalFileName,
        string? ProductName,
        long IsEnabled,
        string CreatedAt)
    {
        public BlockedApplication ToApplication() => new()
        {
            Id = Id,
            Name = Name,
            ExecutablePath = ExecutablePath,
            OriginalFileName = OriginalFileName,
            ProductName = ProductName,
            IsEnabled = IsEnabled != 0,
            CreatedAt = DateTime.Parse(CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }
}
