using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WindowsControlService.Features.Authentication;
using WindowsControlService.Infrastructure.Results;
using WindowsControlService.UnitTests.Fakes;

namespace WindowsControlService.UnitTests.Features.Authentication;

public sealed class PasswordServiceTests
{
    private const string Password = "una-contrasena-larga-2026";

    private readonly FakeSettingsRepository _settings = new();

    // Far below the production count on purpose: these tests exercise the logic, and 210,000
    // iterations per call would make the suite crawl.
    private readonly AuthenticationOptions _options = new() { Pbkdf2Iterations = 100_000 };

    private PasswordService Service => new(
        _settings,
        Options.Create(_options),
        NullLogger<PasswordService>.Instance);

    [Fact]
    public async Task AFreshInstallHasNoPassword()
    {
        Assert.False(await Service.IsConfiguredAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConfiguringStoresHashSaltIterationsAndStamp()
    {
        var result = await Service.ConfigureAsync(Password, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(_settings.Values.ContainsKey(PasswordService.HashKey));
        Assert.True(_settings.Values.ContainsKey(PasswordService.SaltKey));
        Assert.True(_settings.Values.ContainsKey(PasswordService.IterationsKey));
        Assert.True(_settings.Values.ContainsKey(PasswordService.SecurityStampKey));

        // One write, not four. Hash and salt landing separately can leave a new salt beside an
        // old hash, which locks the service out permanently.
        Assert.Equal(1, _settings.WriteCount);
    }

    [Fact]
    public async Task TheStoredHashIsNotThePassword()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);

        Assert.DoesNotContain(Password, _settings.Values[PasswordService.HashKey], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoInstallsOfTheSamePasswordProduceDifferentHashes()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);
        var first = _settings.Values[PasswordService.HashKey];

        var other = new FakeSettingsRepository();
        await new PasswordService(other, Options.Create(_options), NullLogger<PasswordService>.Instance)
            .ConfigureAsync(Password, CancellationToken.None);

        // Per-install salt. Equal hashes would mean the salt is not doing its job.
        Assert.NotEqual(first, other.Values[PasswordService.HashKey]);
    }

    [Fact]
    public async Task ConfiguringTwiceIsAConflict()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);

        var second = await Service.ConfigureAsync("otra-contrasena-2026-larga", CancellationToken.None);

        Assert.Equal(ErrorCode.Conflict, second.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("123456789")]
    public async Task APasswordShorterThanThePolicyIsRejected(string candidate)
    {
        var result = await Service.ConfigureAsync(candidate, CancellationToken.None);

        Assert.Equal(ErrorCode.Invalid, result.Error.Code);
        Assert.False(await Service.IsConfiguredAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheRightPasswordValidatesAndReturnsTheStamp()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);

        var result = await Service.ValidateAsync(Password, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_settings.Values[PasswordService.SecurityStampKey], result.Value);
    }

    [Theory]
    [InlineData("no-es-la-buena")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AWrongOrMissingPasswordIsAFailedLoginNotAnException(string? candidate)
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);

        var result = await Service.ValidateAsync(candidate!, CancellationToken.None);

        // Never an exception: letting null reach PBKDF2 surfaces as a 500 with no body.
        Assert.Equal(ErrorCode.Unauthorized, result.Error.Code);
    }

    [Fact]
    public async Task ValidatingBeforeAnyPasswordExistsFails()
    {
        var result = await Service.ValidateAsync(Password, CancellationToken.None);

        Assert.Equal(ErrorCode.Unauthorized, result.Error.Code);
    }

    [Fact]
    public async Task ChangingThePasswordRotatesTheSecurityStamp()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);
        var before = _settings.Values[PasswordService.SecurityStampKey];

        var result = await Service.ChangeAsync(Password, "otra-contrasena-2026-larga", CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The rotation is what signs out every open session, including the one that changed it.
        Assert.NotEqual(before, _settings.Values[PasswordService.SecurityStampKey]);
        Assert.True((await Service.ValidateAsync("otra-contrasena-2026-larga", CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task ChangingWithTheWrongCurrentPasswordChangesNothing()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);
        var before = _settings.Values[PasswordService.HashKey];

        var result = await Service.ChangeAsync("no-es-la-buena", "otra-contrasena-2026-larga", CancellationToken.None);

        Assert.Equal(ErrorCode.Unauthorized, result.Error.Code);
        Assert.Equal(before, _settings.Values[PasswordService.HashKey]);
    }

    [Fact]
    public async Task AShortNewPasswordIsRejectedAndTheOldOneStillWorks()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);

        var result = await Service.ChangeAsync(Password, "corta", CancellationToken.None);

        Assert.Equal(ErrorCode.Invalid, result.Error.Code);
        Assert.True((await Service.ValidateAsync(Password, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task RaisingTheIterationCountDoesNotLockOutAnExistingPassword()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);

        // The stored count is read from storage, not from options, so tightening the parameter
        // later must not invalidate what is already on disk.
        _options.Pbkdf2Iterations = 400_000;

        Assert.True((await Service.ValidateAsync(Password, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task CorruptedStoredMaterialFailsTheLoginInsteadOfThrowing()
    {
        await Service.ConfigureAsync(Password, CancellationToken.None);
        _settings.Values[PasswordService.SaltKey] = "not base64 at all";

        var result = await Service.ValidateAsync(Password, CancellationToken.None);

        Assert.Equal(ErrorCode.Unauthorized, result.Error.Code);
    }
}
