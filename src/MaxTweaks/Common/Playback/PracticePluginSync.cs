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
    private bool _lookedUi;
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
    /// difficulty's values. Backing fields are written directly so no PropertyChanged fires (that
    /// would re-init spawning again); its sliders show the new values the next time they refresh.
    /// </summary>
    public void AfterDifficultySwap(float noteJumpSpeed, float noteJumpValue)
    {
        if (!_lookedUi)
        {
            _lookedUi = true;
            var type = Type.GetType(PracticeUiTypeName, throwOnError: false);
            if (type != null)
            {
                _practiceUi = _container.TryResolve(type);
            }
        }

        if (_practiceUi == null)
        {
            return;
        }

        var written = 0;
        foreach (var name in new[] { "_defaultNJS", "_njs" })
        {
            written += TrySet(_practiceUi, name, noteJumpSpeed) ? 1 : 0;
        }

        foreach (var name in new[] { "_defaultOffset", "_offset" })
        {
            written += TrySet(_practiceUi, name, noteJumpValue) ? 1 : 0;
        }

        if (written != 4)
        {
            Plugin.Log.Warn($"[Playback] PracticePlugin PracticeUI fields changed ({written}/4 written); its NJS/offset panel may re-apply the old difficulty's values");
        }
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
