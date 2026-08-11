using System.Globalization;
using System.Text;
using System.Xml;

namespace WindowsControlService.Features.ApplicationBlocking;

/// <summary>
/// Builds the WDAC policy XML. Fragile by nature: every detail here was validated against
/// <c>%windir%\schemas\CodeIntegrity\cipolicy.xsd</c> and deployed for real.
/// </summary>
public static class WdacPolicyDocument
{
    /// <summary>
    /// Identity of the policy this service deploys. Stable across deployments so
    /// <c>CiTool --update-policy</c> updates the same policy instead of installing another one
    /// every time, and deliberately different from the A1B2C3D4-... policy an earlier
    /// installation left on this machine, so a leftover can never be mistaken for ours.
    /// </summary>
    public const string PolicyId = "{9E9BB70B-2BD8-4EE9-9031-30476FCF1FF3}";

    /// <summary>A fixed Windows GUID, not ours.</summary>
    private const string PlatformId = "{2E07F7E4-194C-4D20-B7C9-6F44A6C5A234}";

    private const string Namespace = "urn:schemas-microsoft-com:sipolicy";
    private const string PolicyName = "WindowsControlService";

    private const string AllowKernelRuleId = "ID_ALLOW_A_1";
    private const string AllowUserModeRuleId = "ID_ALLOW_A_2";

    private const byte KernelModeScenario = 131;
    private const byte UserModeScenario = 12;

