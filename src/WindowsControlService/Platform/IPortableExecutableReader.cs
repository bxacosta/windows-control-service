namespace WindowsControlService.Platform;

public interface IPortableExecutableReader
{
    /// <summary>
    /// The <c>OriginalFilename</c> field of the PE version resource, read without MUI
    /// redirection. <see langword="null"/> when the binary carries no version information.
    /// </summary>
    /// <remarks>
    /// This is the field WDAC deny rules match on, which is why it may not come from
    /// <c>FileVersionInfo</c>. Callers that need a value regardless fall back to the file name
    /// themselves; inventing one here would hide a binary with no version resource.
    /// </remarks>
    string? ReadOriginalFileName(string executablePath);

    /// <summary>Metadata for display only. <c>FileVersionInfo</c> is fine for this.</summary>
    (string? FileDescription, string? ProductName) ReadDisplayInfo(string executablePath);
}
