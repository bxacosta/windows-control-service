using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.IntegrationTests.Fakes;
using WindowsControlService.IntegrationTests.Infrastructure.Database;
using WindowsControlService.Platform;

namespace WindowsControlService.IntegrationTests.Features.ApplicationBlocking;

/// <summary>
/// The heart of phase 4. A programmable <see cref="FakeCodeIntegrityTool"/> and a real
/// repository over a throwaway SQLite file: nothing here can reach WDAC.
/// </summary>
public sealed class ApplicationBlockingServiceTests : IDisposable
{
    private readonly TemporaryDataDirectory _directory = new();
    private readonly FakeCodeIntegrityTool _codeIntegrity = new();
    private readonly FakePortableExecutableReader _executableReader = new();
    private readonly SequentialExecutor _executor = new();
    private readonly IHost _host;
    private readonly IBlockedApplicationRepository _repository;
    private readonly ApplicationBlockingService _service;

    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "wcs-blocking-tests", Guid.NewGuid().ToString("N"));

    public ApplicationBlockingServiceTests()
    {
        Directory.CreateDirectory(_workDirectory);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [$"--{DataDirectoryExtensions.ConfigurationKey}={_directory.Path}"],
        });

        builder.AddDataDirectory();
        builder.Services.AddDatabase(builder.Configuration);
        builder.Services.AddSingleton<IBlockedApplicationRepository, BlockedApplicationRepository>();

        _host = builder.Build();
        _host.Services.MigrateDatabase();
        _repository = _host.Services.GetRequiredService<IBlockedApplicationRepository>();

        _service = new ApplicationBlockingService(
            _repository,
            _codeIntegrity,
            _executableReader,
            _executor,
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<ApplicationBlockingService>.Instance);
    }

    public void Dispose()
    {
        _executor.Dispose();
        _host.Dispose();
        _directory.Dispose();

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    [Fact]
    public async Task AddingStoresTheRowAndAppliesAPolicyWithOneRule()
    {
        var path = CreateExecutable("target.exe");

        var result = await _service.AddAsync(path, "Target", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await _repository.GetAllAsync(CancellationToken.None));
        Assert.Single(_codeIntegrity.AppliedDocuments);
        Assert.Equal(PolicyState.Enforced, _codeIntegrity.State);
    }

    [Fact]
    public async Task AddingUsesTheOriginalFileNameFromThePeHeader()
    {
        var path = CreateExecutable("renamed.exe");
        _executableReader.OriginalFileNames[path] = "the-real-name.exe";

        var result = await _service.AddAsync(path, "Target", CancellationToken.None);

        var stored = await _repository.GetByIdAsync(result.Value, CancellationToken.None);
        Assert.Equal("the-real-name.exe", stored!.OriginalFileName);
    }

    [Fact]
    public async Task AddingFallsBackToTheFileNameWhenThereIsNoVersionResource()
    {
        var path = CreateExecutable("plain.exe");

        var result = await _service.AddAsync(path, "Target", CancellationToken.None);

        Assert.Equal("plain.exe", (await _repository.GetByIdAsync(result.Value, CancellationToken.None))!.OriginalFileName);
    }

    [Fact]
    public async Task AddingRollsBackTheRowWhenThePolicyCannotBeApplied()
    {
        var path = CreateExecutable("target.exe");
        _codeIntegrity.ApplyFailure = new Error(ErrorCode.OperationFailed, "no");

        var result = await _service.AddAsync(path, "Target", CancellationToken.None);

        // The compensation. Without it the database claims a block the machine is not enforcing.
        Assert.Equal(ErrorCode.OperationFailed, result.Error.Code);
        Assert.Empty(await _repository.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddingAPathThatDoesNotExistNeverReachesCiTool()
    {
        var result = await _service.AddAsync(Path.Combine(_workDirectory, "missing.exe"), "Nope", CancellationToken.None);

        Assert.Equal(ErrorCode.Invalid, result.Error.Code);
        Assert.Empty(_codeIntegrity.AppliedDocuments);
    }

    [Fact]
    public async Task AddingTheSamePathInAnotherCaseIsAConflict()
    {
        var path = CreateExecutable("target.exe");
        await _service.AddAsync(path, "Target", CancellationToken.None);

        var again = await _service.AddAsync(path.ToUpperInvariant(), "Target again", CancellationToken.None);

        Assert.Equal(ErrorCode.Conflict, again.Error.Code);
        Assert.Single(await _repository.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddingAnUnnormalisedPathThatResolvesToTheSameFileIsAConflict()
    {
        var path = CreateExecutable("target.exe");
        await _service.AddAsync(path, "Target", CancellationToken.None);

        var awkward = Path.Combine(_workDirectory, "sub", "..", "target.exe");

        Assert.Equal(ErrorCode.Conflict, (await _service.AddAsync(awkward, "Again", CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task RemovingAppliesThePolicyBeforeDeletingTheRow()
    {
        var first = await AddAsync("one.exe", "One");
        await AddAsync("two.exe", "Two");
        _codeIntegrity.AppliedDocuments.Clear();

        var result = await _service.RemoveAsync(first, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_codeIntegrity.AppliedDocuments);
        Assert.Null(await _repository.GetByIdAsync(first, CancellationToken.None));
    }

    [Fact]
    public async Task RemovingKeepsTheRowWhenThePolicyCannotBeApplied()
    {
        var first = await AddAsync("one.exe", "One");
        await AddAsync("two.exe", "Two");
        _codeIntegrity.ApplyFailure = new Error(ErrorCode.OperationFailed, "no");

        var result = await _service.RemoveAsync(first, CancellationToken.None);

        // Deleting first would leave the application blocked with nothing to explain why.
        Assert.Equal(ErrorCode.OperationFailed, result.Error.Code);
        Assert.NotNull(await _repository.GetByIdAsync(first, CancellationToken.None));
    }

    [Fact]
    public async Task RemovingTheLastEnabledApplicationRemovesThePolicy()
    {
        var only = await AddAsync("one.exe", "One");
        _codeIntegrity.AppliedDocuments.Clear();

        var result = await _service.RemoveAsync(only, CancellationToken.None);

        // An empty list means remove, never apply an empty policy: a service with no active
        // blocks must not leave a policy installed.
        Assert.True(result.IsSuccess);
        Assert.Equal(1, _codeIntegrity.RemoveCount);
        Assert.Empty(_codeIntegrity.AppliedDocuments);
    }

    [Fact]
    public async Task RemovingADisabledApplicationDoesNotTouchCiTool()
    {
        var first = await AddAsync("one.exe", "One");
        await AddAsync("two.exe", "Two");
        await _service.SetEnabledAsync(first, enabled: false, CancellationToken.None);
        _codeIntegrity.AppliedDocuments.Clear();
        var removalsBefore = _codeIntegrity.RemoveCount;

        var result = await _service.RemoveAsync(first, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_codeIntegrity.AppliedDocuments);
        Assert.Equal(removalsBefore, _codeIntegrity.RemoveCount);
        Assert.Null(await _repository.GetByIdAsync(first, CancellationToken.None));
    }

    [Fact]
    public async Task RemovingSomethingThatIsNotThereIsNotFound()
    {
        Assert.Equal(ErrorCode.NotFound, (await _service.RemoveAsync(404, CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task DisablingTheOnlyEnabledApplicationRemovesThePolicy()
    {
        var only = await AddAsync("one.exe", "One");
        _codeIntegrity.AppliedDocuments.Clear();

        var result = await _service.SetEnabledAsync(only, enabled: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _codeIntegrity.RemoveCount);
        Assert.Empty(_codeIntegrity.AppliedDocuments);
    }

    [Fact]
    public async Task DisablingKeepsTheFlagWhenThePolicyCannotBeApplied()
    {
        // Two applications, deliberately. With only one, disabling takes the Remove path and a
        // test that programs Apply to fail proves nothing and passes green.
        var first = await AddAsync("one.exe", "One");
        await AddAsync("two.exe", "Two");
        _codeIntegrity.ApplyFailure = new Error(ErrorCode.OperationFailed, "no");

        var result = await _service.SetEnabledAsync(first, enabled: false, CancellationToken.None);

        Assert.Equal(ErrorCode.OperationFailed, result.Error.Code);
        Assert.True((await _repository.GetByIdAsync(first, CancellationToken.None))!.IsEnabled);
    }

    [Fact]
    public async Task EnablingAgainPutsTheRuleBackInThePolicy()
    {
        var first = await AddAsync("one.exe", "One");
        await AddAsync("two.exe", "Two");
        await _service.SetEnabledAsync(first, enabled: false, CancellationToken.None);
        _codeIntegrity.AppliedDocuments.Clear();

        var result = await _service.SetEnabledAsync(first, enabled: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var document = System.Text.Encoding.UTF8.GetString(Assert.Single(_codeIntegrity.AppliedDocuments));
        Assert.Contains(WdacPolicyDocument.DenyRuleId(first), document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconcilingWithAnUnknownStateDoesNothingAtAll()
    {
        await AddAsync("one.exe", "One");
        _codeIntegrity.AppliedDocuments.Clear();
        var removalsBefore = _codeIntegrity.RemoveCount;
        _codeIntegrity.State = PolicyState.Unknown;

        var result = await _service.ReconcileAsync(CancellationToken.None);

        // Acting on Unknown would reinstall the policy every minute, forever.
        Assert.True(result.IsSuccess);
        Assert.Empty(_codeIntegrity.AppliedDocuments);
        Assert.Equal(removalsBefore, _codeIntegrity.RemoveCount);
    }

    [Fact]
    public async Task ReconcilingRemovesAPolicyThatNoLongerHasAnyEnabledApplication()
    {
        _codeIntegrity.State = PolicyState.Enforced;
        _codeIntegrity.AppliedDocuments.Clear();
        var removalsBefore = _codeIntegrity.RemoveCount;

        var result = await _service.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(removalsBefore + 1, _codeIntegrity.RemoveCount);
    }

    [Fact]
    public async Task ReconcilingReappliesAPolicySomeoneRemoved()
    {
        await AddAsync("one.exe", "One");
        _codeIntegrity.AppliedDocuments.Clear();

        // An administrator can run CiTool --remove-policy. That cannot be prevented, only noticed.
        _codeIntegrity.State = PolicyState.NotEnforced;

        var result = await _service.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_codeIntegrity.AppliedDocuments);
    }

    [Fact]
    public async Task ReconcilingAnAlreadyCorrectMachineChangesNothing()
    {
        await AddAsync("one.exe", "One");
        _codeIntegrity.AppliedDocuments.Clear();
        var removalsBefore = _codeIntegrity.RemoveCount;
        _codeIntegrity.State = PolicyState.Enforced;

        var result = await _service.ReconcileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_codeIntegrity.AppliedDocuments);
        Assert.Equal(removalsBefore, _codeIntegrity.RemoveCount);
    }

    [Fact]
    public async Task ThePolicyStateReportReflectsWhatIsEnabled()
    {
        await AddAsync("one.exe", "One");
        await AddAsync("two.exe", "Two");
        _codeIntegrity.State = PolicyState.Enforced;

        var before = await _service.GetPolicyStateAsync(CancellationToken.None);
        Assert.Equal("Enforced", before.Value.State);
        Assert.Equal(2, before.Value.EnabledRuleCount);
        Assert.Null(before.Value.LastReconciledAt);

        await _service.ReconcileAsync(CancellationToken.None);

        var after = await _service.GetPolicyStateAsync(CancellationToken.None);
        Assert.NotNull(after.Value.LastReconciledAt);
    }

    private async Task<long> AddAsync(string fileName, string name)
    {
        var result = await _service.AddAsync(CreateExecutable(fileName), name, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        _codeIntegrity.ApplyFailure = null;
        return result.Value;
    }

    private string CreateExecutable(string fileName)
    {
        var path = Path.Combine(_workDirectory, fileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "not a real executable, but it exists on disk");
        }

        return path;
    }
}
