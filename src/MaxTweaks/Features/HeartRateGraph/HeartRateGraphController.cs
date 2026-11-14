using System;
using System.Collections.Generic;
using MaxTweaks.Common;
using MaxTweaks.Common.HeartRate;
using MaxTweaks.Configuration;
using MaxTweaks.Core.HeartRateGraph;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace MaxTweaks.Features.HeartRateGraph;

/// <summary>
/// Gameplay: draws the heart rate over the song. The x axis is the song (left edge = start, right
/// edge = end), the y axis MinBpm..MaxBpm; every reading from <see cref="HeartRateFeed"/> is put at
/// the current song time, so the graph starts empty and fills in as the song plays. Line and fill
/// are coloured by heart rate on the plasma ramp (Core HeartRateGraph).
///
/// The graph sits directly under HRCounter's counter, found by its GameObject name (no assembly
/// reference), and follows it when it rides the rotating HUD of 360 levels; without that counter it
/// uses the fallback position from the config. Nothing is created before the first reading, and
/// nothing at all when the HUD is hidden (the same rule HRCounter follows).
/// </summary>
internal sealed class HeartRateGraphController : IInitializable, ITickable, IDisposable
{
    private const string Feature = "HeartRateGraph";
    private const string HRCounterObjectName = "HRCounter Standalone";

    private const float LineWidthMetres = 0.03f;

    // Redraw rate between readings, and how old a reading may be for the line to keep following the song.
    private const float LiveDrawSeconds = 1f / 30f;
    private const double FreshSeconds = 3.0;

    // Looking for HRCounter's counter: not in the first frames (its text has no mesh to measure yet),
    // then twice a second, and give up after a while when it is not there.
    private const int AnchorFirstTick = 3;
    private const float AnchorRetrySeconds = 0.5f;
    private const float AnchorGiveUpSeconds = 5f;

    // HRCounter draws its heart and number well above its own origin (about 1.3 m at its scale), so
    // the graph goes under what is drawn, not under the origin. A measurement further away than this
    // is not believed; the graph then hangs this far under the origin instead.
    private const float MaxPlausibleCounterOffset = 5f;
    private const float AssumedCounterDrop = 0.15f;

    private readonly HeartRateGraphConfig _config;
    private readonly HeartRateFeed _feed;
    private readonly AudioTimeSyncController _audio;
    private readonly CoreGameHUDController.InitData? _hudInitData;
    private readonly HrSeries _series = new();
    private readonly HrGraphLayout _layout = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<HrSample> _drawn = new();

    private HeartRateGraphView? _view;
    private Vector2 _sizeMetres;
    private bool _listening;
    private bool _placed;
    private int _ticksSinceView;
    private float _viewCreatedAt;
    private float _nextAnchorAttempt;
    private int _drawnVersion = -1;
    private double _drawnTime = double.NaN;
    private float _nextLiveDraw;

    public HeartRateGraphController(
        HeartRateGraphConfig config,
        HeartRateFeed feed,
        AudioTimeSyncController audio,
        [InjectOptional] CoreGameHUDController.InitData? hudInitData)
    {
        _config = config;
        _feed = feed;
        _audio = audio;
        _hudInitData = hudInitData;
    }

    public void Initialize() => Guard.Run(Feature, () =>
    {
        if (_hudInitData is { hide: true })
        {
            Plugin.Log.Info($"[{Feature}] off for this song: the HUD is hidden");
            return;
        }

        var sane = _config.MaxBpm > _config.MinBpm;
        _layout.MinBpm = sane ? _config.MinBpm : 100;
        _layout.MaxBpm = sane ? _config.MaxBpm : 200;
        _sizeMetres = new Vector2(Mathf.Clamp(_config.Width, 0.2f, 6f), Mathf.Clamp(_config.Height, 0.05f, 3f));

        _feed.Reading += HandleReading;
        _listening = true;
    });

    public void Tick()
    {
        if (_view == null)
        {
            return;
        }

        if (!_placed)
        {
            Guard.Run(Feature, TryAnchor);
        }

        // The strap reports about once a second; in between, the line follows the song at the last
        // heart rate so the graph moves smoothly instead of in one-second steps.
        if (Time.unscaledTime >= _nextLiveDraw)
        {
            _nextLiveDraw = Time.unscaledTime + LiveDrawSeconds;
            Guard.Run(Feature, Draw);
        }
    }

    public void Dispose() => Guard.Run(Feature, () =>
    {
        if (_listening)
        {
            _feed.Reading -= HandleReading;
            _listening = false;
            Plugin.Log.Debug($"[{Feature}] song ended with {_series.Samples.Count} samples");
        }

        _view?.Dispose();
        _view = null;
    });

    private void HandleReading(int bpm)
    {
        // songEndTime needs the clip and the audio latency, both set in AudioTimeSyncController.Start.
        if (!_audio.isReady)
        {
            return;
        }

        var length = _audio.songEndTime > 0f ? _audio.songEndTime : _audio.songLength;
        if (length <= 0f)
        {
            return;
        }

        _series.Add(_audio.songTime, bpm);
        _view ??= CreateView();
        _layout.SongLength = length;
        Draw();
    }

