using System;
using System.Globalization;
using System.IO;
using System.Text;
using MaxTweaks.Common;
using MaxTweaks.Core;
using MaxTweaks.Core.ActivePause.Flags;
using MaxTweaks.Core.AutoReload;
using UnityEngine;

namespace MaxTweaks.Features.ActivePause;

/// <summary>Outcome of <see cref="FlagWriter.Save"/>.</summary>
internal sealed class FlagWriteResult
{
    public bool Ok { get; set; }

    /// <summary>Why the flag was not saved (null when <see cref="Ok"/>).</summary>
    public string? Error { get; set; }

    /// <summary>flags.json (empty when the folder was unusable).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Full path of the requested screenshot, or null when none was taken.</summary>
    public string? ScreenshotPath { get; set; }

    /// <summary>Name the unreadable previous flags.json was moved to, or null.</summary>
    public string? CorruptBackup { get; set; }
}

/// <summary>
/// Appends a flag to flags.json in the map folder: read-modify-write of the whole file (unknown keys
/// and existing flags are kept, see <see cref="FlagDocument"/>), written to flags.json.tmp and then
/// swapped in atomically (File.Replace over an existing flags.json, File.Move for a new one). A file
/// that does not parse is renamed to flags.json.corrupt-&lt;timestamp&gt; and a fresh document
/// started. A flag id already in the file is regenerated. Only after the JSON is written is the
/// screenshot requested (ScreenCapture, the mirror view in VR, the desktop view in FPFC) into
/// flags/&lt;id&gt;.png; Unity writes it at the end of the frame and reports nothing, so only a failure
/// to request it can be seen (logged; the flag then lists a file that does not exist), and that never
/// fails the save. Every write marks the folder for Auto reload (<see cref="SidecarWrites"/>), so it
/// is not taken for a map edit.
/// </summary>
internal static class FlagWriter
{
    private const int MaxIdAttempts = 20;

    /// <param name="newId">Makes another id when <paramref name="flag"/>'s id is already in the file.</param>
    public static FlagWriteResult Save(FlagRecord flag, string folder, string levelId, string songName, bool screenshot, Func<string> newId)
    {
        var result = new FlagWriteResult();
        if (!SafeMapPaths.TryResolveInside(folder, MapPaths.FlagsFileName, out var path))
        {
            result.Error = "map folder not usable";
            return result;
        }

        result.Path = path;
        SidecarWrites.Guard.Mark(folder);
        string? screenshotPath = null;
        try
        {
            var document = Load(path, levelId, songName, result);
            for (var attempt = 0; attempt < MaxIdAttempts && document.Flags.Exists(f => f.Id == flag.Id); attempt++)
            {
                flag.Id = newId();
            }

            flag.Screenshot = null;
            if (screenshot)
            {
                screenshotPath = PrepareScreenshot(flag, folder);
            }

            document.Append(flag);
            var temp = path + ".tmp";
            File.WriteAllText(temp, document.Serialize(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }

            result.Ok = true;
        }
        catch (Exception e)
        {
            result.Error = e.Message;
        }
        finally
        {
            SidecarWrites.Guard.Mark(folder);
        }

        if (result.Ok && screenshotPath != null)
        {
            try
            {
                ScreenCapture.CaptureScreenshot(screenshotPath);
                result.ScreenshotPath = screenshotPath;
            }
            catch (Exception e)
            {
                Plugin.Log.Warn($"[ActivePause] flag screenshot failed: {e.Message}");
            }
        }

        return result;
    }

    private static FlagDocument Load(string path, string levelId, string songName, FlagWriteResult result)
    {
        if (!File.Exists(path))
        {
            return FlagDocument.Empty(levelId, songName);
        }

        var text = File.ReadAllText(path);
        try
        {
            return FlagDocument.Parse(text);
        }
        catch (FlagDocumentException e)
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Move(path, backup);
            result.CorruptBackup = System.IO.Path.GetFileName(backup);
            Plugin.Log.Warn($"[ActivePause] flag file error: {path} is not a flag file ({e.Message}); moved to {result.CorruptBackup}, starting a new one");
            return FlagDocument.Empty(levelId, songName);
        }
    }

    // Creates flags/ and records flags/<id>.png in the flag; the capture itself is requested after
    // the JSON write succeeded. Null (and no screenshot in the flag) when the folder cannot be made.
    private static string? PrepareScreenshot(FlagRecord flag, string folder)
    {
        var relative = MapPaths.FlagsFolderName + "/" + flag.Id + ".png";
        try
        {
            if (!SafeMapPaths.TryResolveInside(folder, relative, out var full))
            {
                return null;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            flag.Screenshot = relative;
            return full;
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[ActivePause] flag screenshot folder failed: {e.Message}");
            return null;
        }
    }
}
