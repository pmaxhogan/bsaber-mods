using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using IPA.Logging;
using IPA.Utilities;

namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Copies every MaxTweaks log line (plugin logger and its children, so features and the debug
/// channel) to UserData/MaxTweaks.debug-log.txt, opened with read/write/delete sharing.
/// Why: BSIPA opens Logs/_latest.log with FileShare.None for every print batch, so a QA script
/// that tails it makes BSIPA drop that batch ("printer errored: Sharing violation"). Scripts read
/// this mirror instead. Attached only with --debug. The first line names the process id so
/// scripts/qa/launch.ps1 can tell this run's file from the previous one.
/// </summary>
internal sealed class DebugLogMirror : LogPrinter
{
    public const string FileName = "MaxTweaks.debug-log.txt";

    private readonly StreamWriter _writer;

    private DebugLogMirror(StreamWriter writer)
    {
        _writer = writer;
    }

    public override Logger.LogLevel Filter { get; set; } = Logger.LogLevel.All;

    public static void Attach(Logger logger)
    {
        try
        {
            if (logger is not StandardLogger standard)
            {
                return;
            }

            var path = Path.Combine(UnityGame.UserDataPath, FileName);
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            writer.WriteLine($"[MaxTweaks.Debug] session pid={Process.GetCurrentProcess().Id} started={DateTime.Now:yyyy-MM-ddTHH:mm:ss}");
            writer.Flush();
            standard.AddPrinter(new DebugLogMirror(writer));
        }
        catch (Exception e)
        {
            logger.Warn($"[MaxTweaks.Debug] log mirror unavailable: {e.Message}");
        }
    }

    public override void Print(Logger.Level level, DateTime time, string logName, string message)
    {
        foreach (var line in message.Split('\n'))
        {
            _writer.WriteLine($"[{level.ToString().ToUpperInvariant()} @ {time:HH:mm:ss} | {logName}] {line.TrimEnd('\r')}");
        }
    }
}
