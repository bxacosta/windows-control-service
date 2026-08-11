namespace WindowsControlService.IntegrationTests.WebInterface;

/// <summary>
/// The interface tests read the files that are shipped rather than the ones a test host happens
/// to serve: a rule that only covers what exists in the output folder is not a rule.
/// </summary>
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string WebRoot { get; } = Path.Combine(Root, "src", "WindowsControlService", "wwwroot");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WindowsControlService.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root is not above the test output folder.");
    }
}
