namespace WindowsControlService.Platform;

/// <summary>
/// The three fields of the PE version resource that a WDAC deny rule can match on, read without
/// MUI redirection. Any of them may be <see langword="null"/>: plenty of shipped binaries carry
/// no version resource at all, and some carry a partial one.
/// </summary>
public sealed record PeVersionFields(string? OriginalFileName, string? InternalName, string? ProductName)
{
    public static readonly PeVersionFields None = new(null, null, null);

    public bool IsEmpty => OriginalFileName is null && InternalName is null && ProductName is null;
}

public interface IPortableExecutableReader
{
    /// <summary>
    /// Reads the fields a deny rule can be built from, without MUI redirection.
    /// </summary>
    /// <remarks>
    /// These may not come from <c>FileVersionInfo</c>, which follows MUI redirection and answers
    /// <c>NOTEPAD.EXE.MUI</c>. Nothing is invented when a field is missing: a rule built from a
    /// guessed value is a rule WDAC never matches, and the block would fail silently.
    /// </remarks>
    PeVersionFields ReadVersionFields(string executablePath);

    /// <summary>Metadata for display only. <c>FileVersionInfo</c> is fine for this.</summary>
    (string? FileDescription, string? ProductName) ReadDisplayInfo(string executablePath);
}
