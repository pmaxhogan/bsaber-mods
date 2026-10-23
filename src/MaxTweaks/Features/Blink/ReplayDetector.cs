using System;
using System.Reflection;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// Best-effort detection of the installed replay viewers, by reflection so neither mod is a
/// dependency. Absent mods (or renamed members after an update) read as "not a replay".
/// Read at gameplay install time; both mods set their flag before the level scene is built.
/// - BeatLeader: public static bool BeatLeader.Replayer.ReplayerLauncher.IsStartedAsReplay
///   (BeatLeader.dll; set true in StartReplay, false when the replay ends).
/// - ScoreSaber: internal static ReplayState ScoreSaber.Plugin.ReplayState, internal field
///   bool IsPlaybackEnabled (ScoreSaber.dll, ScoreSaber.Core.ReplaySystem.ReplayState).
/// </summary>
internal static class ReplayDetector
{
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool IsWatchingReplay()
    {
        return IsBeatLeaderReplay() || IsScoreSaberReplay();
    }

    /// <summary>Looks the type up in an already loaded assembly only (never triggers a load).</summary>
    private static Type? FindType(string assemblyName, string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == assemblyName)
            {
                return assembly.GetType(typeName, throwOnError: false);
            }
        }

        return null;
    }

    private static bool IsBeatLeaderReplay()
    {
        try
        {
            var type = FindType("BeatLeader", "BeatLeader.Replayer.ReplayerLauncher");
            var property = type?.GetProperty("IsStartedAsReplay", StaticAny);
            return property?.GetValue(null) is true;
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"[Blink] BeatLeader replay check failed: {e.Message}");
            return false;
        }
    }

    private static bool IsScoreSaberReplay()
    {
        try
        {
            var type = FindType("ScoreSaber", "ScoreSaber.Plugin");
            var state = type?.GetProperty("ReplayState", StaticAny)?.GetValue(null);
            if (state == null)
            {
                return false;
            }

            var field = state.GetType().GetField("IsPlaybackEnabled", InstanceAny);
            return field?.GetValue(state) is true;
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"[Blink] ScoreSaber replay check failed: {e.Message}");
            return false;
        }
    }
}
