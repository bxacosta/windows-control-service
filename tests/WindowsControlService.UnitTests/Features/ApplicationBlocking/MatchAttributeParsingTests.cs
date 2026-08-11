using WindowsControlService.Features.ApplicationBlocking;

namespace WindowsControlService.UnitTests.Features.ApplicationBlocking;

/// <summary>
/// The reading end of the barrier, exercised without a database on purpose: that is exactly the
/// situation it exists for. The CHECK constraint arrived in migration 0005, so a database
/// restored from before it, or copied from another machine, reaches this code with the storage
/// side missing.
/// </summary>
public sealed class MatchAttributeParsingTests
{
    [Theory]
    [InlineData("FileName", RuleMatchField.FileName)]
    [InlineData("InternalName", RuleMatchField.InternalName)]
    [InlineData("ProductName", RuleMatchField.ProductName)]
    public void EveryNameThisServiceWritesComesBack(string stored, RuleMatchField expected) =>
        Assert.Equal(expected, BlockedApplicationRepository.ParseMatchAttribute(1, stored));

    [Theory]
    // A real WDAC attribute this service does not write. The obvious case.
    [InlineData("FilePath")]
    // Enum.TryParse accepts the numeric form and answers true with a value that is no member:
    // "7" would have reached the policy as an attribute name and thrown an XmlException while it
    // was being built, which is the moment this guard exists to avoid.
    [InlineData("7")]
    [InlineData("-1")]
    // And Enum.IsDefined alone would not catch this one: "0" parses to a real member and would
    // silently become FileName, quietly turning a row nobody can vouch for into a working rule.
    [InlineData("0")]
    [InlineData("2")]
    // Case matters: the value is written into XML verbatim.
    [InlineData("filename")]
    [InlineData("")]
    [InlineData(" FileName ")]
    public void AnythingElseFailsLoudlyAndSaysWhichRow(string stored)
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => BlockedApplicationRepository.ParseMatchAttribute(42, stored));

        Assert.Contains("42", refused.Message, StringComparison.Ordinal);
        Assert.Contains(stored, refused.Message, StringComparison.Ordinal);
    }
}
