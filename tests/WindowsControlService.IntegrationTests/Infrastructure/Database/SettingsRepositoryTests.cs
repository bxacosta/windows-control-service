using Microsoft.Extensions.Time.Testing;
using WindowsControlService.Infrastructure.Database;

namespace WindowsControlService.IntegrationTests.Infrastructure.Database;

public sealed class SettingsRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 10, 30, 0, TimeSpan.Zero);

    private readonly MigratedDatabase _database;
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ISettingsRepository _repository;

    public SettingsRepositoryTests()
    {
        _database = new MigratedDatabase(services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
        });
        _repository = _database.Get<ISettingsRepository>();
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task AMissingKeyReadsAsNull()
    {
        Assert.Null(await _repository.GetAsync("nothing.here", CancellationToken.None));
    }

    [Fact]
    public async Task AValueSurvivesARoundTrip()
    {
        await _repository.SetAsync("auth.security_stamp", "abc123", CancellationToken.None);

        Assert.Equal("abc123", await _repository.GetAsync("auth.security_stamp", CancellationToken.None));
    }

    [Fact]
    public async Task WritingTheSameKeyTwiceReplacesIt()
    {
        await _repository.SetAsync("k", "first", CancellationToken.None);
        await _repository.SetAsync("k", "second", CancellationToken.None);

        Assert.Equal("second", await _repository.GetAsync("k", CancellationToken.None));
        Assert.Equal(1, await CountRows());
    }

    [Fact]
    public async Task SetManyWritesEveryPair()
    {
        await _repository.SetManyAsync(
            new Dictionary<string, string> { ["hash"] = "h", ["salt"] = "s", ["iterations"] = "210000" },
            CancellationToken.None);

        Assert.Equal("h", await _repository.GetAsync("hash", CancellationToken.None));
        Assert.Equal("s", await _repository.GetAsync("salt", CancellationToken.None));
        Assert.Equal("210000", await _repository.GetAsync("iterations", CancellationToken.None));
    }

    [Fact]
    public async Task AnEmptySetManyIsANoOp()
    {
        await _repository.SetManyAsync(new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(0, await CountRows());
    }

    [Fact]
    public async Task TheTimestampComesFromTheInjectedClock()
    {
        _clock.Advance(TimeSpan.FromDays(2));

        await _repository.SetAsync("k", "v", CancellationToken.None);

        // Round-trip format, UTC, from TimeProvider: no service in this project reads
        // DateTime.UtcNow, and this is what proves it for the repository.
        var stored = await _database.ScalarAsync("SELECT UpdatedAt FROM Settings WHERE Key = 'k';");
        var parsed = DateTime.Parse(
            (string)stored!,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.Equal(Now.AddDays(2).UtcDateTime, parsed);
    }

    private async Task<long> CountRows() => (long)(await _database.ScalarAsync("SELECT COUNT(*) FROM Settings;"))!;
}
