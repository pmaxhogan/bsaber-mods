using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MaxTweaks.Common;
using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Records the arguments of the last StandardLevelScenesTransitionSetupDataSO.Init call (every
/// overload), so the restart fallback can run Init again with only the difficulty key and practice
/// settings changed. Running Init (instead of patching the existing setup data) lets every mod hook
/// on it run for the target difficulty: Heck's StandardModuleActivator prefix (turns Noodle/Chroma
/// modules and their patches on or off for the target map), Heck's setup-data subclass transpiler,
/// and the game's own environment and colour scheme resolution (InitEnvironmentInfo/InitColorInfo).
///
/// Global Harmony prefix through SafePatcher (feature id "DifficultySwitch"), applied at app start,
/// priority First so the arguments are captured before other prefixes (Heck's takes
/// overrideEnvironmentSettings by ref) change them. The prefix only copies the argument array.
/// </summary>
internal static class TransitionInitCapture
{
    /// <summary>The last Init call: overload and a copy of its arguments, or null.</summary>
    public static CapturedInit? Last { get; private set; }

    public static IEnumerable<MethodBase> TargetMethods() =>
        typeof(StandardLevelScenesTransitionSetupDataSO).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(StandardLevelScenesTransitionSetupDataSO.Init))
            .Cast<MethodBase>();

    [HarmonyPriority(Priority.First)]
    public static void Prefix(StandardLevelScenesTransitionSetupDataSO __instance, MethodBase __originalMethod, object[] __args)
    {
        try
        {
            Last = new CapturedInit(__instance, (MethodInfo)__originalMethod, (object[])__args.Clone());
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[DifficultySwitch] capturing the level start arguments failed: {e}");
        }
    }

    /// <summary>Harmony patch class for SafePatcher (TargetMethods + Prefix above).</summary>
    [HarmonyPatch]
    internal static class Patch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets() => TargetMethods();

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Before(StandardLevelScenesTransitionSetupDataSO __instance, MethodBase __originalMethod, object[] __args) =>
            Prefix(__instance, __originalMethod, __args);
    }
}

/// <summary>One recorded StandardLevelScenesTransitionSetupDataSO.Init call.</summary>
internal sealed class CapturedInit
{
    public CapturedInit(StandardLevelScenesTransitionSetupDataSO target, MethodInfo method, object[] args)
    {
        Target = target;
        Method = method;
        Args = args;
    }

    public StandardLevelScenesTransitionSetupDataSO Target { get; }

    public MethodInfo Method { get; }

    public object[] Args { get; }

    /// <summary>A copy of the arguments with the key and practice settings replaced; throws when the overload lacks them.</summary>
    public object[] With(BeatmapKey key, PracticeSettings practice)
    {
        var args = (object[])Args.Clone();
        var parameters = Method.GetParameters();
        var keyIndex = Array.FindIndex(parameters, p => p.Name == "beatmapKey");
        var practiceIndex = Array.FindIndex(parameters, p => p.Name == "practiceSettings");
        if (keyIndex < 0 || practiceIndex < 0)
        {
            throw new InvalidOperationException($"Init overload without beatmapKey/practiceSettings: {Method}");
        }

        args[keyIndex] = key;
        args[practiceIndex] = practice;
        return args;
    }
}

/// <summary>App container: applies <see cref="TransitionInitCapture"/> once the feature exists.</summary>
internal sealed class TransitionInitCaptureInstaller : IInitializable
{
    private readonly SafePatcher _patcher;

    public TransitionInitCaptureInstaller(SafePatcher patcher)
    {
        _patcher = patcher;
    }

    public void Initialize()
    {
        if (_patcher.TryPatch("DifficultySwitch", typeof(TransitionInitCapture.Patch)) == null)
        {
            Plugin.Log.Warn("[DifficultySwitch] level start capture unavailable; the restart fallback will only work between difficulties that need no different mods or environment");
        }
    }
}
