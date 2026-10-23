using System;
using MaxTweaks.Common;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Gameplay: never reloads. Moves settled changes aside for the menu and, if the map being played
/// changed on disk, shows one toast per play. The menu reloads it after the level ends.
/// </summary>
internal sealed class GameplayChangeNotifier : IInitializable, ITickable, IDisposable
{
    private const string Feature = "AutoReload";
    private const float TickInterval = 0.5f;

    private readonly AutoReloadService _service;
    private readonly AutoReloadConfig _config;
    private readonly IToastService _toasts;
    private readonly GameplayCoreSceneSetupData? _setupData;

    private string _songName = string.Empty;
    private float _nextTick;

    public GameplayChangeNotifier(
        AutoReloadService service,
        AutoReloadConfig config,
        IToastService toasts,
        [InjectOptional] GameplayCoreSceneSetupData? setupData)
    {
        _service = service;
        _config = config;
        _toasts = toasts;
        _setupData = setupData;
    }

    public void Initialize()
    {
        Guard.Run(Feature, () =>
        {
            var level = _setupData?.beatmapLevel;
            _songName = level?.songName ?? string.Empty;
            _service.EnterGameplay(SongCoreBridge.FolderOfLevel(level?.levelID));
        });
    }

    public void Tick()
    {
        var now = Time.unscaledTime;
        if (now < _nextTick)
        {
            return;
        }

        _nextTick = now + TickInterval;
        if (!_config.Enabled || !_service.Aggregator.HasWork)
        {
            return;
        }

        Guard.Run(Feature, () =>
        {
            var batch = _service.Aggregator.TryFlush(_service.DebounceSeconds);
            if (batch == null)
            {
                return;
            }

            _service.Defer(batch);
            if (_service.PlayingMap.ShouldNotify(batch) && _config.ShowToasts)
            {
                _toasts.Show($"Map updated on disk: {_songName} - reloads when you exit", 5f);
            }
        });
    }

    public void Dispose() => Guard.Run(Feature, _service.ExitGameplay);
}
