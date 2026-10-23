using System;
using HarmonyLib;

namespace MaxTweaks.Common;

/// <summary>
/// Applies a feature's Harmony patch classes as one unit with its own Harmony id. If any patch
/// fails (usually a game update renamed the target), every patch of that feature is reverted and
/// the feature is marked failed; other features are unaffected.
/// Prefer SiraUtil affinity patches or game events where they fit; use this for global patches.
/// </summary>
internal class SafePatcher
{
    private readonly FeatureStatus _status;

    public SafePatcher(FeatureStatus status)
    {
        _status = status;
    }

    public static string HarmonyId(string feature) => $"pmaxhogan.MaxTweaks.{feature}";

    /// <returns>The Harmony instance on success (call UnpatchSelf to remove), or null on failure.</returns>
    public Harmony? TryPatch(string feature, params Type[] patchClasses)
    {
        var harmony = new Harmony(HarmonyId(feature));
        try
        {
            foreach (var type in patchClasses)
            {
                harmony.CreateClassProcessor(type).Patch();
            }

            return harmony;
        }
        catch (Exception e)
        {
            try
            {
                harmony.UnpatchSelf();
            }
            catch (Exception unpatchError)
            {
                Plugin.Log.Error(unpatchError);
            }

            _status.MarkFailed(feature, $"patching failed: {e.Message}");
            Plugin.Log.Debug(e);
            return null;
        }
    }
}
