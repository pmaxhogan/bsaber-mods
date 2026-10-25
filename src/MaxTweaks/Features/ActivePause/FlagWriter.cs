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
/// copied over flags.json. A file that does not parse is renamed to flags.json.corrupt-&lt;timestamp&gt;
/// and a fresh document started. The screenshot (ScreenCapture, the mirror view in VR, the desktop
/// view in FPFC) goes to flags/&lt;id&gt;.png; Unity writes it at the end of the frame and reports
/// nothing, so only a failure to request it can be seen, and that never fails the save. Every write
/// marks the folder for Auto reload (<see cref="SidecarWrites"/>), so it is not taken for a map edit.
/// </summary>
internal static class FlagWriter
{
    public static FlagWriteResult Save(FlagRecord flag, string folder, string levelId, string songName, bool screenshot)
    {
        var result = new FlagWriteResult();
        if (!SafeMapPaths.TryResolveInside(folder, MapPaths.FlagsFileName, out var path))
        {
            result.Error = "map folder not usable";
            return result;
        }

        result.Path = path;
        SidecarWrites.Guard.Mark(folder);
        flag.Screenshot = null;
        if (screenshot)
        {
            TakeScreenshot(flag, folder, result);
        }

        try
        {
            var document = Load(path, levelId, songName, result);
            document.Append(flag);
            var temp = path + ".tmp";
            File.WriteAllText(temp, document.Serialize(), new UTF8Encoding(false));
            File.Copy(temp, path, overwrite: true);
            File.Delete(temp);
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

    private static void TakeScreenshot(FlagRecord flag, string folder, FlagWriteResult result)
    {
        var relative = MapPaths.FlagsFolderName + "/" + flag.Id + ".png";
        try
        {
            if (!SafeMapPaths.TryResolveInside(folder, relative, out var full))
            {
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            ScreenCapture.CaptureScreenshot(full);
            flag.Screenshot = relative;
            result.ScreenshotPath = full;
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[ActivePause] flag screenshot failed: {e.Message}");
        }
    }
}