    /// <summary>Builds the document as UTF-8 bytes without a BOM.</summary>
    /// <remarks>
    /// Writing through an <c>XmlWriter</c> over a <c>StringBuilder</c> produces
    /// <c>encoding="utf-16"</c> in the declaration, and <c>ConvertFrom-CIPolicy</c> then fails
    /// to read the file that was saved as UTF-8. A <c>MemoryStream</c> with
    /// <c>UTF8Encoding(false)</c> is what makes the declaration and the bytes agree.
    /// </remarks>
    public static byte[] Build(IReadOnlyList<BlockedApplication> enabledApplications)
    {
        ArgumentNullException.ThrowIfNull(enabledApplications);

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("SiPolicy", Namespace);

            // An attribute of SiPolicy, not a child element. PolicyTypeID is deliberately absent:
            // it is a legacy element that is incompatible with carrying our own PolicyID and
            // BasePolicyID.
            writer.WriteAttributeString("PolicyType", "Base Policy");

            writer.WriteElementString("VersionEx", Namespace, "1.0.0.0");

            // Equal to each other and stable: that is what makes --update-policy an update.
            writer.WriteElementString("PolicyID", Namespace, PolicyId);
            writer.WriteElementString("BasePolicyID", Namespace, PolicyId);
            writer.WriteElementString("PlatformID", Namespace, PlatformId);

            WriteRules(writer);
            writer.WriteElementString("EKUs", Namespace, string.Empty);
            WriteFileRules(writer, enabledApplications);
            writer.WriteElementString("Signers", Namespace, string.Empty);
            WriteSigningScenarios(writer, enabledApplications);
            writer.WriteElementString("UpdatePolicySigners", Namespace, string.Empty);
            writer.WriteElementString("CiSigners", Namespace, string.Empty);
            writer.WriteElementString("HvciOptions", Namespace, "0");
            WriteSettings(writer);

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    public static string DenyRuleId(long applicationId) =>
        string.Create(CultureInfo.InvariantCulture, $"ID_DENY_D_{applicationId}");

    private static void WriteRules(XmlWriter writer)
    {
        writer.WriteStartElement("Rules", Namespace);

        // Unsigned: the policy carries no signature.
        WriteOption(writer, "Enabled:Unsigned System Integrity Policy");

        // Deliberate escape hatch: if a policy ever locks the machine down, the advanced boot
        // menu is the way back to the restore point.
        WriteOption(writer, "Enabled:Advanced Boot Options Menu");

        // Without UMCI nothing is enforced in user mode, which is every block this service makes.
        WriteOption(writer, "Enabled:UMCI");

        WriteOption(writer, "Enabled:Update Policy No Reboot");

        writer.WriteEndElement();
    }

    private static void WriteOption(XmlWriter writer, string option)
    {
        writer.WriteStartElement("Rule", Namespace);
        writer.WriteElementString("Option", Namespace, option);
        writer.WriteEndElement();
    }

    private static void WriteFileRules(XmlWriter writer, IReadOnlyList<BlockedApplication> applications)
    {
        writer.WriteStartElement("FileRules", Namespace);

        // The trap that matters most in this file. A policy with only Deny rules is not a
        // blacklist: WDAC treats it as a whitelist and blocks everything. One Allow FileName="*"
        // per signing scenario is what keeps it a blacklist.
        WriteAllow(writer, AllowKernelRuleId, "Allow all kernel mode code");
        WriteAllow(writer, AllowUserModeRuleId, "Allow all user mode code");

        foreach (var application in applications)
        {
            writer.WriteStartElement("Deny", Namespace);

            // ID_DENY_D_{id}, not ID_DENY_{id}: the XSD pattern requires a letter right after
            // ID_DENY_, so a bare number fails validation.
            writer.WriteAttributeString("ID", DenyRuleId(application.Id));
            writer.WriteAttributeString("FriendlyName", application.Name);

            // No MinimumFileVersion. Omitting it makes the rule cover every version of the file,
            // which is what is wanted, and matches what New-CIPolicyRule -Deny produces.
            // Whichever attribute the row recorded. FileName is the common case but not the only
            // one: a binary with no OriginalFilename is matched by InternalName or ProductName.
            //
            // The name comes from an enum, not from a string in a column: an unexpected value
            // would otherwise become an arbitrary attribute name, or an exception thrown while
            // the policy is being built, which is the worst moment to find out.
            writer.WriteAttributeString(application.MatchAttribute.ToString(), application.MatchValue);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteAllow(XmlWriter writer, string id, string friendlyName)
    {
        writer.WriteStartElement("Allow", Namespace);
        writer.WriteAttributeString("ID", id);
        writer.WriteAttributeString("FriendlyName", friendlyName);
        writer.WriteAttributeString("FileName", "*");
        writer.WriteEndElement();
    }

    private static void WriteSigningScenarios(XmlWriter writer, IReadOnlyList<BlockedApplication> applications)
    {
        writer.WriteStartElement("SigningScenarios", Namespace);

        // 131 is kernel mode. It gets the Allow rule and no deny rules at all: blocking a driver
        // is not something this service should ever be able to do by accident.
        WriteScenario(writer, KernelModeScenario, "ID_SIGNINGSCENARIO_DRIVERS", "Kernel Mode Code Integrity", [AllowKernelRuleId]);

        // 12 is user mode, and carries the other Allow rule plus every deny rule.
        string[] userModeRules = [AllowUserModeRuleId, .. applications.Select(a => DenyRuleId(a.Id))];
        WriteScenario(writer, UserModeScenario, "ID_SIGNINGSCENARIO_WINDOWS", "User Mode Code Integrity", userModeRules);

        writer.WriteEndElement();
    }

    private static void WriteScenario(
        XmlWriter writer,
        byte value,
        string id,
        string friendlyName,
        IReadOnlyList<string> ruleIds)
    {
        writer.WriteStartElement("SigningScenario", Namespace);
        writer.WriteAttributeString("Value", value.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("ID", id);
        writer.WriteAttributeString("FriendlyName", friendlyName);

        writer.WriteStartElement("ProductSigners", Namespace);
        writer.WriteStartElement("FileRulesRef", Namespace);

        foreach (var ruleId in ruleIds)
        {
            writer.WriteStartElement("FileRuleRef", Namespace);
            writer.WriteAttributeString("RuleID", ruleId);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>Gives the policy the name that <c>CiTool --list-policies</c> shows.</summary>
    private static void WriteSettings(XmlWriter writer)
    {
        writer.WriteStartElement("Settings", Namespace);
        writer.WriteStartElement("Setting", Namespace);
        writer.WriteAttributeString("Provider", "PolicyInfo");
        writer.WriteAttributeString("Key", "Information");
        writer.WriteAttributeString("ValueName", "Name");

        writer.WriteStartElement("Value", Namespace);
        writer.WriteElementString("String", Namespace, PolicyName);
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
