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
    private const string ConfigTypeName = "PracticePlugin.Configuration.PluginConfig, PracticePlugin";

    private readonly DiContainer _container;
    private bool _looked;
    private object? _songSeeker;
    private PropertyInfo? _playbackPosition;
    private FieldInfo? _startTimeSamples;
    private bool _uiMissed; // looked once and not found: no type/scene scan or warning on every call
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

        SyncPauseSnapshot(ui, noteJumpSpeed, noteJumpValue);
        var refreshed = RefreshPanel(ui);

        Plugin.Log.Info($"[Playback] PracticePlugin NJS/offset set to {noteJumpSpeed:0.##}/{noteJumpValue:0.##} ({written}/4 fields, {refreshed} panel settings refreshed)");
    }

    /// <summary>
    /// The NJS PracticePlugin's pause panel holds (PracticeUI._njs: what the player picked, before
    /// its optional "adjust NJS with speed" scaling), or null when PracticePlugin is not loaded or
    /// its fields changed.
    /// </summary>
    public float? ReadNoteJumpSpeed()
    {
        var ui = ResolvePracticeUi();
        var field = ui?.GetType().GetField("_njs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null && field.FieldType == typeof(float) ? (float)field.GetValue(ui) : null;
    }

    /// <summary>
    /// PracticePlugin's PluginConfig.AdjustNJSWithSpeed: when on, its NJS update applies
    /// NJS / song speed to the spawn controller (AudioSpeedController.UpdateSpawnMovementData).
    /// False when PracticePlugin is not loaded.
    /// </summary>
    public bool AdjustsNoteJumpSpeedWithSpeed()
    {
        var type = FindType(ConfigTypeName);
        if (type == null)
        {
            return false;
        }

        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var instance = type.GetProperty("Instance", Static)?.GetValue(null);
        var value = instance?.GetType().GetProperty("AdjustNJSWithSpeed", BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
        return value is true;
    }

    /// <summary>
    /// After an NJS change from the difficulty switch's NJS row (the spawn controller already
    /// re-inited): PracticeUI._njs (not its default) takes the new value, so its pause panel shows it
    /// and its next speed/offset change re-applies it instead of the old one. Written without
    /// PropertyChanged for the same reason as <see cref="AfterDifficultySwap"/>.
    /// </summary>
    public void AfterNoteJumpSpeedChange(float noteJumpSpeed)
    {
        var ui = ResolvePracticeUi();
        if (ui == null)
        {
            return;
        }

        if (!TrySet(ui, "_njs", noteJumpSpeed))
        {
            Plugin.Log.Warn("[Playback] PracticePlugin PracticeUI._njs not found; its NJS panel may re-apply the old NJS");
            return;
        }

        SyncPauseSnapshot(ui, noteJumpSpeed, null);
        var refreshed = RefreshPanel(ui);
        Plugin.Log.Info($"[Playback] PracticePlugin NJS set to {noteJumpSpeed:0.##} ({refreshed} panel settings refreshed)");
    }

    /// <summary>
    /// PracticeUI snapshots Speed/NJS/Offset on pause (_beforeDeactiveParam) and, when they differ on
    /// resume, re-seeks to the pause position (SongSeeker.ApplyPlaybackPosition), which despawns and
    /// respawns every object. A change we made while paused must not count as one of its own, so the
    /// snapshot takes the new values too (null while not paused since the last resume: nothing to do).
    /// </summary>
    private static void SyncPauseSnapshot(object ui, float noteJumpSpeed, float? noteJumpValue)
    {
        var snapshot = ui.GetType().GetField("_beforeDeactiveParam", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ui);
        if (snapshot == null)
        {
            return;
        }

        var type = snapshot.GetType();
        type.GetProperty("NJS", BindingFlags.Instance | BindingFlags.Public)?.SetValue(snapshot, noteJumpSpeed);
        if (noteJumpValue is { } offset)
        {
            type.GetProperty("Offset", BindingFlags.Instance | BindingFlags.Public)?.SetValue(snapshot, offset);
        }
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

        if (_practiceUi != null || _uiMissed)
        {
            return _practiceUi;
        }

        var type = FindType(PracticeUiTypeName);
        if (type == null)
        {
            _uiMissed = true;
            Plugin.Log.Info("[Playback] PracticePlugin not loaded; no NJS/offset sync needed");
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
                _uiMissed = true;
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
