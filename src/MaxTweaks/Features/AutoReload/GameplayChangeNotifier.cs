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
    private const int FolderLookupRetries = 10;

    private readonly AutoReloadService _service;
    private readonly AutoReloadConfig _config;
    private readonly IToastService _toasts;
    private readonly GameplayCoreSceneSetupData? _setupData;

    private BeatmapLevel? _level;
    private string _songName = string.Empty;
    private int _session;
    private int _lookupRetries;
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
            _level = _setupData?.beatmapLevel;
            _songName = _level?.songName ?? string.Empty;
            var folder = ResolveFolder();
            _session = _service.EnterGameplay(folder);
            _lookupRetries = folder == null && IsCustom(_level) ? FolderLookupRetries : 0;
            Plugin.Log.Debug($"[{Feature}] Gameplay started (session {_session}): level '{_level?.levelID}', folder '{folder ?? "(none)"}'");
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
        if (!_config.Enabled)
        {
            return;
        }

        if (_lookupRetries > 0)
        {
            Guard.Run(Feature, RetryFolderLookup);
        }

        if (!_service.Aggregator.HasWork)
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
            var notify = _service.PlayingMap.ShouldNotify(batch);
            Plugin.Log.Debug($"[{Feature}] Gameplay change: {batch.Folders.Count} folder(s){(batch.Rescan ? " + rescan" : string.Empty)} [{string.Join("; ", batch.Folders)}], playing '{_service.PlayingMap.PlayingFolder ?? "(none)"}', notify={notify}, toasts={_config.ShowToasts}");
            if (notify && _config.ShowToasts)
            {
                _toasts.Show($"Map updated on disk: {_songName} - reloads when you exit", 5f);
            }
        });
    }

    // Only this session's own exit counts: on a restart the next player container may be set up
    // before this one is torn down, and must not be switched off by it.
    public void Dispose() => Guard.Run(Feature, () => _service.ExitGameplay(_session));

    private static bool IsCustom(BeatmapLevel? level) =>
        level != null && level.levelID.StartsWith(CustomLevelLoader.kCustomLevelPrefixId, StringComparison.Ordinal);

    private string? ResolveFolder() => SongCoreBridge.FolderOfLevel(_level) ?? _service.FolderOfRetiredLevel(_level?.levelID);

    private void RetryFolderLookup()
    {
        _lookupRetries--;
        var folder = ResolveFolder();
        if (folder == null)
        {
            if (_lookupRetries == 0)
            {
                Plugin.Log.Debug($"[{Feature}] No folder found for playing level '{_level?.levelID}'; no in-game toast for it");
            }

            return;
        }

        _lookupRetries = 0;
        var changedAlready = _service.SetPlayingFolder(_session, folder);
        Plugin.Log.Debug($"[{Feature}] Playing folder resolved late: '{folder}', changed already={changedAlready}");
        if (changedAlready && _config.ShowToasts)
        {
            _toasts.Show($"Map updated on disk: {_songName} - reloads when you exit", 5f);
        }
    }
}
