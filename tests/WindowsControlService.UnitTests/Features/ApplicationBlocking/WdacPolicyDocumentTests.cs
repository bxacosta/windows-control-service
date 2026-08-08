using System.Text;
using System.Xml.Linq;
using System.Xml.Schema;
using WindowsControlService.Features.ApplicationBlocking;

namespace WindowsControlService.UnitTests.Features.ApplicationBlocking;

public sealed class WdacPolicyDocumentTests
{
    private const string Namespace = "urn:schemas-microsoft-com:sipolicy";

    private static readonly string SchemaPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "schemas",
        "CodeIntegrity",
        "cipolicy.xsd");

    [Fact]
    public void TheDocumentValidatesAgainstTheWindowsSchema()
    {
        // The cheapest test in the project and the most valuable: it catches nearly every
        // structural mistake without deploying anything.
        Assert.True(File.Exists(SchemaPath), $"{SchemaPath} is missing; this is a Windows file.");

        var schemas = new XmlSchemaSet();
        schemas.Add(Namespace, SchemaPath);

        var document = XDocument.Parse(Encoding.UTF8.GetString(WdacPolicyDocument.Build(SampleApplications())));

        var problems = new List<string>();
        document.Validate(schemas, (_, args) => problems.Add(args.Message));

        Assert.Empty(problems);
    }

    [Fact]
    public void AnEmptyPolicyAlsoValidates()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(Namespace, SchemaPath);

        var document = XDocument.Parse(Encoding.UTF8.GetString(WdacPolicyDocument.Build([])));

        var problems = new List<string>();
        document.Validate(schemas, (_, args) => problems.Add(args.Message));

        Assert.Empty(problems);
    }

    [Fact]
    public void TheBytesAreUtf8WithoutAByteOrderMark()
    {
        var bytes = WdacPolicyDocument.Build(SampleApplications());

        // ConvertFrom-CIPolicy fails on a file whose declaration and bytes disagree, and an
        // XmlWriter over a StringBuilder writes encoding="utf-16" into a file saved as UTF-8.
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("encoding=\"utf-8\"", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithNoApplicationsTheTwoAllowRulesAreStillThere()
    {
        var document = Parse([]);

        // A deny-only policy is not a blacklist: WDAC treats it as a whitelist and blocks
        // everything. Losing the Allow rules is the worst failure this project can produce.
        var allows = document.Descendants(XName.Get("Allow", Namespace)).ToList();

        Assert.Equal(2, allows.Count);
        Assert.All(allows, allow => Assert.Equal("*", allow.Attribute("FileName")?.Value));
        Assert.Empty(document.Descendants(XName.Get("Deny", Namespace)));
    }

    [Fact]
    public void EachApplicationBecomesOneDenyRuleAndOneReferenceInUserMode()
    {
        var applications = SampleApplications();

        var document = Parse(applications);

        Assert.Equal(applications.Count, document.Descendants(XName.Get("Deny", Namespace)).Count());

        var userModeRefs = RuleRefsOfScenario(document, "12");
        foreach (var application in applications)
        {
            Assert.Contains(WdacPolicyDocument.DenyRuleId(application.Id), userModeRefs);
        }
    }

    [Fact]
    public void TheKernelScenarioCarriesNoDenyRule()
    {
        var applications = SampleApplications();

        var kernelRefs = RuleRefsOfScenario(Parse(applications), "131");

        // Blocking a driver is not something this service should be able to do by accident.
        Assert.All(kernelRefs, id => Assert.StartsWith("ID_ALLOW_", id, StringComparison.Ordinal));
        Assert.Single(kernelRefs);
    }

    [Fact]
    public void DenyRuleIdsMatchThePatternTheSchemaDemands()
    {
        var document = Parse(SampleApplications());

        foreach (var deny in document.Descendants(XName.Get("Deny", Namespace)))
        {
            // ID_DENY_D_1, not ID_DENY_1: the schema wants a letter straight after ID_DENY_.
            Assert.Matches("^ID_DENY_[A-Z][_A-Z0-9]*$", deny.Attribute("ID")!.Value);
        }
    }

    [Fact]
    public void ThePolicyIdIsStableAndEqualToTheBasePolicyId()
    {
        var first = Parse(SampleApplications());
        var second = Parse([]);

        var policyId = first.Root!.Element(XName.Get("PolicyID", Namespace))!.Value;
        var basePolicyId = first.Root.Element(XName.Get("BasePolicyID", Namespace))!.Value;

        // Equal and stable is what makes --update-policy update the same policy instead of
        // installing a new one on every deployment.
        Assert.Equal(policyId, basePolicyId);
        Assert.Equal(policyId, second.Root!.Element(XName.Get("PolicyID", Namespace))!.Value);
        Assert.Equal(WdacPolicyDocument.PolicyId, policyId);
    }

    [Fact]
    public void TheRequiredRuleOptionsAreAllPresent()
    {
        var options = Parse([])
            .Descendants(XName.Get("Option", Namespace))
            .Select(option => option.Value)
            .ToList();

        Assert.Contains("Enabled:UMCI", options);
        Assert.Contains("Enabled:Unsigned System Integrity Policy", options);
        Assert.Contains("Enabled:Advanced Boot Options Menu", options);
        Assert.Contains("Enabled:Update Policy No Reboot", options);
    }

    [Fact]
    public void PolicyTypeIsAnAttributeAndPolicyTypeIdIsAbsent()
    {
        var root = Parse([]).Root!;

        Assert.Equal("Base Policy", root.Attribute("PolicyType")?.Value);
        Assert.Null(root.Element(XName.Get("PolicyTypeID", Namespace)));
    }

    [Fact]
    public void ANameContainingMarkupIsEscaped()
    {
        // FriendlyName comes from whatever the user typed.
        var application = new BlockedApplication
        {
            Id = 7,
            Name = "Ampersand & <script>\"quoted\"</script>",
            MatchValue = "evil.exe",
        };

        var bytes = WdacPolicyDocument.Build([application]);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("<script>", text, StringComparison.Ordinal);

        // Still parses, and the value survives intact.
        var deny = Parse([application]).Descendants(XName.Get("Deny", Namespace)).Single();
        Assert.Equal(application.Name, deny.Attribute("FriendlyName")!.Value);
    }

    [Fact]
    public void TheSchemaValidatesADocumentWithAnAwkwardName()
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(Namespace, SchemaPath);

        var document = Parse([new BlockedApplication { Id = 1, Name = "A & B", MatchValue = "a.exe" }]);

        var problems = new List<string>();
        document.Validate(schemas, (_, args) => problems.Add(args.Message));

        Assert.Empty(problems);
    }

    private static XDocument Parse(IReadOnlyList<BlockedApplication> applications) =>
        XDocument.Parse(Encoding.UTF8.GetString(WdacPolicyDocument.Build(applications)));

    private static IReadOnlyList<string> RuleRefsOfScenario(XDocument document, string value) =>
    [
        .. document.Descendants(XName.Get("SigningScenario", Namespace))
            .Single(scenario => scenario.Attribute("Value")?.Value == value)
            .Descendants(XName.Get("FileRuleRef", Namespace))
            .Select(reference => reference.Attribute("RuleID")!.Value)
    ];

    private static List<BlockedApplication> SampleApplications() =>
    [
        new() { Id = 1, Name = "Test target", MatchValue = "wcs-test-target.exe" },
        new() { Id = 2, Name = "Another", MatchValue = "another.exe" },
        new() { Id = 42, Name = "Third", MatchValue = "third.exe" },
    ];
}
