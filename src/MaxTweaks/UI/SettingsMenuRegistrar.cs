using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Settings;
using MaxTweaks.Common;
using MaxTweaks.Configuration;
using MaxTweaks.Features.ActivePause;
using MaxTweaks.Features.AutoReload;
using MaxTweaks.Features.Blink;
using MaxTweaks.Features.Chapters;
using MaxTweaks.Features.DifficultySwitch;
using MaxTweaks.Features.HeartRateBridge;
using MaxTweaks.Features.HeartRateGraph;
using MaxTweaks.Features.HideOst;
using MaxTweaks.Features.ObsClips;
using MaxTweaks.Features.ObsRecording;
using Zenject;

namespace MaxTweaks.UI;

/// <summary>
/// Registers the Mod Settings page (UI/Settings.bsml) and the quick-toggle tab under the song
/// preview (UI/QuickTab.bsml). Bound in the menu container: BSML's singletons only exist there,
/// and the menu is rebuilt on every soft restart, so register on Initialize and remove on Dispose.
/// </summary>
internal class SettingsMenuRegistrar : IInitializable, IDisposable
{
    private const string Name = "MaxTweaks";

    private readonly PluginConfig _config;
    private readonly FeatureStatus _status;

    public SettingsMenuRegistrar(
        PluginConfig config,
        FeatureStatus status,
        HideOstSettingsHost hideOst,
        AutoReloadSettingsHost autoReload,
        BlinkSettingsHost blink,
        ChaptersSettingsHost chapters,
        DifficultySwitchSettingsHost difficultySwitch,
        ActivePauseSettingsHost activePause,
        HeartRateBridgeSettingsHost heartRateBridge,
        HeartRateGraphSettingsHost heartRateGraph,
        ObsRecordingSettingsHost obsRecording,
        ObsClipsSettingsHost obsClips)
    {
        _config = config;
        _status = status;
        HideOst = hideOst;
        AutoReload = autoReload;
        Blink = blink;
        Chapters = chapters;
        DifficultySwitch = difficultySwitch;
        ActivePause = activePause;
        HeartRateBridge = heartRateBridge;
        HeartRateGraph = heartRateGraph;
        ObsRecording = obsRecording;
        ObsClips = obsClips;
    }

    // Per-feature hosts, used by <macro.as-host> in Settings.bsml.
    [UIValue("hide-ost")]
    public HideOstSettingsHost HideOst { get; }

    [UIValue("auto-reload")]
    public AutoReloadSettingsHost AutoReload { get; }

    [UIValue("blink")]
    public BlinkSettingsHost Blink { get; }

    [UIValue("chapters")]
    public ChaptersSettingsHost Chapters { get; }

    [UIValue("difficulty-switch")]
    public DifficultySwitchSettingsHost DifficultySwitch { get; }

    [UIValue("active-pause")]
    public ActivePauseSettingsHost ActivePause { get; }

    [UIValue("heart-rate-bridge")]
    public HeartRateBridgeSettingsHost HeartRateBridge { get; }

    [UIValue("heart-rate-graph")]
    public HeartRateGraphSettingsHost HeartRateGraph { get; }

    [UIValue("obs-recording")]
    public ObsRecordingSettingsHost ObsRecording { get; }

    [UIValue("obs-clips")]
    public ObsClipsSettingsHost ObsClips { get; }

    [UIValue("status-text")]
    public string StatusText => _status.Failures.Count == 0
        ? string.Empty
        : "<color=#ff8080>Disabled after an error: " + string.Join(", ", _status.Failures.Keys) + " (see log)</color>";

    // Quick tab: master switches only.
    [UIValue("quick-hide-ost")]
    public bool QuickHideOst { get => _config.HideOst.Enabled; set => HideOst.Enabled = value; }

    [UIValue("quick-auto-reload")]
    public bool QuickAutoReload { get => _config.AutoReload.Enabled; set => _config.AutoReload.Enabled = value; }

    [UIValue("quick-blink")]
    public bool QuickBlink { get => _config.Blink.Enabled; set => _config.Blink.Enabled = value; }

    [UIValue("quick-chapters")]
    public bool QuickChapters { get => _config.Chapters.Enabled; set => _config.Chapters.Enabled = value; }

    [UIValue("quick-difficulty-switch")]
    public bool QuickDifficultySwitch { get => _config.DifficultySwitch.Enabled; set => _config.DifficultySwitch.Enabled = value; }

    [UIValue("quick-active-pause")]
    public bool QuickActivePause { get => _config.ActivePause.Enabled; set => _config.ActivePause.Enabled = value; }

    [UIValue("quick-heart-rate-bridge")]
    public bool QuickHeartRateBridge { get => _config.HeartRateBridge.Enabled; set => _config.HeartRateBridge.Enabled = value; }

    [UIValue("quick-heart-rate-graph")]
    public bool QuickHeartRateGraph { get => _config.HeartRateGraph.Enabled; set => _config.HeartRateGraph.Enabled = value; }

    [UIValue("quick-obs-recording")]
    public bool QuickObsRecording { get => _config.ObsRecording.Enabled; set => _config.ObsRecording.Enabled = value; }

    [UIValue("quick-obs-clips")]
    public bool QuickObsClips { get => _config.ObsClips.Enabled; set => _config.ObsClips.Enabled = value; }

    public void Initialize()
    {
        Guard.Run(Name, () =>
        {
            BSMLSettings.Instance.AddSettingsMenu(Name, "MaxTweaks.UI.Settings.bsml", this);
            GameplaySetup.Instance.AddTab(Name, "MaxTweaks.UI.QuickTab.bsml", this);
        });
    }

    public void Dispose()
    {
        Guard.Run(Name, () =>
        {
            BSMLSettings.Instance?.RemoveSettingsMenu(this);
            GameplaySetup.Instance?.RemoveTab(Name);
        });
    }
}
