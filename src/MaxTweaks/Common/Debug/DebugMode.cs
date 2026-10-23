using System;
using System.Linq;
using System.Text;

namespace MaxTweaks.Common.Debugging;

/// <summary>
/// QA debug channel switch. True only when the game was started with <c>--debug</c> (the same flag
/// BSIPA uses for debug logging). Plugin.cs installs the Debug* installers only when this is true,
/// so without the flag none of Common/Debug is bound and nothing runs.
/// Namespace is "Debugging" (not "Debug") so it never shadows UnityEngine.Debug.
/// </summary>
internal static class DebugMode
{
    private static bool? _enabled;

    public static bool Enabled => _enabled ??= Detect();

    private static bool Detect()
    {
        try
        {
            return Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Log helpers: every line starts with "[MaxTweaks.Debug] " and is forced to ASCII.</summary>
internal static class DebugLog
{
    public const string Prefix = "[MaxTweaks.Debug] ";

    public static void Info(string message) => Plugin.Log.Info(Prefix + Ascii(message));

    public static void Event(string message) => Info("event " + message);

    public static void Ok(string? id, string verb, string message) => Info($"ok #{id ?? "-"} {verb}: {message}");

    public static void Error(string? id, string verb, string message) => Plugin.Log.Warn(Prefix + Ascii($"error #{id ?? "-"} {verb}: {message}"));

    /// <summary>Replaces non-ASCII and control characters (song names can contain any) with '?'.</summary>
    public static string Ascii(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c >= 0x20 && c < 0x7f ? c : '?');
        }

        return sb.ToString();
    }
}

/// <summary>Thrown by a command handler to report a user-facing error (logged as "error #id").</summary>
internal sealed class DebugCommandException : Exception
{
    public DebugCommandException(string message)
        : base(message)
    {
    }
}
