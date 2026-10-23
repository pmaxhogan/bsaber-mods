using System;

namespace MaxTweaks.Common;

/// <summary>
/// Wrap code that runs inside the game's own call stack (patch bodies, gameplay events, per-frame
/// updates). An exception escaping there can break gameplay itself (e.g. notes stop spawning), so
/// log it and keep the game running instead.
/// </summary>
internal static class Guard
{
    public static void Run(string feature, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[{feature}] {e}");
        }
    }

    public static T Run<T>(string feature, Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[{feature}] {e}");
            return fallback;
        }
    }
}
