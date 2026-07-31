using WindowsControlService.Infrastructure.Database;

namespace WindowsControlService.UnitTests.Fakes;

internal sealed class FakeSettingsRepository : ISettingsRepository
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Number of SetMany calls, to prove password material is written in one go.</summary>
    public int WriteCount { get; private set; }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken) =>
        SetManyAsync(new Dictionary<string, string> { [key] = value }, cancellationToken);

    public Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        WriteCount++;

        foreach (var (key, value) in values)
        {
            Values[key] = value;
        }

        return Task.CompletedTask;
    }
}
