using System;
using System.Globalization;
using System.Text;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>What a finished recording is named after.</summary>
public sealed class RecordingLabel
{
    public RecordingLabel(string? songName, string? difficulty)
    {
        SongName = songName ?? string.Empty;
        Difficulty = difficulty ?? string.Empty;
    }

    public string SongName { get; }

    /// <summary>Already in display form, see <see cref="RecordingFileName.DifficultyLabel"/>.</summary>
    public string Difficulty { get; }

    public override string ToString() => "'" + SongName + "' (" + Difficulty + ")";
}

/// <summary>
/// Builds the new name of a saved recording: "&lt;OBS's own name&gt; - &lt;song&gt; - &lt;difficulty&gt;&lt;ext&gt;" in
/// the same folder, so OBS's timestamp stays in front. Works on the path as text (both '/' and '\'
/// separate folders, as OBS reports Windows paths with '/'), so it behaves the same on any OS.
/// </summary>
public static class RecordingFileName
{
    public const int MaxSongNameLength = 80;
    public const int MaxDifficultyLength = 40;

    /// <summary>
    /// What OBS writes recordings as: its standard formats (MP4, hybrid and fragmented MP4/MOV, MKV,
    /// MOV, FLV, MPEG-TS, HLS) plus the usual containers of its custom FFmpeg output. Lower case.
    /// </summary>
    public static readonly string[] RecordingExtensions =
    {
        ".mp4", ".mkv", ".mov", ".flv", ".ts", ".m3u8", ".avi", ".webm", ".m4v", ".mpg", ".mxf",
    };

    private const string Separator = " - ";
    private const string InvalidCharacters = "<>:\"/\\|?*";

    private static readonly string[] DeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The difficulty as the game shows it (ExpertPlus becomes Expert+), with the characteristic in
    /// front unless it is Standard, and " - Practice" after it for a practice-mode run:
    /// "Expert+", "OneSaber Hard", "Expert - Practice".
    /// </summary>
    public static string DifficultyLabel(string? difficulty, string? characteristic, bool practice = false)
    {
        var label = DifficultyName(difficulty, characteristic);
        return practice ? (label.Length == 0 ? "Practice" : label + " - Practice") : label;
    }

    private static string DifficultyName(string? difficulty, string? characteristic)
    {
        var name = (difficulty ?? string.Empty).Trim();
        if (string.Equals(name, "ExpertPlus", StringComparison.OrdinalIgnoreCase))
        {
            name = "Expert+";
        }

        var mode = (characteristic ?? string.Empty).Trim();
        if (mode.Length == 0 || string.Equals(mode, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return name.Length == 0 ? mode : mode + " " + name;
    }

    /// <summary>
    /// Makes text safe as part of a Windows file name: characters Windows forbids and control
    /// characters become spaces, runs of white space collapse to one space, leading spaces and
    /// trailing spaces and dots go, and the result is cut to <paramref name="maxLength"/> characters
    /// (0 = no limit) without splitting a surrogate pair. Other Unicode is kept.
    /// </summary>
    public static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text!.Length);
        var lastWasSpace = true;
        foreach (var raw in text)
        {
            var c = raw < ' ' || raw == '\u007f' || InvalidCharacters.IndexOf(raw) >= 0 ? ' ' : raw;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }

        var result = TrimEnd(builder.ToString());
        if (maxLength > 0 && result.Length > maxLength)
        {
            var cut = maxLength;
            if (char.IsHighSurrogate(result[cut - 1]))
            {
                cut--;
            }

            result = TrimEnd(result.Substring(0, cut));
        }

        return result;
    }

    /// <summary>
    /// The path to rename <paramref name="originalPath"/> to. Never returns a path that
    /// <paramref name="exists"/> reports as taken: " (2)", " (3)"... is added before the extension.
    /// Returns the original path unchanged when there is nothing to add.
    /// </summary>
    public static string Build(string originalPath, string? songName, string? difficulty, Func<string, bool> exists)
    {
        Split(originalPath, out var folder, out var name, out var extension);
        var result = name;
        var song = Sanitize(songName, MaxSongNameLength);
        if (song.Length > 0)
        {
            result += Separator + song;
        }

        var level = Sanitize(difficulty, MaxDifficultyLength);
        if (level.Length > 0)
        {
            result += Separator + level;
        }

        if (result.Length == name.Length)
        {
            return originalPath;
        }

        var candidate = folder + result + extension;
        for (var n = 2; n < 1000 && exists(candidate); n++)
        {
            candidate = folder + result + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + extension;
        }

        return candidate;
    }

