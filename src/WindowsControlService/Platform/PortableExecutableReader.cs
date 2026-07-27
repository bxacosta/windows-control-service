using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WindowsControlService.Platform;

/// <inheritdoc cref="IPortableExecutableReader"/>
/// <remarks>
/// <para>
/// The whole point of this class is that <c>FileVersionInfo.GetVersionInfo</c> calls
/// <c>GetFileVersionInfoW</c>, which <b>follows MUI redirection</b> and answers
/// <c>NOTEPAD.EXE.MUI</c> instead of <c>Notepad.exe</c> for system binaries. WDAC reads the
/// field without MUI, so a rule generated from FileVersionInfo simply never matches and the
/// block silently does nothing. The Ex variants with FILE_VER_GET_NEUTRAL avoid it.
/// </para>
/// <para>
/// Deliberately <c>DllImport</c> and not <c>LibraryImport</c>: the modern form requires
/// <c>AllowUnsafeBlocks</c> across the whole project, and its payoff is trimming and AOT
/// compatibility, neither of which this service uses. Relaxing a project-wide safety switch
/// to modernise three declarations is a bad trade.
/// </para>
/// </remarks>
public sealed class PortableExecutableReader(ILogger<PortableExecutableReader> logger) : IPortableExecutableReader
{
    private const uint FileVerGetNeutral = 0x02;

    public string? ReadOriginalFileName(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            return ReadNeutralOriginalFileName(executablePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not read version information from {Path}.", executablePath);
            return null;
        }
    }

    public (string? FileDescription, string? ProductName) ReadDisplayInfo(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            // MUI redirection is harmless here: this text is only ever shown to a person.
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return (NullIfBlank(info.FileDescription), NullIfBlank(info.ProductName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not read display information from {Path}.", executablePath);
            return (null, null);
        }
    }

    private static string? ReadNeutralOriginalFileName(string executablePath)
    {
        var size = GetFileVersionInfoSizeEx(FileVerGetNeutral, executablePath, out _);
        if (size == 0)
        {
            // No version resource at all, or the file does not exist. Both mean "no name here".
            return null;
        }

        var block = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!GetFileVersionInfoEx(FileVerGetNeutral, executablePath, 0, size, block))
            {
                return null;
            }

            if (!VerQueryValue(block, @"\VarFileInfo\Translation", out var translations, out var translationBytes))
            {
                return null;
            }

            // Four bytes per entry: two of language, two of code page. The first translation
            // that answers wins; binaries do not disagree with themselves about their own name.
            for (var offset = 0; offset + 4 <= translationBytes; offset += 4)
            {
                var language = (ushort)Marshal.ReadInt16(translations, offset);
                var codePage = (ushort)Marshal.ReadInt16(translations, offset + 2);

                var subBlock = string.Create(
                    CultureInfo.InvariantCulture,
                    $@"\StringFileInfo\{language:X4}{codePage:X4}\OriginalFilename");

                if (!VerQueryValue(block, subBlock, out var value, out var valueLength) || valueLength == 0)
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(value, (int)valueLength)?.TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [DllImport("version.dll", EntryPoint = "GetFileVersionInfoSizeExW",
        CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFileVersionInfoSizeEx(uint flags, string filePath, out uint handle);

    [DllImport("version.dll", EntryPoint = "GetFileVersionInfoExW",
        CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileVersionInfoEx(
        uint flags, string filePath, uint handle, uint bufferLength, IntPtr buffer);

    [DllImport("version.dll", EntryPoint = "VerQueryValueW",
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VerQueryValue(
        IntPtr block, string subBlock, out IntPtr valuePointer, out uint valueLength);
}
