using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Infrastructure.Database;
using WindowsControlService.Infrastructure.Hosting;
using WindowsControlService.IntegrationTests.Infrastructure.Database;

namespace WindowsControlService.IntegrationTests.Features.ApplicationBlocking;

/// <summary>
/// The match attribute travels from a column into the name of an XML attribute inside a policy
/// that Windows enforces. These pin both ends of that trip: what the database will accept, and
/// what the schema on this machine says is legal on a deny rule.
/// </summary>
public sealed class RuleMatchFieldTests : IDisposable
{
    private readonly TemporaryDataDirectory _directory = new();
    private readonly IHost _host;
    private readonly IBlockedApplicationRepository _repository;

    public RuleMatchFieldTests()
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

    [Theory]
    [InlineData(RuleMatchField.FileName)]
    [InlineData(RuleMatchField.InternalName)]
    [InlineData(RuleMatchField.ProductName)]
    public async Task EveryAttributeSurvivesTheRoundTrip(RuleMatchField field)
    {
        var id = await _repository.InsertAsync(
            new BlockedApplication
            {
                Name = "Target",
                ExecutablePath = $@"C:\Apps\{field}.exe",
                MatchAttribute = field,
                MatchValue = "value",
                CreatedAt = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc),
            },
            CancellationToken.None);

        var stored = await _repository.GetByIdAsync(id, CancellationToken.None);

        Assert.Equal(field, stored!.MatchAttribute);
    }

    [Fact]
    public async Task TheDatabaseRefusesAnAttributeThisServiceCannotWrite()
    {
        var connectionString = _host.Services.GetRequiredService<IDbConnectionFactory>().ConnectionString;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO BlockedApplications (Name, ExecutablePath, MatchAttribute, MatchValue, IsEnabled, CreatedAt)
            VALUES ('Target', 'C:\Apps\x.exe', 'FilePath', 'C:\Apps\x.exe', 1, '2026-08-19T00:00:00.0000000Z');
            """;

        // FilePath is a deliberate choice of bad value: it is a real WDAC attribute, so it is
        // exactly what a future migration might reach for, and it is refused here rather than
        // becoming a rule that is walked around by moving the file somewhere else.
        var rejected = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Contains("CHECK constraint failed", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryAttributeIsOneTheWindowsSchemaAllowsOnADenyRule()
    {
        var schemaPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "schemas", "CodeIntegrity", "cipolicy.xsd");

        Assert.True(File.Exists(schemaPath), $"The Code Integrity schema is not at {schemaPath}.");

        XNamespace xs = "http://www.w3.org/2001/XMLSchema";

        // The attributes hang off an anonymous complexType inside the Deny element, not off the
        // named DenyType, which is only the type of its ID.
        var denyAttributes = XDocument.Load(schemaPath)
            .Descendants(xs + "element")
            .Where(element => (string?)element.Attribute("name") == "Deny")
            .Descendants(xs + "attribute")
            .Select(attribute => (string?)attribute.Attribute("name"))
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(denyAttributes);

        // This pins the platform rather than the code. If a Windows update ever dropped one of
        // these, a policy built with it would stop converting, and that failure would surface as
        // a deployment error with nothing to explain it.
        foreach (var field in Enum.GetNames<RuleMatchField>())
        {
            Assert.Contains(field, denyAttributes);
        }
    }
}
