using System;
using System.Reflection;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Common.Playback;

/// <summary>
/// Keeps PracticePlugin (9.x, optional, no hard reference) consistent after a seek made while paused.
/// Its SongSeeker records the audio position at pause (<c>_startTimeSamples</c>, and
/// <c>PlaybackPosition</c> as a float fraction) and on resume re-applies its own seek whenever
/// <c>RoundToInt(Lerp(0, samples, PlaybackPosition)) != _startTimeSamples</c>. Without this sync its
/// seek bar shows the old position and a float rounding mismatch could make it seek back over ours.
/// We set both fields to the new position, computed exactly the way it compares them.
/// </summary>
internal sealed class PracticePluginSync
{
    private const string SongSeekerTypeName = "PracticePlugin.Views.SongSeeker, PracticePlugin";
    private const string PracticeUiTypeName = "PracticePlugin.Views.PracticeUI, PracticePlugin";

    private readonly DiContainer _container;
    private bool _looked;
    private object? _songSeeker;
    private PropertyInfo? _playbackPosition;
    private FieldInfo? _startTimeSamples;
    private bool _warnedUi;
    private object? _practiceUi;

    public PracticePluginSync(DiContainer container)
    {
        _container = container;
    }

    /// <summary>
    /// After an in-place difficulty switch: PracticePlugin's PracticeUI caches the note jump speed and
    /// offset from BeatmapObjectSpawnController.InitData when injected (_defaultNJS/_njs,
    /// _defaultOffset/_offset), and any later change in its pause panel (speed, NJS, offset) calls
    /// AudioSpeedController.UpdateSpawnMovementData with those values. Point them at the new
    /// difficulty's values. Backing fields are written directly so no PropertyChanged fires: its
    /// AudioSpeedController reacts to "NJS"/"Offset" changes with UpdateSpawnMovementData, which re-runs
    /// BeatmapObjectSpawnController.Start and with it Chroma's environment enhancements. Instead the
    /// pause panel's BSML settings are told to re-read their values (GenericSetting.ReceiveValue),
    /// which redraws the labels through PracticeUI's formatters (the underline marks the new default).
    /// </summary>
    public void AfterDifficultySwap(float noteJumpSpeed, float noteJumpValue)
    {
        var ui = ResolvePracticeUi();
        if (ui == null)
        {
            return;
        }

        var written = 0;
        foreach (var name in new[] { "_defaultNJS", "_njs" })
        {
            written += TrySet(ui, name, noteJumpSpeed) ? 1 : 0;
        }

        foreach (var name in new[] { "_defaultOffset", "_offset" })
        {
            written += TrySet(ui, name, noteJumpValue) ? 1 : 0;
        }

        if (written != 4)
        {
            Plugin.Log.Warn($"[Playback] PracticePlugin PracticeUI fields changed ({written}/4 written); its NJS/offset panel may re-apply the old difficulty's values");
        }

        var refreshed = RefreshPanel(ui);

        Plugin.Log.Info($"[Playback] PracticePlugin NJS/offset set to {noteJumpSpeed:0.##}/{noteJumpValue:0.##} ({written}/4 fields, {refreshed} panel settings refreshed)");
    }

    /// <summary>
    /// PracticePlugin's PlayerInstaller binds PracticeUI (FromNewComponentAsViewController, AsCached)
    /// in the StandardPlayer container, the same one as ours. Resolve it from there; if that fails,
    /// find the live view controller instance. Every failure is logged once.
    /// </summary>
    private object? ResolvePracticeUi()
    {
        if (_practiceUi is UnityEngine.Object live && live == null)
        {
            _practiceUi = null; // destroyed
        }

        if (_practiceUi != null)
        {
            return _practiceUi;
        }

        var type = FindType(PracticeUiTypeName);
        if (type == null)
        {
            if (!_warnedUi)
            {
                _warnedUi = true;
                Plugin.Log.Info("[Playback] PracticePlugin not loaded; no NJS/offset sync needed");
            }

            return null;
        }

