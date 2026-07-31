using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.IntegrationTests.Infrastructure.Database;

namespace WindowsControlService.IntegrationTests.Features.ApplicationBlocking;

public sealed class BlockedApplicationRepositoryTests : IDisposable
{
    private static readonly DateTime Created = new(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

    private readonly TemporaryDataDirectory _directory = new();
    private readonly IHost _host;
    private readonly IBlockedApplicationRepository _repository;

    public BlockedApplicationRepositoryTests()
    {
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
    }

    public void Dispose()
    {
        _host.Dispose();
        _directory.Dispose();
    }

    [Fact]
    public async Task AnInsertedRowComesBackWithEverythingItWasGiven()
    {
        var id = await InsertAsync(@"C:\Apps\editor.exe", "Editor", "editor.exe", "Editor Suite");

        var stored = await _repository.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("Editor", stored.Name);
        Assert.Equal(@"C:\Apps\editor.exe", stored.ExecutablePath);
        Assert.Equal("editor.exe", stored.OriginalFileName);
        Assert.Equal("Editor Suite", stored.ProductName);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task CreatedAtComesBackAsUtc()
    {
        var id = await InsertAsync(@"C:\Apps\editor.exe", "Editor", "editor.exe");

        var stored = await _repository.GetByIdAsync(id, CancellationToken.None);

        // Parsing without RoundtripKind converts to local time and returns Kind=Local, which
        // silently breaks every later comparison against UTC.
        Assert.Equal(DateTimeKind.Utc, stored!.CreatedAt.Kind);
        Assert.Equal(Created, stored.CreatedAt);
    }

    [Fact]
    public async Task AMissingIdIsNull()
    {
        Assert.Null(await _repository.GetByIdAsync(404, CancellationToken.None));
    }

    [Fact]
    public async Task ExistsByPathIgnoresCase()
    {
        await InsertAsync(@"C:\Apps\Editor.exe", "Editor", "editor.exe");

        Assert.True(await _repository.ExistsByPathAsync(@"c:\apps\editor.EXE", CancellationToken.None));
        Assert.False(await _repository.ExistsByPathAsync(@"C:\Apps\other.exe", CancellationToken.None));
    }

    [Fact]
    public async Task TheUniqueIndexRejectsTheSamePathInAnotherCase()
    {
        await InsertAsync(@"C:\Apps\Editor.exe", "Editor", "editor.exe");

        // The database is the last line of defence: the service checks first, but two concurrent
        // adds would otherwise both pass that check.
        await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(@"c:\apps\editor.exe", "Again", "editor.exe"));
    }

    [Fact]
    public async Task GetEnabledLeavesOutTheDisabledOnes()
    {
        var enabled = await InsertAsync(@"C:\Apps\a.exe", "A", "a.exe");
        var disabled = await InsertAsync(@"C:\Apps\b.exe", "B", "b.exe");
        await _repository.SetEnabledAsync(disabled, enabled: false, CancellationToken.None);

        var rows = await _repository.GetEnabledAsync(CancellationToken.None);

        Assert.Equal([enabled], rows.Select(row => row.Id));
        Assert.Equal(2, (await _repository.GetAllAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task SetEnabledReportsWhetherItMatchedARow()
    {
        var id = await InsertAsync(@"C:\Apps\a.exe", "A", "a.exe");

        Assert.True(await _repository.SetEnabledAsync(id, enabled: false, CancellationToken.None));
        Assert.False((await _repository.GetByIdAsync(id, CancellationToken.None))!.IsEnabled);
        Assert.False(await _repository.SetEnabledAsync(404, enabled: false, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteReportsWhetherItMatchedARow()
    {
        var id = await InsertAsync(@"C:\Apps\a.exe", "A", "a.exe");

        Assert.True(await _repository.DeleteAsync(id, CancellationToken.None));
        Assert.Null(await _repository.GetByIdAsync(id, CancellationToken.None));
        Assert.False(await _repository.DeleteAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task IdsAreNotReused()
    {
        var first = await InsertAsync(@"C:\Apps\a.exe", "A", "a.exe");
        await _repository.DeleteAsync(first, CancellationToken.None);

        var second = await InsertAsync(@"C:\Apps\b.exe", "B", "b.exe");

        // AUTOINCREMENT, on purpose: the deny rule id is derived from this, and recycling one
        // could let a stale cached rule match a new entry.
        Assert.NotEqual(first, second);
    }

    private Task<long> InsertAsync(string path, string name, string originalFileName, string? productName = null) =>
        _repository.InsertAsync(
            new BlockedApplication
            {
                Name = name,
                ExecutablePath = path,
                OriginalFileName = originalFileName,
                ProductName = productName,
                IsEnabled = true,
                CreatedAt = Created,
            },
            CancellationToken.None);
}
