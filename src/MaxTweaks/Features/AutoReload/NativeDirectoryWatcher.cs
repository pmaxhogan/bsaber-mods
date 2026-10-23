using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Recursive directory watcher on Win32 ReadDirectoryChangesW, run on its own background thread with
/// overlapped I/O so <see cref="Dispose"/> can cancel the pending read.
///
/// Why not System.IO.FileSystemWatcher: the game's Mono picks DefaultWatcher on Windows (logged
/// "backend DefaultWatcher" in every game log), a managed 750 ms polling thread that stats the whole
/// tree, scans once synchronously on the caller's thread and never raises Error.
///
/// Callbacks run on the watcher thread: <c>onPath</c> gets full paths of changed entries (both names
/// of a rename), <c>onOverflow</c> means events were lost (buffer overflow / ERROR_NOTIFY_ENUM_DIR),
/// <c>onError</c> means the watcher stopped for good (the caller should fall back to polling).
/// </summary>
internal sealed class NativeDirectoryWatcher : IDisposable
{
    private const int BufferSize = 64 * 1024;
    private const uint FileListDirectory = 0x1;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint NotifyFilter = 0x1 /* FILE_NAME */ | 0x2 /* DIR_NAME */ | 0x8 /* SIZE */ | 0x10 /* LAST_WRITE */;
    private const int ErrorOperationAborted = 995;
    private const int ErrorIoPending = 997;
    private const int ErrorNotifyEnumDir = 1022;

    private readonly string _path;
    private readonly Action<string> _onPath;
    private readonly Action _onOverflow;
    private readonly Action<string> _onError;
    private readonly ManualResetEvent _stop = new(false);
    private SafeFileHandle? _handle;
    private Thread? _thread;

    public NativeDirectoryWatcher(string path, Action<string> onPath, Action onOverflow, Action<string> onError)
    {
        _path = path;
        _onPath = onPath;
        _onOverflow = onOverflow;
        _onError = onError;
    }

    /// <summary>Opens the directory and starts the thread. Throws if the directory cannot be watched.</summary>
    public void Start()
    {
        var handle = CreateFileW(_path, FileListDirectory, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics | FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"CreateFileW failed for {_path}");
        }

        _handle = handle;
        _thread = new Thread(Run) { IsBackground = true, Name = "MaxTweaks.AutoReload.Watcher" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread != null && _thread != Thread.CurrentThread)
        {
            _thread.Join(2000);
        }

        _thread = null;
    }

    private void Run()
    {
        var buffer = Marshal.AllocHGlobal(BufferSize);
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
        using var ioDone = new ManualResetEvent(false);
        var waits = new WaitHandle[] { ioDone, _stop };
        var handle = _handle!;
        try
        {
            while (!_stop.WaitOne(0))
            {
                ioDone.Reset();
                Marshal.StructureToPtr(new NativeOverlapped { EventHandle = ioDone.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
                if (!ReadDirectoryChangesW(handle, buffer, BufferSize, true, NotifyFilter, IntPtr.Zero, overlapped, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorIoPending)
                    {
                        _onError($"ReadDirectoryChangesW failed (error {error})");
                        return;
                    }
                }

                if (WaitHandle.WaitAny(waits) == 1)
                {
                    // Stopping: cancel the read and wait until the kernel no longer owns the buffers.
                    CancelIoEx(handle, overlapped);
                    GetOverlappedResult(handle, overlapped, out _, true);
                    return;
                }

                if (!GetOverlappedResult(handle, overlapped, out var bytes, false))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNotifyEnumDir)
                    {
                        _onOverflow();
                        continue;
                    }

                    if (error != ErrorOperationAborted)
                    {
                        _onError($"ReadDirectoryChangesW result failed (error {error})");
                    }

                    return;
                }

                if (bytes == 0)
                {
                    // The kernel buffer overflowed: the changes are lost, only a rescan helps.
                    _onOverflow();
                    continue;
                }

                Parse(buffer, (int)bytes);
            }
        }
        catch (Exception e)
        {
            _onError(e.ToString());
        }
        finally
        {
            handle.Dispose();
            Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Parse(IntPtr buffer, int length)
    {
        // FILE_NOTIFY_INFORMATION: DWORD NextEntryOffset, DWORD Action, DWORD FileNameLength (bytes), WCHAR FileName[].
        var offset = 0;
        while (offset + 12 <= length)
        {
            var record = IntPtr.Add(buffer, offset);
            var next = Marshal.ReadInt32(record, 0);
            var nameBytes = Marshal.ReadInt32(record, 8);
            if (nameBytes > 0 && offset + 12 + nameBytes <= length)
            {
                var relative = Marshal.PtrToStringUni(IntPtr.Add(record, 12), nameBytes / 2);
                _onPath(Path.Combine(_path, relative));
            }

            if (next <= 0)
            {
                return;
            }

            offset += next;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadDirectoryChangesW(
        SafeFileHandle directory,
        IntPtr buffer,
        int bufferLength,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        IntPtr bytesReturned,
        IntPtr overlapped,
        IntPtr completionRoutine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