        try
        {
            _practiceUi = _container.TryResolve(type);
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[Playback] resolving PracticePlugin PracticeUI from the container failed: {e.Message}");
        }

        if (_practiceUi == null)
        {
            var found = Resources.FindObjectsOfTypeAll(type);
            foreach (var candidate in found)
            {
                if (candidate != null && candidate is Component { gameObject.scene.isLoaded: true })
                {
                    _practiceUi = candidate;
                    break;
                }
            }

            if (_practiceUi == null)
            {
                Plugin.Log.Warn($"[Playback] PracticePlugin PracticeUI not found (container and {found.Length} scene objects); its NJS/offset panel may re-apply the old difficulty's values");
            }
            else
            {
                Plugin.Log.Info("[Playback] PracticePlugin PracticeUI found as a scene object (not resolvable from the container)");
            }
        }

        return _practiceUi;
    }

    private static Type? FindType(string assemblyQualifiedName)
    {
        var type = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (type != null)
        {
            return type;
        }

        var comma = assemblyQualifiedName.IndexOf(',');
        var fullName = comma < 0 ? assemblyQualifiedName : assemblyQualifiedName.Substring(0, comma).Trim();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// The panel's BSML settings keep their own copy of the value (read once in Setup). PracticePlugin
    /// parses PracticeUI.bsml into the pause menu, not under the PracticeUI object, so find every BSML
    /// setting whose bound value lives on this PracticeUI (BSMLPropertyValue.host) and let it re-read.
    /// Settings parsed later read the new values themselves.
    /// </summary>
    private static int RefreshPanel(object ui)
    {
        var refreshed = 0;
        var hostField = typeof(BeatSaberMarkupLanguage.Parser.BSMLPropertyValue).GetField("host", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (hostField == null)
        {
            Plugin.Log.Warn("[Playback] BSMLPropertyValue.host not found; PracticePlugin's NJS/offset labels are not refreshed");
            return 0;
        }

        foreach (var setting in Resources.FindObjectsOfTypeAll<BeatSaberMarkupLanguage.Components.Settings.GenericSetting>())
        {
            if (setting == null || setting.AssociatedValue is not BeatSaberMarkupLanguage.Parser.BSMLPropertyValue value)
            {
                continue;
            }

            if (!ReferenceEquals(hostField.GetValue(value), ui))
            {
                continue;
            }

            setting.ReceiveValue();
            refreshed++;
        }

        return refreshed;
    }

    private static bool TrySet(object target, string name, float value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(float))
        {
            return false;
        }

        field.SetValue(target, value);
        return true;
    }

    public void AfterPausedSeek(AudioSource? audioSource)
    {
        if (audioSource == null || audioSource.clip == null || audioSource.clip.samples <= 0 || !Resolve())
        {
            return;
        }

        var position = audioSource.timeSamples / (float)audioSource.clip.samples;
        _playbackPosition!.SetValue(_songSeeker, position);
        _startTimeSamples!.SetValue(_songSeeker, Mathf.RoundToInt(Mathf.Lerp(0f, audioSource.clip.samples, position)));
    }

    private bool Resolve()
    {
        if (!_looked)
        {
            _looked = true;
            var type = Type.GetType(SongSeekerTypeName, throwOnError: false);
            if (type != null)
            {
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _playbackPosition = type.GetProperty("PlaybackPosition", Flags);
                _startTimeSamples = type.GetField("_startTimeSamples", Flags);
                _songSeeker = _container.TryResolve(type);
                if (_songSeeker != null && (_playbackPosition == null || _startTimeSamples == null))
                {
                    Plugin.Log.Warn("[Playback] PracticePlugin SongSeeker found but its fields changed; its seek bar will not follow skips");
                    _songSeeker = null;
                }
            }
        }

        return _songSeeker != null;
    }
}
