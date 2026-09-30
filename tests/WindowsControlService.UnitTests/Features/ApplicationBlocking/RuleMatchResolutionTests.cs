using WindowsControlService.Features.ApplicationBlocking;
using WindowsControlService.Platform;

namespace WindowsControlService.UnitTests.Features.ApplicationBlocking;

/// <summary>Which field of the version resource a deny rule is built on, most specific first.</summary>
public sealed class RuleMatchResolutionTests
{
    [Theory]
    [InlineData("app.exe", "app_internal", "App Suite", RuleMatchField.FileName, "app.exe")]
    [InlineData(null, "app_internal", "App Suite", RuleMatchField.InternalName, "app_internal")]
    [InlineData(null, null, "App Suite", RuleMatchField.ProductName, "App Suite")]
    public void TheMatchAttributeFollowsWhatTheBinaryActuallyCarries(
        string? original, string? internalName, string? product, RuleMatchField expectedAttribute, string expectedValue)
    {
        var match = ApplicationBlockingService.ResolveMatch(new PeVersionFields(original, internalName, product));

        Assert.NotNull(match);
        Assert.Equal(expectedAttribute, match.Value.Attribute);
        Assert.Equal(expectedValue, match.Value.Value);
    }

    [Fact]
    public void NoVersionFieldsAtAllMeansNoRuleIsPossible() =>
        Assert.Null(ApplicationBlockingService.ResolveMatch(PeVersionFields.None));
}
