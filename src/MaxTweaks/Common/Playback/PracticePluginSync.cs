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

    private readonly DiContainer _container;
    private bool _looked;
    private object? _songSeeker;
    private PropertyInfo? _playbackPosition;
    private FieldInfo? _startTimeSamples;

    public PracticePluginSync(DiContainer container)
    {
        _container = container;
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
