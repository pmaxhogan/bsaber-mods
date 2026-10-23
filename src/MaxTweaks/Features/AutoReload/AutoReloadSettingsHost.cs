using BeatSaberMarkupLanguage.Attributes;
using MaxTweaks.Configuration;

namespace MaxTweaks.Features.AutoReload;

/// <summary>BSML host for this feature's tab on the MaxTweaks settings page (UI/Settings.bsml).</summary>
internal class AutoReloadSettingsHost
{
    private readonly AutoReloadConfig _config;

    public AutoReloadSettingsHost(AutoReloadConfig config)
    {
        _config = config;
    }

    [UIValue("enabled")]
    public bool Enabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    [UIValue("debounce-seconds")]
    public float DebounceSeconds
    {
        get => _config.DebounceSeconds;
        set => _config.DebounceSeconds = value;
    }

    [UIValue("show-toasts")]
    public bool ShowToasts
    {
        get => _config.ShowToasts;
        set => _config.ShowToasts = value;
    }
}
