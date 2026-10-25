using System;
using MaxTweaks.Common;
using MaxTweaks.Configuration;
using MaxTweaks.Core.Blink;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// Menu-side "Test blink": plays one blink (the configured length, strength and overlay mode) on the
/// main camera right away, so the look can be checked in the headset without waiting for a real
/// one in a song. Bound in the menu container; the overlay is rebuilt on every start so changed
/// settings apply.
/// </summary>
internal sealed class BlinkPreview : ITickable, IDisposable
{
    private const float FadeSeconds = 0.3f;

    private readonly BlinkConfig _config;
    private readonly BlinkAssets _assets;
    private BlinkOverlay? _overlay;
    private float _elapsed;
    private bool _running;

    public BlinkPreview(BlinkConfig config, BlinkAssets assets)
    {
        _config = config;
        _assets = assets;
    }

    public void Start()
    {
        Guard.Run("Blink", () =>
        {
            _overlay?.Dispose();
            _overlay = new BlinkOverlay(_assets, _config.VignetteStrength, _config.ScreenSpaceOverlay);
            _elapsed = 0f;
            _running = true;
            Plugin.Log.Info($"[Blink] test blink: {_config.DurationSeconds:0.#} s, strength {_config.VignetteStrength:0.##}, {(_config.ScreenSpaceOverlay ? "screen-space" : "world")} overlay");
        });
    }

    public void Tick()
    {
        if (!_running || _overlay == null)
        {
            return;
        }

        Guard.Run("Blink", () =>
        {
            _elapsed += Time.unscaledDeltaTime;
            var alpha = BlinkEnvelope.Alpha(_elapsed, _config.DurationSeconds, FadeSeconds);
            _overlay.SetAlpha(alpha);
            if (_elapsed >= _config.DurationSeconds)
            {
                _running = false;
                _overlay.SetAlpha(0f);
            }
        });
    }

    public void Dispose()
    {
        _overlay?.Dispose();
        _overlay = null;
        _running = false;
    }
}