    private void Draw()
    {
        var view = _view;
        var samples = _series.Samples;
        if (view == null || samples.Count == 0)
        {
            return;
        }

        _drawn.Clear();
        _drawn.AddRange(samples);
        var last = samples[samples.Count - 1];
        var now = (double)_audio.songTime;
        if (now > last.Time && now - last.Time <= HrSeries.DefaultMaxGapSeconds && _feed.TryGetFresh(FreshSeconds, out _))
        {
            _drawn.Add(new HrSample(now, last.Bpm, false));
        }

        var time = _drawn[_drawn.Count - 1].Time;
        if (_series.Version == _drawnVersion && Math.Abs(time - _drawnTime) < 1e-3)
        {
            return;
        }

        _drawnVersion = _series.Version;
        _drawnTime = time;
        HrGraphGeometry.Build(view.Mesh, _drawn, _layout);
        view.Refresh();
    }

    private HeartRateGraphView CreateView()
    {
        var fallback = new Vector3(_config.FallbackX, _config.FallbackY, _config.FallbackZ);
        var view = new HeartRateGraphView(_sizeMetres, fallback, Quaternion.identity);
        _layout.Width = view.Size.x;
        _layout.Height = view.Size.y;
        _layout.LineWidth = LineWidthMetres / Common.Pointing.WorldCanvas.Scale;
        _viewCreatedAt = Time.unscaledTime;
        _ticksSinceView = 0;
        _nextAnchorAttempt = 0f;
        return view;
    }

    private void TryAnchor()
    {
        var view = _view;
        if (view == null || ++_ticksSinceView < AnchorFirstTick || Time.unscaledTime < _nextAnchorAttempt)
        {
            return;
        }

        _nextAnchorAttempt = Time.unscaledTime + AnchorRetrySeconds;
        var counter = GameObject.Find(HRCounterObjectName);
        if (counter != null)
        {
            var transform = counter.transform;
            var bottom = MeasureBottom(transform, out var measured, out var extent);
            var center = bottom - (transform.up * (Mathf.Max(0f, _config.GapBelowHRCounter) + (_sizeMetres.y * 0.5f)));
            view.Place(center, transform.rotation, transform.parent);
            _placed = true;
            Plugin.Log.Info(
                $"[{Feature}] shown under HRCounter: counter origin {Format(transform.position)}, its lowest point {Format(bottom)}"
                + $" ({(measured ? "measured, counter is " + extent : "assumed")}), graph centre {Format(view.Position)}, {_sizeMetres.x:0.00} x {_sizeMetres.y:0.00} m");
            return;
        }

        if (Time.unscaledTime - _viewCreatedAt >= AnchorGiveUpSeconds)
        {
            _placed = true;
            Plugin.Log.Info(
                $"[{Feature}] shown at the fallback position {Format(view.Position)}, {_sizeMetres.x:0.00} x {_sizeMetres.y:0.00} m:"
                + " HRCounter's counter is not on screen");
        }
    }

    /// <summary>
    /// World position of the middle of the counter's bottom edge: the lowest point of what it draws
    /// (text glyphs and images), measured in the counter's own axes so a rotated HUD works too.
    /// </summary>
    private Vector3 MeasureBottom(Transform counter, out bool measured, out string extent)
    {
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var graphic in counter.GetComponentsInChildren<Graphic>(false))
        {
            if (!graphic.isActiveAndEnabled)
            {
                continue;
            }

            if (graphic is TMP_Text text && text.textBounds.size.y > 0f)
            {
                // The glyphs themselves: a text box is usually much taller than its text.
                var bounds = text.textBounds;
                var textTransform = text.transform;
                _corners[0] = textTransform.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, 0f));
                _corners[1] = textTransform.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, 0f));
                _corners[2] = textTransform.TransformPoint(new Vector3(bounds.max.x, bounds.max.y, 0f));
                _corners[3] = textTransform.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, 0f));
            }
            else
            {
                graphic.rectTransform.GetWorldCorners(_corners);
            }

            foreach (var corner in _corners)
            {
                var local = counter.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x);
                maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y);
                maxY = Mathf.Max(maxY, local.y);
            }
        }

        if (minY < float.PositiveInfinity)
        {
            var bottom = counter.TransformPoint(new Vector3((minX + maxX) * 0.5f, minY, 0f));
            if ((bottom - counter.position).magnitude <= MaxPlausibleCounterOffset)
            {
                var scale = counter.lossyScale;
                extent = FormattableString.Invariant($"{(maxX - minX) * scale.x:0.00} x {(maxY - minY) * scale.y:0.00} m");
                measured = true;
                return bottom;
            }
        }

        extent = string.Empty;
        measured = false;
        return counter.position - (counter.up * AssumedCounterDrop);
    }

    private static string Format(Vector3 v) =>
        FormattableString.Invariant($"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})");
}
