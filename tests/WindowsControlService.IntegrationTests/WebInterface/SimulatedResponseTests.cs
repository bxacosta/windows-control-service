using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowsControlService.IntegrationTests.WebInterface;

/// <summary>
/// Keeps the canned answers in <c>scripts/interface-dom.mjs</c> honest about what this service
/// can actually produce.
/// </summary>
/// <remarks>
/// <para>
/// The harness renders the interface against fixed data. That is what makes it deterministic and
/// what lets it run without touching this machine -- and it is also a second copy of the API's
/// vocabulary, which can drift from the first with nothing to notice. It has drifted twice, and
/// both times the same way: the simulated data said what the browser assumed instead of what the
/// service answers, so every scenario passed with the interface visibly wrong.
/// </para>
/// <para>
/// Once the mock only ever produced <c>Logon</c> and <c>Logoff</c>, while the machine records
/// mostly <c>Reconnect</c> and <c>Disconnect</c> -- every reconnection was drawn as a
/// disconnection. Once it said <c>status: 'healthy'</c>, a value <c>HealthEndpoints</c> cannot
/// return, and the health dot never went green.
/// </para>
/// <para>
/// So this checks the two things that broke, and only those. Comparing whole payloads was
/// considered and rejected: the shapes already agree, real data and canned data differ by
/// design, and a check that has to be taught which differences are allowed is a check nobody
/// keeps. What matters is the closed sets -- the values the browser can be tempted to compare
/// against.
/// </para>
/// </remarks>
public sealed class SimulatedResponseTests
{
    private static readonly string Harness =
        File.ReadAllText(Path.Combine(Repository.Root, "scripts", "interface-dom.mjs"));

    /// <summary>
    /// Every enum that reaches a client through a response, found by reflection rather than
    /// listed: a new one added to a new response is covered the day it is written, which a list
    /// in this file would not be.
    /// </summary>
    private static IEnumerable<(string Property, Type Enum)> ContractEnums() =>
        typeof(WindowsControlService.Features.Health.HealthResponse).Assembly.GetTypes()
            .Where(type => type.IsPublic
                && type.Namespace?.StartsWith("WindowsControlService.Features", StringComparison.Ordinal) == true
                && !type.Name.EndsWith("Request", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties())
            .Select(property => (
                Property: JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                Enum: Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .Where(pair => pair.Enum.IsEnum)
            .Distinct();

    [Fact]
    public void EveryValueAnEnumCanTakeAppearsInTheSimulatedAnswers()
    {
        var uncovered = new List<string>();

        foreach (var (property, type) in ContractEnums())
        {
            foreach (var name in Enum.GetNames(type))
            {
                if (!Harness.Contains($"{property}: '{name}'", StringComparison.Ordinal))
                {
                    uncovered.Add($"{type.Name}.{name} (as \"{property}: '{name}'\")");
                }
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "The interface harness never renders these values, so nothing would notice if the "
            + "interface got them wrong. Add a scenario that produces each:\n  "
            + string.Join("\n  ", uncovered));
    }

    [Fact]
    public void NoSimulatedAnswerUsesAValueTheServiceCannotProduce()
    {
        var invented = new List<string>();

        foreach (var (property, type) in ContractEnums())
        {
            var allowed = Enum.GetNames(type);

            // Deliberately literal: it reads the canned objects as they are written, which is
            // also how they are read by whoever edits them. A value spelled some other way is
            // not matched and not checked, and that is a miss rather than a false alarm.
            foreach (Match match in Regex.Matches(Harness, $"{Regex.Escape(property)}: '([A-Za-z]+)'"))
            {
                var value = match.Groups[1].Value;
                if (!allowed.Contains(value, StringComparer.Ordinal))
                {
                    invented.Add($"\"{property}: '{value}'\" is not a {type.Name} ({string.Join(", ", allowed)})");
                }
            }
        }

        Assert.True(invented.Count == 0, string.Join("\n  ", invented));
    }

    /// <summary>
    /// <c>status</c> is not an enum, it is a constant, which is what made it worth faking wrong
    /// for months. Asked of the running service rather than copied from the source, so that
    /// changing the literal fails here instead of silently making the harness a liar again.
    /// </summary>
    [Fact]
    public async Task TheSimulatedHealthAnswerIsTheOneTheServiceGives()
    {
        using var factory = new ServiceApplicationFactory();
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonElement>("/api/health", CancellationToken.None);
        var status = health.GetProperty("status").GetString();

        Assert.Contains($"status: '{status}'", Harness, StringComparison.Ordinal);
    }
}
