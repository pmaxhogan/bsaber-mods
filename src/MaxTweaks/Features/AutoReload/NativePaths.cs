using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MaxTweaks.Features.AutoReload;

/// <summary>Resolves junctions and symlinks to their final target (Win32 GetFinalPathNameByHandle).</summary>
internal static class NativePaths
{
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>The final path of <paramref name="path"/>, or the input unchanged if it cannot be resolved.</summary>
    public static string ResolveFinalPath(string path)
    {
        try
        {
            using var handle = CreateFileW(path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return path;
            }

            var buffer = new StringBuilder(1024);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
            {
                return path;
            }

            var resolved = buffer.ToString();
            if (resolved.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            {
                return @"\\" + resolved.Substring(8);
            }

            return resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved.Substring(4) : resolved;
        }
        catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is IOException)
        {
            return path;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder filePath, uint filePathSize, uint flags);
}
