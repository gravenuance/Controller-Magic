using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ControllerMagic;

// The path a file really lives at, with every symlink and junction along the way resolved, so two
// routes to one exe compare as the same file.
internal static class FinalPath
{
    private const uint FILE_NAME_NORMALIZED = 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    // Null when the file can't be opened (missing, a removed link) or its path can't be read.
    public static string? TryResolve(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new char[512];
            uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, FILE_NAME_NORMALIZED);
            if (length >= buffer.Length)
            {
                buffer = new char[length];
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, FILE_NAME_NORMALIZED);
            }

            if (length == 0 || length >= buffer.Length)
            {
                AppLog.Default.Warning($"FinalPath: couldn't read the final path of {path} ({new Win32Exception(Marshal.GetLastPInvokeError()).Message})");
                return null;
            }

            return StripDevicePrefix(new string(buffer, 0, (int)length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // \\?\D:\x -> D:\x and \\?\UNC\server\share -> \\server\share, the forms the rest of the app uses.
    internal static string StripDevicePrefix(string path)
    {
        const string UncPrefix = @"\\?\UNC\";
        const string DosPrefix = @"\\?\";
        if (path.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[UncPrefix.Length..];
        return path.StartsWith(DosPrefix, StringComparison.Ordinal) ? path[DosPrefix.Length..] : path;
    }
}
