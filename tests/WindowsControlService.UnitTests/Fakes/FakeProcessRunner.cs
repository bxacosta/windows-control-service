using WindowsControlService.Platform;

namespace WindowsControlService.UnitTests.Fakes;

/// <summary>
/// Hand-written double, not a mocking framework: there are few of these, they are explicit, and
/// when one fails the reason is readable.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<Func<string, IReadOnlyList<string>, ProcessResult?>> _responses = [];

    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public ProcessResult Default { get; set; } = new(0, string.Empty, string.Empty);

    /// <summary>Answers with <paramref name="result"/> when every fragment appears in the arguments.</summary>
    public FakeProcessRunner When(string argumentFragment, ProcessResult result)
    {
        _responses.Add((_, arguments) =>
            arguments.Any(argument => argument.Contains(argumentFragment, StringComparison.OrdinalIgnoreCase))
                ? result
                : null);

        return this;
    }

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));

        foreach (var response in _responses)
        {
            if (response(fileName, arguments) is { } result)
            {
                return Task.FromResult(result);
            }
        }

        return Task.FromResult(Default);
    }
}