    /// <summary>The file name with its extension, without the folder.</summary>
    public static string NameOf(string path)
    {
        Split(path, out _, out var name, out var extension);
        return name + extension;
    }

    /// <summary>
    /// Whether a file name reported by OBS may be renamed. OBS is trusted to press record, not to
    /// point the game at arbitrary files, so only this is accepted: a full path on a drive of this PC
    /// ("C:\..." or "C:/..."), without "." or ".." parts, whose file has one of the
    /// <see cref="RecordingExtensions"/>. Network and device paths ("\\server\share", "\\?\", "\\.\"),
    /// relative paths, alternate data streams and device names (NUL, COM1...) are refused, with
    /// <paramref name="reason"/> saying why (fit for a log line; it never repeats the path).
    /// </summary>
    /// <param name="windowsPaths">
    /// True for Windows rules (where the game runs). False only for running the same code on other
    /// systems (the unit tests in CI): a full path is then one starting with a single '/'.
    /// </param>
    public static bool IsLocalRecordingPath(string? path, bool windowsPaths, out string reason)
    {
        if (string.IsNullOrEmpty(path))
        {
            reason = "no path";
            return false;
        }

        var text = path!;
        foreach (var c in text)
        {
            if (c < ' ' || c == '\u007f')
            {
                reason = "the path has control characters";
                return false;
            }
        }

        if (text.Length >= 2 && IsSeparator(text[0], windowsPaths) && IsSeparator(text[1], windowsPaths))
        {
            reason = "it is a network or device path";
            return false;
        }

        int rootLength;
        if (windowsPaths)
        {
            var driveLetter = (text[0] >= 'A' && text[0] <= 'Z') || (text[0] >= 'a' && text[0] <= 'z');
            if (text.Length < 3 || !driveLetter || text[1] != ':' || !IsSeparator(text[2], true))
            {
                reason = "it is not a full path on a drive of this PC";
                return false;
            }

            if (text.IndexOf(':', 2) >= 0)
            {
                reason = "it names a stream or a device";
                return false;
            }

            rootLength = 3;
        }
        else
        {
            if (text[0] != '/')
            {
                reason = "it is not a full path";
                return false;
            }

            rootLength = 1;
        }

        var parts = text.Substring(rootLength).Split(windowsPaths ? new[] { '/', '\\' } : new[] { '/' });
        foreach (var part in parts)
        {
            if (part == "." || part == "..")
            {
                reason = "it has relative parts";
                return false;
            }
        }

        var file = parts[parts.Length - 1];
        var dot = file.LastIndexOf('.');
        var extension = dot > 0 ? file.Substring(dot).ToLowerInvariant() : string.Empty;
        if (Array.IndexOf(RecordingExtensions, extension) < 0)
        {
            reason = "it does not have a recording's file extension";
            return false;
        }

        if (windowsPaths && Array.IndexOf(DeviceNames, file.Substring(0, file.IndexOf('.')).TrimEnd(' ').ToUpperInvariant()) >= 0)
        {
            reason = "it names a device";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsSeparator(char c, bool windowsPaths) => c == '/' || (windowsPaths && c == '\\');

    private static string TrimEnd(string text) => text.TrimEnd(' ', '.');

    private static void Split(string path, out string folder, out string name, out string extension)
    {
        var slash = path.LastIndexOfAny(new[] { '/', '\\' });
        folder = path.Substring(0, slash + 1);
        var file = path.Substring(slash + 1);
        var dot = file.LastIndexOf('.');
        if (dot > 0)
        {
            name = file.Substring(0, dot);
            extension = file.Substring(dot);
        }
        else
        {
            name = file;
            extension = string.Empty;
        }
    }
}
