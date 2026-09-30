namespace WindowsControlService.IntegrationTests.Platform;

/// <summary>
/// The tests that read or write USBSTOR Start, run one at a time: in parallel, a read could land
/// between another test's write and its restore and assert against a value the machine never
/// meant to have.
/// </summary>
[CollectionDefinition(Name)]
public sealed class UsbRegistryTests
{
    public const string Name = "USB registry";
}
