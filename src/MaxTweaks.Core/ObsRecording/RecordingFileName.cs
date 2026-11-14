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

    private const string Separator = " - ";
    private const string InvalidCharacters = "<>:\"/\\|?*";

    /// <summary>
    /// The difficulty as the game shows it (ExpertPlus becomes Expert+), with the characteristic in
    /// front unless it is Standard: "Expert+", "OneSaber Hard".
    /// </summary>
    public static string DifficultyLabel(string? difficulty, string? characteristic)
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
