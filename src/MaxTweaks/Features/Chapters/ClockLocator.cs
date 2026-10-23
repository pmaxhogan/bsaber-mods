using System;
using System.IO;
using IPA.Loader;
using IPA.Utilities;
using MaxTweaks.Core.Chapters;

namespace MaxTweaks.Features.Chapters;

/// <summary>
/// Finds the gameplay clock so the chapter stack can sit above it. The only clock mod installed is
/// Enhancements (3.0.18): its BasicClock is a BSML FloatingScreen (150x50 units, scale 0.02) at
/// Clock.Position with Euler rotation Clock.Rotation and font size Clock.Size, created in gameplay when
/// Clock.Enabled and Clock.ShowInGame (decompiled Enhancements.Clock.BasicClock / XGameInstaller).
/// Its settings live in UserData/Enhancements.json and are read once per song.
/// </summary>
internal static class ClockLocator
{
    public static ClockPlacement? Find()
    {
        try
        {
            if (PluginManager.GetPluginFromId("Enhancements") == null)
            {
                return null;
            }

            var path = Path.Combine(UnityGame.UserDataPath, "Enhancements.json");
            return ChaptersPlacement.ParseEnhancementsClock(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Plugin.Log.Debug($"[Chapters] cannot read Enhancements.json, assuming its default clock: {e.Message}");
            return ClockPlacement.EnhancementsDefault;
        }
    }
}
